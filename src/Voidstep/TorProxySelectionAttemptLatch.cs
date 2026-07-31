using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Voidstep
{
    /// <summary>
    /// TOR keeps state 2 active when a Voidstep selection is rejected, for example because Blink
    /// is still on cooldown. The adapter previously retried Select every mission tick. This latch
    /// permits one selection attempt per concrete TOR targeting session and resets as soon as TOR
    /// leaves state 2 or changes to another proxy.
    /// </summary>
    [HarmonyPatch(typeof(TorAbilityWheelAdapter), nameof(TorAbilityWheelAdapter.Tick))]
    internal static class TorProxySelectionAttemptLatch
    {
        private static readonly ConditionalWeakTable<TorAbilityWheelAdapter, State> States =
            new ConditionalWeakTable<TorAbilityWheelAdapter, State>();

        private sealed class State
        {
            internal bool Attempted;
            internal AbilityId Ability;
            internal object Proxy;
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            TorAbilityWheelAdapter __instance,
            object ____component,
            object ____logic,
            PropertyInfo ____currentAbilityProperty,
            PropertyInfo ____currentStateProperty)
        {
            if (__instance == null || ____component == null || ____logic == null ||
                ____currentAbilityProperty == null || ____currentStateProperty == null)
            {
                Reset(__instance);
                return true;
            }

            try
            {
                var torState = Convert.ToInt32(
                    ____currentStateProperty.GetValue(____logic, null));
                var proxy = ____currentAbilityProperty.GetValue(____component, null);
                AbilityId ability;
                if (torState != 2 ||
                    !__instance.TryGetProxyAbility(proxy, out ability))
                {
                    Reset(__instance);
                    return true;
                }

                var state = States.GetOrCreateValue(__instance);
                if (state.Attempted && state.Ability == ability &&
                    ReferenceEquals(state.Proxy, proxy))
                {
                    return false;
                }

                state.Attempted = true;
                state.Ability = ability;
                state.Proxy = proxy;
                return true;
            }
            catch
            {
                Reset(__instance);
                return true;
            }
        }

        private static void Reset(TorAbilityWheelAdapter adapter)
        {
            if (adapter != null)
                States.Remove(adapter);
        }
    }
}
