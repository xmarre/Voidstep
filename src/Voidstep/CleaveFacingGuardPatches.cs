using System;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    // Retained only to make the failed restoration approach explicit in the source
    // history. These two legacy patches are disabled: writing LookDirection again
    // after a turn command has already reached the native agent is too late.
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

    [HarmonyPatch(typeof(AbilityManager), "TickVoidstep")]
    internal static class CleaveTickFacingGuardPatch
    {
        private static bool Prepare() => false;

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

    [HarmonyPatch(typeof(AbilityManager), "CancelCurrent")]
    internal static class CleaveCancellationFacingGuardPatch
    {
        private static bool Prepare() => false;

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

    // A native turn command cannot be safely undone after the fact. The scope
    // below blocks Voidstep's own facing writers before they call Agent.LookDirection.
    internal static class CleaveTurnMutationScope
    {
        [ThreadStatic]
        private static int _depth;

        internal static bool Active => _depth > 0;

        internal static void Enter() => _depth++;

        internal static void Exit()
        {
            if (_depth > 0)
                _depth--;
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "TickVoidstep")]
    internal static class CleaveTickTurnMutationScopePatch
    {
        private static void Prefix() => CleaveTurnMutationScope.Enter();

        private static Exception Finalizer(Exception __exception)
        {
            CleaveTurnMutationScope.Exit();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "CancelCurrent")]
    internal static class CleaveCancellationTurnMutationScopePatch
    {
        private static void Prefix(AbilityManager __instance, out bool __state)
        {
            __state = __instance != null && __instance.IsBusy &&
                      __instance.ActiveAbility == AbilityId.VoidstepCleave;
            if (__state)
                CleaveTurnMutationScope.Enter();
        }

        private static Exception Finalizer(bool __state, Exception __exception)
        {
            if (__state)
                CleaveTurnMutationScope.Exit();
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(AnimationController),
        nameof(AnimationController.SetActorFacing),
        new Type[] { typeof(Agent), typeof(Vec3) })]
    internal static class CleaveVectorFacingWriteSuppressionPatch
    {
        private static bool Prefix() => !CleaveTurnMutationScope.Active;
    }

    [HarmonyPatch(
        typeof(AnimationController),
        nameof(AnimationController.SetActorFacing),
        new Type[] { typeof(Agent), typeof(double) })]
    internal static class CleaveAngleFacingWriteSuppressionPatch
    {
        private static bool Prefix() => !CleaveTurnMutationScope.Active;
    }

    // Agent.TeleportToPosition in Bannerlord 1.3.15 only changes native position.
    // Voidstep itself introduced the body turn by also submitting a zero movement
    // direction and then repeatedly rewriting LookDirection. Keep position changes
    // and optional input cancellation, but never submit a movement/facing direction.
    [HarmonyPatch(
        typeof(AbilityManager),
        "TeleportActor",
        new Type[] { typeof(Agent), typeof(Vec3), typeof(bool) })]
    internal static class OrientationNeutralTeleportPatch
    {
        private static bool Prefix(
            AbilityManager __instance,
            Agent actor,
            Vec3 position,
            bool preserveMomentum)
        {
            if (actor == null || !actor.IsActive())
                return false;

            var before = actor.LookDirection;
            var mount = actor.MountAgent;
            if (mount != null && mount.IsActive())
            {
                mount.TeleportToPosition(position);
                actor.TeleportToPosition(position + Vec3.Up * 0.4f);
            }
            else
            {
                actor.TeleportToPosition(position);
            }

            if (!preserveMomentum)
            {
                actor.MovementInputVector = Vec2.Zero;
                if (mount != null && mount.IsActive())
                    mount.MovementInputVector = Vec2.Zero;
            }

            __instance?.Logger.Debug(
                $"Orientation-neutral teleport position={Format(position)}, " +
                $"lookBefore={Format(before)}, lookAfter={Format(actor.LookDirection)}, " +
                $"movementDirectionWasNotOverwritten=true.");
            return false;
        }

        private static string Format(Vec3 value) =>
            $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";
    }

    // The original locked-target path used target + travel * 1.5, placing the
    // player beyond the target. With facing preserved, that necessarily puts the
    // enemy behind the player and looks like a reversal. Land on the near side.
    [HarmonyPatch(
        typeof(AbilityManager),
        "ResolveVoidstepDestination",
        new Type[] { typeof(Agent), typeof(float) })]
    internal static class CleaveNearSideDestinationPatch
    {
        private static bool Prefix(
            AbilityManager __instance,
            TargetingService ____targeting,
            Agent player,
            float range,
            ref Vec3 __result)
        {
            if (player == null || !player.IsActive() || ____targeting == null)
                return true;

            var locked = ____targeting.FindLockedEnemy(player, range, 30f);
            if (locked == null)
                return true;

            var travel = locked.Position - player.Position;
            travel.z = 0f;
            var distance = travel.Normalize();
            if (distance < 0.001f)
                return true;

            // Maintain up to 1.5 m stand-off while guaranteeing that the landing
            // point never crosses through the target. At very short range, remain
            // in place and perform the radial sweep rather than teleport backwards.
            var standOff = Math.Min(1.5f, Math.Max(0f, distance - 0.5f));
            __result = standOff > 0.001f
                ? locked.Position - travel * standOff
                : player.Position;

            __instance?.Logger.Debug(
                $"Voidstep Cleave near-side lock enemy={locked.Index}, " +
                $"distance={distance:0.00}, standOff={standOff:0.00}, destination={Format(__result)}.");
            return false;
        }

        private static string Format(Vec3 value) =>
            $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";
    }

    // Heavy-thrown and command actions are not melee sweep animations and can
    // independently twist the skeleton. Cleave retains its radial effects and
    // virtual target schedule without forcing an unrelated action on channel 1.
    [HarmonyPatch(typeof(AnimationController), nameof(AnimationController.BeginCleave))]
    internal static class CleaveUnsafeActionSuppressionPatch
    {
        private static bool Prefix(AnimationController __instance, Agent actor)
        {
            __instance?.ResetActionSpeed(actor);
            return false;
        }
    }
}
