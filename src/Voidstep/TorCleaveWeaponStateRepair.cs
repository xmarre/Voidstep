using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    /// <summary>
    /// Replaces the provisional per-tick TOR stance release with a one-shot handoff.
    /// TOR owns cached wielded items and weapon-key bindings while ability mode is active;
    /// every Voidstep proxy selection must therefore leave TOR targeting immediately after
    /// Voidstep has accepted the selection, not only Voidstep Cleave.
    /// </summary>
    internal static class TorCleaveWeaponStateRepair
    {
        private const string HarmonyId = "xmarre.voidstep";
        private static readonly VoidstepLogger Logger = new VoidstepLogger();
        private static readonly ConditionalWeakTable<TorAbilityWheelAdapter, State> States =
            new ConditionalWeakTable<TorAbilityWheelAdapter, State>();

        private static readonly AccessTools.FieldRef<TorAbilityWheelAdapter, AbilitySelectionController> Selection =
            AccessTools.FieldRefAccess<TorAbilityWheelAdapter, AbilitySelectionController>("_selection");
        private static readonly AccessTools.FieldRef<TorAbilityWheelAdapter, object> Logic =
            AccessTools.FieldRefAccess<TorAbilityWheelAdapter, object>("_logic");
        private static readonly AccessTools.FieldRef<TorAbilityWheelAdapter, bool> TargetingOwned =
            AccessTools.FieldRefAccess<TorAbilityWheelAdapter, bool>("_targetingOwned");

        private static bool _installed;

        private sealed class State
        {
            internal AbilityId? ReleasedAbility;
            internal bool RestoreFailureLogged;
        }

        internal static void Install()
        {
            if (_installed)
                return;

            try
            {
                var target = AccessTools.Method(
                    typeof(TorAbilityWheelAdapter),
                    nameof(TorAbilityWheelAdapter.Tick),
                    new[] { typeof(float) });
                var obsoletePostfix = AccessTools.Method(
                    typeof(TorCleaveStanceReleasePatch),
                    "Postfix");
                var replacementPostfix = AccessTools.Method(
                    typeof(TorCleaveWeaponStateRepair),
                    nameof(AfterAdapterTick));

                if (target == null || obsoletePostfix == null || replacementPostfix == null)
                    throw new MissingMethodException("Voidstep TOR weapon-state repair patch surface is incomplete.");

                var harmony = new Harmony(HarmonyId);
                harmony.Unpatch(target, obsoletePostfix);

                var info = Harmony.GetPatchInfo(target);
                var alreadyPatched = info != null && info.Postfixes.Any(
                    patch => patch.PatchMethod == replacementPostfix);
                if (!alreadyPatched)
                    harmony.Patch(target, postfix: new HarmonyMethod(replacementPostfix));

                _installed = true;
                Logger.Info("Replaced repeated TOR stance cleanup with one-shot Voidstep weapon-state restoration for every proxy ability.");
            }
            catch (Exception ex)
            {
                Logger.Error("TOR Voidstep weapon-state repair could not be installed.", ex);
            }
        }

        private static void AfterAdapterTick(TorAbilityWheelAdapter __instance)
        {
            if (__instance == null)
                return;

            var state = States.GetOrCreateValue(__instance);
            var selection = Selection(__instance);
            var selectedAbility = selection?.SelectedAbility;

            if (!selectedAbility.HasValue)
            {
                state.ReleasedAbility = null;
                state.RestoreFailureLogged = false;
                return;
            }

            if (state.ReleasedAbility.HasValue && state.ReleasedAbility.Value != selectedAbility.Value)
            {
                state.ReleasedAbility = null;
                state.RestoreFailureLogged = false;
            }

            if (!__instance.OwnsTargeting)
                return;

            if (state.ReleasedAbility.HasValue && state.ReleasedAbility.Value == selectedAbility.Value)
            {
                // TOR can expose its previous targeting state for one additional tick.
                // Do not call its native cleanup path again; only clear Voidstep's stale
                // ownership mirror so combat input and confirmation remain available.
                TargetingOwned(__instance) = false;
                return;
            }

            state.ReleasedAbility = selectedAbility.Value;
            __instance.CloseTargetingMode();
            RestoreTorWeaponState(__instance, state, selectedAbility.Value);
        }

        private static void RestoreTorWeaponState(TorAbilityWheelAdapter adapter, State state, AbilityId ability)
        {
            try
            {
                var logic = Logic(adapter);
                if (logic == null)
                    throw new InvalidOperationException("TOR ability-manager logic is unavailable after Voidstep selection.");

                var logicType = logic.GetType();
                var updateWieldedItems = logicType.GetMethod(
                    "UpdateWieldedItems",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
                var bindWeaponKeys = logicType.GetMethod(
                    "BindWeaponKeys",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                updateWieldedItems?.Invoke(logic, null);
                bindWeaponKeys?.Invoke(logic, null);

                if (updateWieldedItems == null || bindWeaponKeys == null)
                {
                    throw new MissingMethodException(
                        "TOR did not expose both UpdateWieldedItems() and BindWeaponKeys().");
                }

                Logger.Debug("TOR " + ability + " selection restored cached wielded items and weapon-key bindings once.");
            }
            catch (Exception ex)
            {
                if (state.RestoreFailureLogged)
                    return;
                state.RestoreFailureLogged = true;
                Logger.Error("TOR Voidstep weapon-state restoration failed for " + ability + ".", Unwrap(ex));
            }
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }
    }

    [HarmonyPatch(typeof(VoidstepMissionBehavior), nameof(VoidstepMissionBehavior.EarlyStart))]
    internal static class TorCleaveWeaponStateRepairInstallerPatch
    {
        private static void Postfix()
        {
            TorCleaveWeaponStateRepair.Install();
        }
    }
}
