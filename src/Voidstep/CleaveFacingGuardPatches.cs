using System;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal readonly struct CleaveFacingState
    {
        private CleaveFacingState(Agent actor, Vec3 actorFacing, Agent mount, Vec3 mountFacing)
        {
            Actor = actor;
            ActorFacing = actorFacing;
            Mount = mount;
            MountFacing = mountFacing;
        }

        private Agent Actor { get; }
        private Vec3 ActorFacing { get; }
        private Agent Mount { get; }
        private Vec3 MountFacing { get; }

        internal static CleaveFacingState Capture(Agent actor)
        {
            if (actor == null || !actor.IsActive())
                return default(CleaveFacingState);

            var mount = actor.MountAgent;
            if (mount != null && !mount.IsActive())
                mount = null;

            return new CleaveFacingState(
                actor,
                Normalize(actor.LookDirection),
                mount,
                mount != null ? Normalize(mount.LookDirection) : Vec3.Zero);
        }

        internal void Restore(VoidstepLogger logger, string stage)
        {
            if (Actor == null || !Actor.IsActive())
                return;

            try
            {
                if (Mount != null && Mount.IsActive())
                    Mount.LookDirection = MountFacing;
                Actor.LookDirection = ActorFacing;
            }
            catch (Exception ex)
            {
                logger?.Debug($"Cleave facing restoration failed during {stage}: {ex.Message}");
            }
        }

        private static Vec3 Normalize(Vec3 facing)
        {
            facing.z = 0f;
            if (facing.Normalize() < 0.001f)
                facing = Vec3.Forward;
            return facing;
        }
    }

    // Preserve the direction that exists at the start of each Cleave mission tick.
    // This intentionally neutralizes every internal yaw write made by teleport,
    // sweep presentation and recovery while leaving virtual sweep scheduling intact.
    [HarmonyPatch(typeof(AbilityManager), "TickVoidstep")]
    internal static class CleaveTickFacingGuardPatch
    {
        private static void Prefix(Agent player, out CleaveFacingState __state)
        {
            __state = CleaveFacingState.Capture(player);
        }

        private static Exception Finalizer(
            AbilityManager __instance,
            CleaveFacingState __state,
            Exception __exception)
        {
            __state.Restore(__instance?.Logger, "Cleave tick");
            return __exception;
        }
    }

    // Escape, actor replacement and other external cancellation paths can invoke
    // CancelCurrent outside TickVoidstep. Preserve the current live direction there
    // as well so the pre-wind-up snapshot cannot turn the player back.
    [HarmonyPatch(typeof(AbilityManager), "CancelCurrent")]
    internal static class CleaveCancellationFacingGuardPatch
    {
        private static void Prefix(
            AbilityManager __instance,
            AbilityContext ____context,
            int ____castActorIndex,
            out CleaveFacingState __state)
        {
            __state = default(CleaveFacingState);
            if (__instance == null || !__instance.IsBusy ||
                __instance.ActiveAbility != AbilityId.VoidstepCleave)
                return;

            var actor = ____context?.Player;
            if (actor == null || actor.Index != ____castActorIndex)
                return;

            __state = CleaveFacingState.Capture(actor);
        }

        private static Exception Finalizer(
            AbilityManager __instance,
            CleaveFacingState __state,
            Exception __exception)
        {
            __state.Restore(__instance?.Logger, "Cleave cancellation");
            return __exception;
        }
    }
}
