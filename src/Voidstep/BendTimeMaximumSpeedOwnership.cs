using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// AgentDrivenProperties alone cannot exceed Bannerlord's native per-agent maximum
    /// speed cap. Bend Time therefore owns that multiplier for the player and current mount.
    /// Existing limits are refreshed when another system changes them and are restored only
    /// while the live value still equals Voidstep's applied value.
    /// </summary>
    internal static class BendTimeMaximumSpeedOwnership
    {
        private sealed class State
        {
            internal Agent Player;
            internal Agent Mount;
            internal float Factor = 1f;
            internal float OriginalPlayerLimit;
            internal float AppliedPlayerLimit;
            internal bool PlayerApplied;
            internal float OriginalMountLimit;
            internal float AppliedMountLimit;
            internal bool MountApplied;
            internal bool FailureLogged;
        }

        private static readonly ConditionalWeakTable<TimeControlService, State> States =
            new ConditionalWeakTable<TimeControlService, State>();
        private static readonly VoidstepLogger Logger = new VoidstepLogger();

        internal static void Begin(TimeControlService service, Agent player, float requestedFactor, bool succeeded)
        {
            if (service == null)
                return;

            if (!succeeded)
            {
                Restore(service, false);
                return;
            }

            var state = States.GetOrCreateValue(service);
            RestoreState(state);
            state.Player = player;
            state.Factor = Math.Max(0.001f, Math.Min(1f, requestedFactor));
            state.FailureLogged = false;
            CapturePlayer(state);
        }

        internal static void Tick(TimeControlService service)
        {
            if (service == null || !States.TryGetValue(service, out var state))
                return;

            if (!service.Active || !VoidstepSettings.Current.PreservePlayerSpeed ||
                state.Player == null || !state.Player.IsActive())
            {
                RestoreState(state);
                return;
            }

            try
            {
                var compensation = Math.Min(8f, 1f / Math.Max(0.001f, state.Factor));
                ApplyLimit(
                    state.Player,
                    compensation,
                    ref state.OriginalPlayerLimit,
                    ref state.AppliedPlayerLimit,
                    ref state.PlayerApplied);

                var currentMount = state.Player.MountAgent;
                if (currentMount != null && !currentMount.IsActive())
                    currentMount = null;
                if (!ReferenceEquals(currentMount, state.Mount))
                {
                    RestoreMount(state);
                    state.Mount = currentMount;
                    CaptureMount(state);
                }

                if (state.Mount != null)
                {
                    ApplyLimit(
                        state.Mount,
                        compensation,
                        ref state.OriginalMountLimit,
                        ref state.AppliedMountLimit,
                        ref state.MountApplied);
                }

                state.FailureLogged = false;
            }
            catch (Exception ex)
            {
                if (!state.FailureLogged)
                {
                    state.FailureLogged = true;
                    Logger.Debug("Bend Time maximum-speed ownership failed safely: " + ex.Message);
                }
            }
        }

        internal static void Restore(TimeControlService service, bool remove)
        {
            if (service == null || !States.TryGetValue(service, out var state))
                return;

            RestoreState(state);
            if (remove)
                States.Remove(service);
        }

        private static void CapturePlayer(State state)
        {
            state.PlayerApplied = false;
            if (state.Player == null || !state.Player.IsActive())
                return;
            state.OriginalPlayerLimit = state.Player.GetMaximumSpeedLimit();
        }

        private static void CaptureMount(State state)
        {
            state.MountApplied = false;
            if (state.Mount == null || !state.Mount.IsActive())
                return;
            state.OriginalMountLimit = state.Mount.GetMaximumSpeedLimit();
        }

        private static void ApplyLimit(
            Agent agent,
            float compensation,
            ref float original,
            ref float applied,
            ref bool ownsApplied)
        {
            if (agent == null || !agent.IsActive())
                return;

            var current = agent.GetMaximumSpeedLimit();
            if (ownsApplied && Approximately(current, applied))
                return;

            if (ownsApplied)
                original = current;
            else if (float.IsNaN(original) || float.IsInfinity(original))
                original = current;

            agent.SetMaximumSpeedLimit(compensation, true);
            applied = agent.GetMaximumSpeedLimit();
            ownsApplied = true;
        }

        private static void RestoreState(State state)
        {
            RestorePlayer(state);
            RestoreMount(state);
            state.Player = null;
            state.Mount = null;
            state.Factor = 1f;
        }

        private static void RestorePlayer(State state)
        {
            if (!state.PlayerApplied)
                return;

            try
            {
                if (state.Player != null && state.Player.IsActive() &&
                    Approximately(state.Player.GetMaximumSpeedLimit(), state.AppliedPlayerLimit))
                {
                    state.Player.SetMaximumSpeedLimit(state.OriginalPlayerLimit, true);
                }
            }
            finally
            {
                state.PlayerApplied = false;
            }
        }

        private static void RestoreMount(State state)
        {
            if (!state.MountApplied)
                return;

            try
            {
                if (state.Mount != null && state.Mount.IsActive() &&
                    Approximately(state.Mount.GetMaximumSpeedLimit(), state.AppliedMountLimit))
                {
                    state.Mount.SetMaximumSpeedLimit(state.OriginalMountLimit, true);
                }
            }
            finally
            {
                state.MountApplied = false;
            }
        }

        private static bool Approximately(float left, float right)
        {
            return Math.Abs(left - right) <=
                   0.001f * Math.Max(1f, Math.Max(Math.Abs(left), Math.Abs(right)));
        }
    }

    [HarmonyPatch(typeof(TimeControlService), nameof(TimeControlService.Begin))]
    internal static class BendTimeMaximumSpeedBeginPatch
    {
        private static void Postfix(
            TimeControlService __instance,
            Agent __0,
            float __1,
            bool __result)
        {
            BendTimeMaximumSpeedOwnership.Begin(__instance, __0, __1, __result);
        }
    }

    [HarmonyPatch(typeof(TimeControlService), nameof(TimeControlService.Tick))]
    internal static class BendTimeMaximumSpeedTickPatch
    {
        private static void Postfix(TimeControlService __instance)
        {
            BendTimeMaximumSpeedOwnership.Tick(__instance);
        }
    }

    /// <summary>
    /// Restore the native speed multiplier only after TimeControlService has completed its
    /// owned request cleanup and returned all driven properties and action channels to normal.
    /// </summary>
    [HarmonyPatch(typeof(TimeControlService), "CompleteLocalState")]
    internal static class BendTimeMaximumSpeedCompletePatch
    {
        private static void Postfix(TimeControlService __instance)
        {
            BendTimeMaximumSpeedOwnership.Restore(__instance, false);
        }
    }

    [HarmonyPatch(typeof(TimeControlService), nameof(TimeControlService.Cleanup))]
    internal static class BendTimeMaximumSpeedCleanupPatch
    {
        private static void Postfix(TimeControlService __instance)
        {
            BendTimeMaximumSpeedOwnership.Restore(__instance, true);
        }
    }
}
