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
    /// Resolves the cast marker from the complete camera ray instead of ending the ray at the
    /// maximum ability radius. Camera yaw selects the point around the player and camera pitch
    /// selects its distance. The result is finally clamped to the circular cast radius.
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
                {
                    break;
                }

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

            // Some Bannerlord camera modes expose a ray that does not collide with terrain.
            // In that case map pitch continuously onto the radius: level aim is maximum range,
            // while looking down moves the marker towards the player.
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
                return (flags & (BodyFlags.AgentOnly | BodyFlags.MissileOnly | BodyFlags.DroppedItem)) != 0;
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
    /// The ordinary mission callback occasionally supplies a null/non-player affector and can
    /// expose the finalized damage only through AttackCollisionData. Retry Domino exactly once
    /// when the original handler queued nothing, using Blow.OwnerId and the finalized damage.
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

    /// <summary>
    /// Mission time speed also scales the main-agent controller's own pre-tick. Driven-property
    /// and action-speed multipliers cannot compensate input/controller time that was already
    /// reduced. Scale that one player-owned delta back while leaving the rest of the mission on
    /// the requested Bend Time factor.
    /// </summary>
    internal static class BendTimeMainAgentTickRuntime
    {
        private static WeakReference<TimeControlService> _owner;
        private static int _playerIndex = -1;
        private static float _compensation = 1f;
        private static bool _logged;

        internal static void Update(
            TimeControlService service,
            Agent player,
            float factor,
            VoidstepLogger logger)
        {
            if (service == null || !service.Active || player == null || !player.IsActive() ||
                factor <= 0.001f || factor >= 0.999f)
            {
                Clear(service);
                return;
            }

            _owner = new WeakReference<TimeControlService>(service);
            _playerIndex = player.Index;
            _compensation = Math.Min(8f, 1f / factor);
            if (!_logged)
            {
                _logged = true;
                logger?.Debug(
                    "Bend Time main-agent controller delta exemption armed=" +
                    _compensation.ToString("0.00") + "x.");
            }
        }

        internal static void Clear(TimeControlService service)
        {
            if (_owner != null && _owner.TryGetTarget(out var current) &&
                service != null && !ReferenceEquals(current, service))
                return;
            _owner = null;
            _playerIndex = -1;
            _compensation = 1f;
            _logged = false;
        }

        internal static void Scale(ref float dt)
        {
            if (_owner == null || !_owner.TryGetTarget(out var service) || !service.Active)
            {
                Clear(service);
                return;
            }

            var player = Mission.Current?.MainAgent;
            if (player == null || !player.IsActive() || player.Index != _playerIndex)
                return;
            dt = Math.Max(0f, dt) * _compensation;
        }
    }

    [HarmonyPatch(typeof(TimeControlService), nameof(TimeControlService.Tick))]
    internal static class BendTimeMainAgentRuntimeUpdatePatch
    {
        private static void Postfix(
            TimeControlService __instance,
            float ____factor,
            Agent ____player,
            VoidstepLogger ____logger)
        {
            BendTimeMainAgentTickRuntime.Update(
                __instance,
                ____player,
                ____factor,
                ____logger);
        }
    }

    [HarmonyPatch(typeof(TimeControlService), "RestoreCompensation")]
    internal static class BendTimeMainAgentRuntimeCleanupPatch
    {
        private static void Postfix(TimeControlService __instance) =>
            BendTimeMainAgentTickRuntime.Clear(__instance);
    }

    [HarmonyPatch]
    internal static class MissionMainAgentControllerDeltaPatch
    {
        private const string ControllerTypeName =
            "TaleWorlds.MountAndBlade.View.MissionViews.MissionMainAgentController";

        private static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName(ControllerTypeName);
            return type == null
                ? null
                : AccessTools.Method(type, "OnPreMissionTick", new[] { typeof(float) });
        }

        private static bool Prepare() => TargetMethod() != null;

        private static void Prefix(ref float dt) =>
            BendTimeMainAgentTickRuntime.Scale(ref dt);
    }
}
