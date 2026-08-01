using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Safe fallback for camera modes where the native mission-screen ground projection is not
    /// available. The complete camera ray is evaluated before the result is clamped to range.
    /// </summary>
    internal static class VariableDistanceCameraAimRuntime
    {
        private const float LongCameraRayLength = 250f;
        private const int MaximumIgnoredDynamicHits = 64;
        private const float MinimumPlanarDistance = 0.20f;

        internal static bool TryResolve(
            Mission mission,
            Agent actor,
            float range,
            out Vec3 position)
        {
            position = Vec3.Invalid;
            if (mission?.Scene == null || actor == null || !actor.IsActive())
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

            var end = source + direction * LongCameraRayLength;
            var rayStart = source;
            for (var ignored = 0; ignored < MaximumIgnoredDynamicHits; ignored++)
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
                    break;

                if (!IsDynamicTeleportTransparent(entity))
                {
                    position = ClampToCircleAndGround(mission, actor.Position, point, range);
                    return position.IsValid;
                }

                var remaining = end - point;
                if (remaining.Normalize() < 0.001f)
                    break;
                rayStart = point + remaining * 0.18f;
            }

            var planar = direction;
            planar.z = 0f;
            if (planar.Normalize() < 0.001f)
                planar = CameraAuthoritativeCastRuntime.GetCameraFacing(mission, actor);

            var downwardPitch = Math.Max(0f, Math.Min(0.95f, -direction.z));
            var radialFraction = 1f - downwardPitch / 0.95f;
            var planarDistance = MinimumPlanarDistance +
                Math.Max(0f, range - MinimumPlanarDistance) * radialFraction;
            var fallback = actor.Position + planar * planarDistance;
            var ground = mission.Scene.GetGroundHeightAtPosition(
                fallback,
                BodyFlags.CommonCollisionExcludeFlagsForAgent);
            if (float.IsNaN(ground) || float.IsInfinity(ground))
                return false;
            fallback.z = ground;
            position = fallback;
            return true;
        }

        private static Vec3 ClampToCircleAndGround(
            Mission mission,
            Vec3 origin,
            Vec3 requested,
            float range)
        {
            var delta = requested - origin;
            delta.z = 0f;
            var planarDistance = delta.Length;
            var allowed = Math.Max(0f, range);
            if (planarDistance > allowed && planarDistance > 0.001f)
                requested = origin + delta * (allowed / planarDistance);

            var ground = mission.Scene.GetGroundHeightAtPosition(
                requested,
                BodyFlags.CommonCollisionExcludeFlagsForAgent);
            if (float.IsNaN(ground) || float.IsInfinity(ground))
                return Vec3.Invalid;
            requested.z = ground;
            return requested;
        }

        private static bool IsDynamicTeleportTransparent(WeakGameEntity entity)
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
    }

    [HarmonyPatch(
        typeof(CameraAuthoritativeCastRuntime),
        nameof(CameraAuthoritativeCastRuntime.TryResolveCameraGround))]
    internal static class VariableDistanceCameraAimPatch
    {
        private static bool Prefix(
            Mission mission,
            Agent actor,
            float range,
            ref Vec3 position,
            ref bool __result)
        {
            __result = VariableDistanceCameraAimRuntime.TryResolve(
                mission,
                actor,
                range,
                out position);
            return false;
        }
    }

    /// <summary>
    /// Bannerlord can expose finalized damage only through AttackCollisionData and can supply a
    /// missing/non-player affector. Retry Domino exactly once when the original callback queued
    /// nothing, using Blow.OwnerId and the finalized damage value.
    /// </summary>
    [HarmonyPatch(typeof(VoidstepMissionBehavior), nameof(VoidstepMissionBehavior.OnAgentHit))]
    internal static class DominoAuthoritativeDamageCallbackPatch
    {
        private static readonly FieldInfo DominoField =
            AccessTools.Field(typeof(AbilityManager), "_domino");
        private static readonly FieldInfo PendingField =
            AccessTools.Field(typeof(DominoLinkService), "_pending");

        private static void Prefix(AbilityManager ____manager, out int __state)
        {
            __state = GetPendingCount(____manager);
        }

        private static void Postfix(
            AbilityManager ____manager,
            Agent affectedAgent,
            Agent affectorAgent,
            in Blow blow,
            in AttackCollisionData attackCollisionData,
            int __state)
        {
            if (____manager == null || affectedAgent == null ||
                GetPendingCount(____manager) > __state)
                return;

            var damage = Math.Max(blow.InflictedDamage, attackCollisionData.InflictedDamage);
            if (damage <= 0)
                return;

            var domino = DominoField?.GetValue(____manager) as DominoLinkService;
            if (domino == null)
                return;

            var source = ResolveSource(affectorAgent, blow.OwnerId);
            var corrected = blow;
            corrected.InflictedDamage = damage;
            domino.OnAgentHit(affectedAgent, source, ref corrected);

            if (GetPendingCount(____manager) > __state)
            {
                ____manager.Logger.Debug(
                    "Domino accepted authoritative damage callback target=" + affectedAgent.Index +
                    ", ownerId=" + blow.OwnerId +
                    ", blowDamage=" + blow.InflictedDamage +
                    ", collisionDamage=" + attackCollisionData.InflictedDamage + ".");
            }
        }

        private static Agent ResolveSource(Agent supplied, int ownerId)
        {
            var mission = Mission.Current;
            var player = mission?.MainAgent;
            if (supplied != null)
            {
                if (ReferenceEquals(supplied, player))
                    return supplied;
                var playerMount = player?.MountAgent;
                if (playerMount != null && ReferenceEquals(supplied, playerMount))
                    return supplied;
            }

            if (mission != null && ownerId >= 0)
            {
                var owner = mission.FindAgentWithIndex(ownerId);
                if (owner != null)
                    return owner;
            }
            return supplied;
        }

        private static int GetPendingCount(AbilityManager manager)
        {
            try
            {
                var domino = DominoField?.GetValue(manager) as DominoLinkService;
                return (PendingField?.GetValue(domino) as IList)?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
