using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Owns one exact native position-and-facing frame after Blink or Voidstep. SetInitialFrame
    /// updates position and body direction atomically; a short mission-scoped reconciliation
    /// window corrects native controller rollback without globally patching Agent presentation.
    /// Ownership ends immediately when the camera is deliberately turned away.
    /// </summary>
    internal static class CameraFacingTeleportOwnership
    {
        private const float HoldSeconds = 0.55f;
        private const float ReapplyDotThreshold = 0.985f;
        private const float CameraReleaseDotThreshold = 0.82f;

        private static readonly ConditionalWeakTable<Mission, State> States =
            new ConditionalWeakTable<Mission, State>();

        private sealed class State
        {
            internal int ActorIndex = -1;
            internal int MountIndex = -1;
            internal Vec3 Facing = Vec3.Forward;
            internal float ExpiresAt;
            internal bool Armed;
            internal string Source;
            internal VoidstepLogger Logger;
            internal int Corrections;
        }

        internal static void Teleport(
            Mission mission,
            Agent actor,
            Vec3 position,
            Vec3 facing,
            string source,
            VoidstepLogger logger)
        {
            if (mission == null || actor == null || !actor.IsActive())
                return;

            facing = Normalize(facing);
            SetExactFrame(actor, position, facing);
            Arm(mission, actor, facing, source, logger);
            logger?.Debug(
                source + " applied atomic native teleport frame; actor=" + actor.Index +
                ", facing=" + Format(facing) + ".");
        }

        internal static void AlignCurrent(
            Mission mission,
            Agent actor,
            Vec3 facing,
            string source,
            VoidstepLogger logger)
        {
            if (mission == null || actor == null || !actor.IsActive())
                return;

            facing = Normalize(facing);
            SetExactFrame(actor, GetTeleportBasePosition(actor), facing);
            Arm(mission, actor, facing, source, logger);
        }

        internal static void Tick(Mission mission)
        {
            if (mission == null || !States.TryGetValue(mission, out var state) || !state.Armed)
                return;

            var actor = mission.MainAgent;
            if (actor == null || !actor.IsActive() || actor.Index != state.ActorIndex)
            {
                state.Armed = false;
                return;
            }

            if (MBCommon.GetApplicationTime() >= state.ExpiresAt)
            {
                state.Logger?.Debug(
                    state.Source + " native teleport-frame ownership released after " +
                    state.Corrections + " correction(s).");
                state.Armed = false;
                return;
            }

            var cameraFacing = CameraAuthoritativeCastRuntime.GetCameraFacing(mission, actor);
            if (Vec3.DotProduct(state.Facing, cameraFacing) < CameraReleaseDotThreshold)
            {
                state.Logger?.Debug(
                    state.Source + " native teleport-frame ownership released for deliberate camera turn.");
                state.Armed = false;
                return;
            }

            var mount = actor.MountAgent;
            if (state.MountIndex >= 0 &&
                (mount == null || !mount.IsActive() || mount.Index != state.MountIndex))
            {
                state.Armed = false;
                return;
            }

            var bodyOwner = mount != null && mount.IsActive() ? mount : actor;
            var bodyFacing = BodyAlignedCleaveRuntime.GetBodyFacing(bodyOwner);
            var lookFacing = Normalize(actor.LookDirection);
            if (Vec3.DotProduct(state.Facing, bodyFacing) >= ReapplyDotThreshold &&
                Vec3.DotProduct(state.Facing, lookFacing) >= ReapplyDotThreshold)
                return;

            SetExactFrame(actor, GetTeleportBasePosition(actor), state.Facing);
            state.Corrections++;
            if (state.Corrections == 1)
            {
                state.Logger?.Debug(
                    state.Source + " corrected native post-teleport frame rollback; facing=" +
                    Format(state.Facing) + ".");
            }
        }

        internal static void Clear(Mission mission)
        {
            if (mission == null)
                return;
            if (States.TryGetValue(mission, out var state))
                state.Armed = false;
            States.Remove(mission);
        }

        private static void Arm(
            Mission mission,
            Agent actor,
            Vec3 facing,
            string source,
            VoidstepLogger logger)
        {
            var mount = actor.MountAgent;
            var state = States.GetOrCreateValue(mission);
            state.ActorIndex = actor.Index;
            state.MountIndex = mount != null && mount.IsActive() ? mount.Index : -1;
            state.Facing = facing;
            state.ExpiresAt = MBCommon.GetApplicationTime() + HoldSeconds;
            state.Armed = true;
            state.Source = source;
            state.Logger = logger;
            state.Corrections = 0;
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

    /// <summary>
    /// Existing Blink/Cleave postfixes route through AlignToCamera. Replace that one-shot
    /// LookDirection/SetMovementDirection implementation with exact native frame ownership.
    /// </summary>
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
            var mission = Mission.Current;
            CameraFacingTeleportOwnership.AlignCurrent(
                mission,
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
