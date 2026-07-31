using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Applies one exact native position-and-facing frame per teleport. The previous 0.55-second
    /// reconciliation loop repeatedly resubmitted SetInitialFrame whenever normal animation changed
    /// body yaw, producing visible full rotations. This implementation never reapplies a teleport
    /// frame from Tick and deduplicates the immediate postfix alignment for Blink.
    /// </summary>
    internal static class CameraFacingTeleportOwnership
    {
        private const float DuplicateWindowSeconds = 0.08f;
        private const float DuplicatePositionToleranceSquared = 0.04f;
        private const float DuplicateFacingDot = 0.995f;

        private static readonly ConditionalWeakTable<Mission, State> States =
            new ConditionalWeakTable<Mission, State>();

        private sealed class State
        {
            internal int ActorIndex = -1;
            internal Vec3 Position = Vec3.Invalid;
            internal Vec3 Facing = Vec3.Forward;
            internal float AppliedAt;
            internal string Source;
        }

        internal static void Teleport(
            Mission mission,
            Agent actor,
            Vec3 position,
            Vec3 facing,
            string source,
            VoidstepLogger logger)
        {
            if (!OwnsLiveMainAgent(mission, actor))
                return;

            facing = Normalize(facing);
            SetExactFrame(actor, position, facing);
            Remember(mission, actor, position, facing, source);
            logger?.Debug(
                source + " applied one atomic native teleport frame; actor=" + actor.Index +
                ", facing=" + Format(facing) + ".");
        }

        internal static void AlignCurrent(
            Mission mission,
            Agent actor,
            Vec3 facing,
            string source,
            VoidstepLogger logger)
        {
            if (!OwnsLiveMainAgent(mission, actor))
                return;

            facing = Normalize(facing);
            var position = GetTeleportBasePosition(actor);
            if (IsImmediateDuplicate(mission, actor, position, facing))
                return;

            SetExactFrame(actor, position, facing);
            Remember(mission, actor, position, facing, source);
            logger?.Debug(
                source + " applied one post-teleport native facing frame; actor=" + actor.Index +
                ", facing=" + Format(facing) + ".");
        }

        // Deliberately no frame reconciliation here. Normal body/action updates after teleport must
        // remain native; replaying SetInitialFrame from Tick was the source of repeated 360-degree turns.
        internal static void Tick(Mission mission)
        {
        }

        internal static void Clear(Mission mission)
        {
            if (mission != null)
                States.Remove(mission);
        }

        private static bool OwnsLiveMainAgent(Mission mission, Agent actor)
        {
            return mission != null && actor != null && actor.IsActive() &&
                   ReferenceEquals(mission.MainAgent, actor) &&
                   ReferenceEquals(Mission.Current, mission);
        }

        private static bool IsImmediateDuplicate(
            Mission mission,
            Agent actor,
            Vec3 position,
            Vec3 facing)
        {
            if (mission == null || !States.TryGetValue(mission, out var state) ||
                state.ActorIndex != actor.Index)
                return false;

            if (MBCommon.GetApplicationTime() - state.AppliedAt > DuplicateWindowSeconds)
                return false;

            var delta = position - state.Position;
            delta.z = 0f;
            return delta.LengthSquared <= DuplicatePositionToleranceSquared &&
                   Vec3.DotProduct(state.Facing, facing) >= DuplicateFacingDot;
        }

        private static void Remember(
            Mission mission,
            Agent actor,
            Vec3 position,
            Vec3 facing,
            string source)
        {
            var state = States.GetOrCreateValue(mission);
            state.ActorIndex = actor.Index;
            state.Position = position;
            state.Facing = facing;
            state.AppliedAt = MBCommon.GetApplicationTime();
            state.Source = source;
        }

        private static void SetExactFrame(Agent actor, Vec3 basePosition, Vec3 facing)
        {
            var direction = facing.AsVec2;
            var mount = actor.MountAgent;
            if (mount != null && mount.IsActive())
            {
                var mountPosition = basePosition;
                mount.SetInitialFrame(in mountPosition, in direction, true);
                mount.LookDirection = facing;

                var riderPosition = basePosition + Vec3.Up * 0.4f;
                actor.SetInitialFrame(in riderPosition, in direction, true);
                actor.LookDirection = facing;
                return;
            }

            var actorPosition = basePosition;
            actor.SetInitialFrame(in actorPosition, in direction, true);
            actor.LookDirection = facing;
        }

        private static Vec3 GetTeleportBasePosition(Agent actor)
        {
            var mount = actor?.MountAgent;
            return mount != null && mount.IsActive() ? mount.Position : actor.Position;
        }

        private static Vec3 Normalize(Vec3 value)
        {
            value.z = 0f;
            if (value.Normalize() < 0.001f)
                value = Vec3.Forward;
            return value;
        }

        private static string Format(Vec3 value) =>
            "(" + value.x.ToString("0.00") + ", " + value.y.ToString("0.00") +
            ", " + value.z.ToString("0.00") + ")";
    }

    /// <summary>
    /// Compatibility target retained for the branch-local patch that disables the obsolete
    /// pre-teleport guard. It intentionally owns no state and performs no direction mutation.
    /// </summary>
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
            AbilityContext ____context)
        {
            if (actor == null || !actor.IsActive())
                return false;

            var mission = ____context?.Mission;
            var facing = CameraAuthoritativeCastRuntime.GetCameraFacing(mission, actor);
            CameraFacingTeleportOwnership.Teleport(
                mission,
                actor,
                position,
                facing,
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
        private static bool Prefix(
            Agent actor,
            Vec3 facing,
            string source,
            VoidstepLogger logger)
        {
            CameraFacingTeleportOwnership.AlignCurrent(
                Mission.Current,
                actor,
                facing,
                source,
                logger);
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
