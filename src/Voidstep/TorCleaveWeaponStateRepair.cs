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
    /// Performs a one-shot TOR targeting handoff for every Voidstep proxy and restores TOR's
    /// cached wielded items and weapon bindings. Targeting release and restoration success are
    /// tracked separately so a transient restoration failure can be retried without reopening or
    /// closing TOR targeting again.
    /// </summary>
    internal static class TorCleaveWeaponStateRepair
    {
        private const string HarmonyId = "xmarre.voidstep";
        private static readonly VoidstepLogger Logger = new VoidstepLogger();
        private static readonly ConditionalWeakTable<TorAbilityWheelAdapter, State> States =
            new ConditionalWeakTable<TorAbilityWheelAdapter, State>();

        private static readonly AccessTools.FieldRef<TorAbilityWheelAdapter, AbilitySelectionController> Selection =
            AccessTools.FieldRefAccess<TorAbilityWheelAdapter, AbilitySelectionController>("_selection");
        private static readonly AccessTools.FieldRef<AbilitySelectionController, Mission> SelectionMission =
            AccessTools.FieldRefAccess<AbilitySelectionController, Mission>("_mission");
        private static readonly AccessTools.FieldRef<TorAbilityWheelAdapter, object> Logic =
            AccessTools.FieldRefAccess<TorAbilityWheelAdapter, object>("_logic");
        private static readonly AccessTools.FieldRef<TorAbilityWheelAdapter, bool> TargetingOwned =
            AccessTools.FieldRefAccess<TorAbilityWheelAdapter, bool>("_targetingOwned");

        private static bool _installed;

        private sealed class State
        {
            internal AbilityId? Ability;
            internal bool TargetingReleased;
            internal bool WeaponStateRestored;
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
                var replacementPostfix = AccessTools.Method(
                    typeof(TorCleaveWeaponStateRepair),
                    nameof(AfterAdapterTick));

                if (target == null || replacementPostfix == null)
                    throw new MissingMethodException("Voidstep TOR weapon-state repair patch surface is incomplete.");

                var harmony = new Harmony(HarmonyId);
                var info = Harmony.GetPatchInfo(target);
                var alreadyPatched = info != null && info.Postfixes.Any(
                    patch => patch.PatchMethod == replacementPostfix);
                if (!alreadyPatched)
                {
                    var method = new HarmonyMethod(replacementPostfix)
                    {
                        priority = Priority.Last
                    };
                    harmony.Patch(target, postfix: method);
                }

                _installed = true;
                Logger.Info("Installed one-shot TOR targeting, stance and weapon-state restoration for every Voidstep proxy ability.");
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
                ResetState(state);
                return;
            }

            if (!state.Ability.HasValue || state.Ability.Value != selectedAbility.Value)
            {
                ResetState(state);
                state.Ability = selectedAbility.Value;
            }

            var mission = selection == null ? null : SelectionMission(selection);
            var actor = mission?.MainAgent;

            if (!state.TargetingReleased)
            {
                if (!__instance.OwnsTargeting)
                    return;

                TorProxyCastStanceFix.ReleaseBeforeVoidstepActivation(actor);
                __instance.CloseTargetingMode();
                TorProxyCastStanceFix.ReleaseBeforeVoidstepActivation(actor);
                state.TargetingReleased = true;
            }
            else if (__instance.OwnsTargeting)
            {
                // TOR can expose its previous targeting state for one additional tick.
                TargetingOwned(__instance) = false;
                TorProxyCastStanceFix.ReleaseBeforeVoidstepActivation(actor);
            }

            if (!state.WeaponStateRestored)
            {
                state.WeaponStateRestored = RestoreTorWeaponState(
                    __instance,
                    state,
                    selectedAbility.Value);
            }
        }

        private static bool RestoreTorWeaponState(
            TorAbilityWheelAdapter adapter,
            State state,
            AbilityId ability)
        {
            try
            {
                var logic = Logic(adapter);
                if (logic == null)
                    throw new InvalidOperationException("TOR ability-manager logic is unavailable after Voidstep selection.");

                var logicType = logic.GetType();
                var updateWieldedItems = FindInstanceMethod(logicType, "UpdateWieldedItems");
                var bindWeaponKeys = FindInstanceMethod(logicType, "BindWeaponKeys");
                if (updateWieldedItems == null || bindWeaponKeys == null)
                {
                    throw new MissingMethodException(
                        "TOR did not expose both UpdateWieldedItems() and BindWeaponKeys().");
                }

                updateWieldedItems.Invoke(logic, null);
                bindWeaponKeys.Invoke(logic, null);
                state.RestoreFailureLogged = false;
                Logger.Debug("TOR " + ability + " selection restored cached wielded items and weapon-key bindings once.");
                return true;
            }
            catch (Exception ex)
            {
                if (!state.RestoreFailureLogged)
                {
                    state.RestoreFailureLogged = true;
                    Logger.Error("TOR Voidstep weapon-state restoration failed for " + ability + "; retrying on a later tick.", Unwrap(ex));
                }
                return false;
            }
        }

        private static MethodInfo FindInstanceMethod(Type type, string name)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var method = current.GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method != null)
                    return method;
            }
            return null;
        }

        private static void ResetState(State state)
        {
            state.Ability = null;
            state.TargetingReleased = false;
            state.WeaponStateRestored = false;
            state.RestoreFailureLogged = false;
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