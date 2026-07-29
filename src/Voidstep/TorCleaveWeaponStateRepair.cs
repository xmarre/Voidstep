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
    /// Replaces the provisional per-tick Cleave stance release with a one-shot handoff.
    /// TOR owns cached wielded items and weapon-key bindings while ability mode is active;
    /// repeatedly calling DisableAbilityMode can therefore keep weapon selection disabled.
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
            internal bool ReleasedForCurrentSelection;
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
                    throw new MissingMethodException("Voidstep Cleave stance repair patch surface is incomplete.");

                var harmony = new Harmony(HarmonyId);
                harmony.Unpatch(target, obsoletePostfix);

                var info = Harmony.GetPatchInfo(target);
                var alreadyPatched = info != null && info.Postfixes.Any(
                    patch => patch.PatchMethod == replacementPostfix);
                if (!alreadyPatched)
                    harmony.Patch(target, postfix: new HarmonyMethod(replacementPostfix));

                _installed = true;
                Logger.Info("Replaced repeated TOR Cleave stance cleanup with one-shot weapon-state restoration.");
            }
            catch (Exception ex)
            {
                Logger.Error("TOR Cleave weapon-state repair could not be installed.", ex);
            }
        }

        private static void AfterAdapterTick(TorAbilityWheelAdapter __instance)
        {
            if (__instance == null)
                return;

            var state = States.GetOrCreateValue(__instance);
            var selection = Selection(__instance);
            var cleaveSelected = selection != null &&
                                 selection.SelectedAbility.HasValue &&
                                 selection.SelectedAbility.Value == AbilityId.VoidstepCleave;

            if (!cleaveSelected)
            {
                state.ReleasedForCurrentSelection = false;
                state.RestoreFailureLogged = false;
                return;
            }

            if (!__instance.OwnsTargeting)
                return;

            if (state.ReleasedForCurrentSelection)
            {
                // TOR can expose its previous targeting state for one additional tick.
                // Do not call its native cleanup path again; only clear Voidstep's stale
                // ownership mirror so combat input and confirmation remain available.
                TargetingOwned(__instance) = false;
                return;
            }

            state.ReleasedForCurrentSelection = true;
            __instance.CloseTargetingMode();
            RestoreTorWeaponState(__instance, state);
        }

        private static void RestoreTorWeaponState(TorAbilityWheelAdapter adapter, State state)
        {
            try
            {
                var logic = Logic(adapter);
                if (logic == null)
                    throw new InvalidOperationException("TOR ability-manager logic is unavailable after Cleave selection.");

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

                Logger.Debug("TOR Cleave selection restored cached wielded items and weapon-key bindings once.");
            }
            catch (Exception ex)
            {
                if (state.RestoreFailureLogged)
                    return;
                state.RestoreFailureLogged = true;
                Logger.Error("TOR Cleave weapon-state restoration failed.", Unwrap(ex));
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
