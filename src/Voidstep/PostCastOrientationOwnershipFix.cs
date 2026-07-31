using System;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Teleporting owns position only. TOR's projected cursor can select a destination that is
    /// unrelated or even opposite to the camera/body facing vector, so writing any orientation at
    /// the teleport boundary can produce a 180/360-degree correction. Native body, look, action and
    /// mount orientation are therefore left completely untouched.
    /// </summary>
    internal static class CameraFacingTeleportOwnership
    {
        internal static void Teleport(
            Mission mission,
            Agent actor,
            Vec3 position,
            Vec3 ignoredFacing,
            string source,
            VoidstepLogger logger)
        {
            if (!OwnsLiveMainAgent(mission, actor))
                return;

            var beforeBody = BodyAlignedCleaveRuntime.GetBodyFacing(actor);
            var beforeLook = Normalize(actor.LookDirection);
            var mount = actor.MountAgent;
            var mounted = mount != null && mount.IsActive();

            if (mounted)
            {
                mount.TeleportToPosition(position);
                actor.TeleportToPosition(position + Vec3.Up * 0.4f);
            }
            else
            {
                actor.TeleportToPosition(position);
            }

            var afterBody = BodyAlignedCleaveRuntime.GetBodyFacing(actor);
            var afterLook = Normalize(actor.LookDirection);
            logger?.Debug(
                source + " applied position-only teleport; actor=" + actor.Index +
                ", bodyDelta=" + AngleDegrees(beforeBody, afterBody).ToString("0.0") +
                "deg, lookDelta=" + AngleDegrees(beforeLook, afterLook).ToString("0.0") +
                "deg, mounted=" + mounted + ".");
        }

        internal static void AlignCurrent(
            Mission mission,
            Agent actor,
            Vec3 facing,
            string source,
            VoidstepLogger logger)
        {
            // Deliberately empty. Post-teleport camera/body alignment was the remaining source of
            // intermittent turns because cursor-projected destinations do not own camera facing.
        }

        internal static void Tick(Mission mission)
        {
        }

        internal static void Clear(Mission mission)
        {
        }

        private static bool OwnsLiveMainAgent(Mission mission, Agent actor)
        {
            return mission != null && actor != null && actor.IsActive() &&
                   ReferenceEquals(mission.MainAgent, actor) &&
                   ReferenceEquals(Mission.Current, mission);
        }

        private static Vec3 Normalize(Vec3 value)
        {
            value.z = 0f;
            if (value.Normalize() < 0.001f)
                value = Vec3.Forward;
            return value;
        }

        private static double AngleDegrees(Vec3 left, Vec3 right)
        {
            left = Normalize(left);
            right = Normalize(right);
            var dot = Math.Max(-1f, Math.Min(1f, Vec3.DotProduct(left, right)));
            return Math.Acos(dot) * 180.0 / Math.PI;
        }
    }

    internal static class PostTeleportOrientationGuard
    {
        internal readonly struct Snapshot
        {
        }

        internal static void Arm(
            AbilityManager manager,
            Agent actor,
            Snapshot snapshot,
            string source,
            VoidstepLogger logger)
        {
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "TeleportActor")]
    internal static class PositionOnlySharedTeleportPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Agent actor,
            Vec3 position,
            bool preserveMomentum,
            AbilityContext ____context)
        {
            if (actor == null || !actor.IsActive())
                return false;

            CameraFacingTeleportOwnership.Teleport(
                ____context?.Mission,
                actor,
                position,
                Vec3.Zero,
                "Blink",
                ____context?.Logger);

            if (!preserveMomentum)
            {
                actor.MovementInputVector = Vec2.Zero;
                var mount = actor.MountAgent;
                if (mount != null && mount.IsActive())
                    mount.MovementInputVector = Vec2.Zero;
            }
            return false;
        }
    }

    [HarmonyPatch(
        typeof(CameraAuthoritativeCastRuntime),
        nameof(CameraAuthoritativeCastRuntime.AlignToCamera))]
    internal static class CameraAlignmentUsesExactNativeFramePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            // Suppress every legacy post-teleport orientation write.
            return false;
        }
    }

    [HarmonyPatch(typeof(AbilityManager), nameof(AbilityManager.Tick))]
    internal static class PostTeleportOrientationGuardTickPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(AbilityContext ____context)
        {
            CameraFacingTeleportOwnership.Tick(____context?.Mission);
        }
    }

    [HarmonyPatch(typeof(AbilityManager), nameof(AbilityManager.Cleanup))]
    internal static class CameraFacingTeleportCleanupPatch
    {
        private static void Postfix(AbilityContext ____context)
        {
            CameraFacingTeleportOwnership.Clear(____context?.Mission);
        }
    }
}
