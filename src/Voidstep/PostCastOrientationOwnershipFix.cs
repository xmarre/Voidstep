using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Compatibility boundary retained for older branch-local callers. Teleport ownership now
    /// preserves the actor's existing native body frame and never derives yaw from camera, cursor
    /// or travel direction.
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
            PreservedFrameTeleportRuntime.Teleport(
                mission,
                actor,
                position,
                true,
                source,
                logger);
        }

        internal static void AlignCurrent(
            Mission mission,
            Agent actor,
            Vec3 facing,
            string source,
            VoidstepLogger logger)
        {
            // Deliberately empty. A destination or camera vector never owns post-teleport yaw.
        }

        internal static void Tick(Mission mission)
        {
        }

        internal static void Clear(Mission mission)
        {
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
    internal static class PreservedFrameSharedTeleportPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Agent actor,
            Vec3 position,
            bool preserveMomentum,
            AbilityContext ____context)
        {
            PreservedFrameTeleportRuntime.Teleport(
                ____context?.Mission,
                actor,
                position,
                preserveMomentum,
                "Blink",
                ____context?.Logger);
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
            // Suppress every legacy post-teleport camera-derived orientation write.
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
