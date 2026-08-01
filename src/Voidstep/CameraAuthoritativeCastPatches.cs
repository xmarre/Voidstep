using System;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal static class CameraAuthoritativeCastRuntime
    {
        private const int MaximumIgnoredRayHits = 64;

        internal static Vec3 GetCameraFacing(Mission mission, Agent actor)
        {
            try
            {
                var facing = mission.GetCameraFrame().rotation.f;
                facing.z = 0f;
                if (facing.Normalize() >= 0.001f)
                    return facing;
            }
            catch
            {
            }

            var fallback = actor != null ? actor.LookDirection : Vec3.Forward;
            fallback.z = 0f;
            if (fallback.Normalize() < 0.001f)
                fallback = Vec3.Forward;
            return fallback;
        }

        internal static bool TryResolveCameraGround(
            Mission mission,
            Agent actor,
            float range,
            out Vec3 position)
        {
            position = Vec3.Invalid;
            if (mission?.Scene == null || actor == null)
                return false;

            Vec3 source;
            Vec3 direction;
            try
            {
                var camera = mission.GetCameraFrame();
                source = camera.origin;
                direction = camera.rotation.f;
            }
            catch
            {
                source = actor.GetEyeGlobalPosition();
                direction = actor.LookDirection;
            }

            if (direction.Normalize() < 0.001f)
                direction = Vec3.Forward;

            var cameraOffset = source - actor.Position;
            cameraOffset.z = 0f;
            var rayLength = Math.Max(1f, range) + Math.Min(10f, cameraOffset.Length) + 4f;
            var end = source + direction * rayLength;
            var rayStart = source;

            for (var ignored = 0; ignored < MaximumIgnoredRayHits; ignored++)
            {
                var distance = 1f;
                var point = end;
                WeakGameEntity entity = default(WeakGameEntity);
                if (!mission.Scene.RayCastForClosestEntityOrTerrain(
                        rayStart,
                        end,
                        out distance,
                        out point,
                        out entity,
                        0.05f,
                        BodyFlags.CommonCollisionExcludeFlagsForAgent))
                {
                    break;
                }

                if (!ShouldIgnoreTeleportRayEntity(entity))
                {
                    position = ClampToRange(actor.Position, point, range);
                    return true;
                }

                var remaining = end - point;
                if (remaining.Normalize() < 0.001f)
                    break;
                rayStart = point + remaining * 0.18f;
            }

            var planar = GetCameraFacing(mission, actor);
            var fallback = actor.Position + planar * Math.Max(0f, range);
            var ground = mission.Scene.GetGroundHeightAtPosition(
                fallback,
                BodyFlags.CommonCollisionExcludeFlagsForAgent);
            if (float.IsNaN(ground) || float.IsInfinity(ground))
                return false;
            fallback.z = ground;
            position = fallback;
            return true;
        }

        internal static bool IsStaticWorldBlocked(
            Mission mission,
            Vec3 source,
            Vec3 target,
            float radius)
        {
            if (mission?.Scene == null)
                return true;

            var rayStart = source;
            for (var ignored = 0; ignored < MaximumIgnoredRayHits; ignored++)
            {
                var distance = 1f;
                var point = target;
                WeakGameEntity entity = default(WeakGameEntity);
                if (!mission.Scene.RayCastForClosestEntityOrTerrain(
                        rayStart,
                        target,
                        out distance,
                        out point,
                        out entity,
                        radius,
                        BodyFlags.CommonCollisionExcludeFlagsForAgent))
                {
                    return false;
                }

                if (!ShouldIgnoreTeleportRayEntity(entity))
                    return true;

                var remaining = target - point;
                if (remaining.Normalize() < 0.001f)
                    return false;
                rayStart = point + remaining * 0.18f;
            }

            return false;
        }

        internal static void AlignToCamera(
            Agent actor,
            Vec3 facing,
            string source,
            VoidstepLogger logger)
        {
            // Compatibility target only. Camera state never mutates an Agent.
        }

        private static bool ShouldIgnoreTeleportRayEntity(WeakGameEntity entity)
        {
            if (!entity.IsValid)
                return false;

            try
            {
                var flags = entity.BodyFlag | entity.PhysicsDescBodyFlag;
                return (flags &
                        (BodyFlags.AgentOnly | BodyFlags.MissileOnly | BodyFlags.DroppedItem)) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static Vec3 ClampToRange(Vec3 origin, Vec3 requested, float range)
        {
            var delta = requested - origin;
            delta.z = 0f;
            var planarDistance = delta.Length;
            if (planarDistance <= Math.Max(0f, range) || planarDistance <= 0.001f)
                return requested;
            var clamped = origin + delta * (range / planarDistance);
            clamped.z = requested.z;
            return clamped;
        }
    }

    [HarmonyPatch(
        typeof(TargetingService),
        nameof(TargetingService.TryGetAimedGroundPosition))]
    internal static class CameraGroundIgnoresAgentsPatch
    {
        private static bool Prefix(
            Mission ____mission,
            Agent player,
            float range,
            ref Vec3 position,
            ref bool __result)
        {
            __result = CameraAuthoritativeCastRuntime.TryResolveCameraGround(
                ____mission,
                player,
                range,
                out position);
            return false;
        }
    }

    [HarmonyPatch(typeof(BlinkController), "ResolveRequestedPosition")]
    internal static class BlinkCameraDestinationPatch
    {
        private static bool Prefix(
            Mission ____mission,
            Agent actor,
            float range,
            ref Vec3 __result)
        {
            if (!CameraAuthoritativeCastRuntime.TryResolveCameraGround(
                    ____mission,
                    actor,
                    range,
                    out __result))
            {
                __result = actor.Position +
                    CameraAuthoritativeCastRuntime.GetCameraFacing(
                        ____mission,
                        actor) * range;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(AbilitySelectionController), "ResolveCleaveDestination")]
    internal static class CleavePreviewCameraDestinationPatch
    {
        private static bool Prefix(
            Mission ____mission,
            Agent player,
            float range,
            ref Vec3 __result)
        {
            if (!CameraAuthoritativeCastRuntime.TryResolveCameraGround(
                    ____mission,
                    player,
                    range,
                    out __result))
            {
                __result = player.Position +
                    CameraAuthoritativeCastRuntime.GetCameraFacing(
                        ____mission,
                        player) * range;
            }
            return false;
        }
    }

    [HarmonyPatch(
        typeof(BodyAlignedCleaveRuntime),
        nameof(BodyAlignedCleaveRuntime.ResolveRequested))]
    internal static class CleaveExecutionCameraDestinationPatch
    {
        private static bool Prefix(
            AbilityManager manager,
            AbilityContext context,
            Agent player,
            float range,
            ref Vec3 __result)
        {
            var state = BodyAlignedCleaveRuntime.Get(manager);
            state.Logger = context?.Logger;
            state.Facing = CameraAuthoritativeCastRuntime.GetCameraFacing(
                context?.Mission,
                player);
            state.TargetIndex = -1;
            state.TargetPosition = Vec3.Invalid;

            if (!CameraAuthoritativeCastRuntime.TryResolveCameraGround(
                    context?.Mission,
                    player,
                    range,
                    out __result))
            {
                __result = player.Position + state.Facing * range;
            }
            return false;
        }
    }

    [HarmonyPatch(
        typeof(BodyAlignedCleaveRuntime),
        nameof(BodyAlignedCleaveRuntime.ValidateOnFacingAxis))]
    internal static class CleaveUsesMarkerValidationPatch
    {
        private static bool Prefix(
            TeleportValidator validator,
            Agent actor,
            Vec3 requested,
            float maximumRange,
            bool allowThroughWalls,
            ref TeleportValidationResult __result)
        {
            try
            {
                BodyAlignedCleaveRuntime.EnterValidationBypass();
                __result = validator.Validate(
                    actor,
                    requested,
                    maximumRange,
                    allowThroughWalls);
            }
            finally
            {
                BodyAlignedCleaveRuntime.ExitValidationBypass();
            }
            return false;
        }
    }

    [HarmonyPatch(
        typeof(BodyAlignedCleaveRuntime),
        nameof(BodyAlignedCleaveRuntime.PrepareSweep))]
    internal static class CleaveSweepKeepsCameraFacingPatch
    {
        private static bool Prefix(
            BodyAlignedCleaveState state,
            CleaveExecutionSnapshot snapshot,
            Agent actor)
        {
            if (state == null)
                return false;

            state.Facing = BodyAlignedCleaveRuntime.NormalizeFacing(state.Facing);
            var facingAngle = AngleMath.NormalizeRadians(
                Math.Atan2(state.Facing.y, state.Facing.x));
            state.StartAngle = AngleMath.NormalizeRadians(
                facingAngle -
                (int)snapshot.Direction * snapshot.SweepRadians * 0.5);
            state.VisualBurstIndex = 0;
            state.ForwardBurstPlayed = false;
            BodyAlignedCleaveRuntime.BindActor(state, actor);
            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportValidator), "IsWallBetween")]
    internal static class TeleportPathIgnoresAgentsPatch
    {
        private static bool Prefix(
            Mission ____mission,
            Agent actor,
            Vec3 candidate,
            ref bool __result)
        {
            __result = CameraAuthoritativeCastRuntime.IsStaticWorldBlocked(
                ____mission,
                actor.GetChestGlobalPosition(),
                candidate + Vec3.Up * 0.9f,
                0.22f);
            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportValidator), "IsOccupied")]
    internal static class TeleportDestinationIgnoresAgentsPatch
    {
        private static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(PostTeleportOrientationGuard),
        nameof(PostTeleportOrientationGuard.Arm))]
    internal static class DisableLegacyBodyRestorationPatch
    {
        private static bool Prefix() => false;
    }
}
