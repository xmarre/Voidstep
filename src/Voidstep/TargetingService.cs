using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    internal sealed class TargetingService
    {
        private const int MaximumIgnoredRayHits = 6;
        private readonly Mission _mission;
        private readonly MBList<Agent> _nearby = new MBList<Agent>();

        public TargetingService(Mission mission) => _mission = mission;

        public Vec3 GetAimDirection(Agent player)
        {
            var direction = GetCameraRayDirection(player);
            direction.z = 0f;
            if (direction.Normalize() < 0.001f)
                direction = Vec3.Forward;
            return direction;
        }

        public Agent FindLockedEnemy(Agent player, float range, float halfAngleDegrees = 28f)
        {
            if (player == null || player.Team == null)
                return null;

            _nearby.Clear();
            _mission.GetNearbyEnemyAgents(player.Position.AsVec2, range, player.Team, _nearby);
            var forward = GetAimDirection(player);
            var minimumDot = (float)Math.Cos(halfAngleDegrees * Math.PI / 180.0);
            Agent best = null;
            var bestScore = float.MaxValue;
            for (var i = 0; i < _nearby.Count; i++)
            {
                var candidate = _nearby[i];
                if (!IsUsableTarget(player, candidate, true))
                    continue;
                var delta = candidate.GetChestGlobalPosition() - player.GetChestGlobalPosition();
                delta.z = 0f;
                var distance = delta.Normalize();
                if (distance <= 0.001f)
                    continue;
                var dot = Vec3.DotProduct(forward, delta);
                if (dot < minimumDot)
                    continue;
                var score = distance * (2f - dot);
                if (score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        public bool TryGetAimedGroundPosition(Agent player, float range, out Vec3 position)
        {
            position = Vec3.Invalid;
            if (player == null)
                return false;

            Vec3 source;
            try { source = _mission.GetCameraFrame().origin; }
            catch { source = player.GetEyeGlobalPosition(); }

            var direction = GetCameraRayDirection(player);
            var sourceOffset = source - player.Position;
            sourceOffset.z = 0f;
            var rayLength = range + Math.Min(8f, sourceOffset.Length) + 3f;
            var target = source + direction * rayLength;
            var rayStart = source;

            for (var ignored = 0; ignored <= MaximumIgnoredRayHits; ignored++)
            {
                var distance = 1f;
                var point = target;
                WeakGameEntity entity = default(WeakGameEntity);
                if (!_mission.Scene.RayCastForClosestEntityOrTerrain(
                        rayStart,
                        target,
                        out distance,
                        out point,
                        out entity,
                        0.05f,
                        BodyFlags.CommonCollisionExcludeFlagsForAgent))
                {
                    break;
                }

                if (!IsTransientProjectileEntity(entity))
                {
                    position = ClampToRange(player.Position, point, range);
                    return true;
                }

                var remaining = target - point;
                if (remaining.Normalize() < 0.001f)
                    break;
                rayStart = point + remaining * 0.15f;
            }

            var planar = direction;
            planar.z = 0f;
            if (planar.Normalize() < 0.001f)
                planar = GetAimDirection(player);
            target = player.Position + planar * range;
            var terrain = _mission.Scene.GetGroundHeightAtPosition(target, BodyFlags.CommonCollisionExcludeFlagsForAgent);
            if (float.IsNaN(terrain) || float.IsInfinity(terrain))
                return false;
            target.z = terrain;
            position = target;
            return true;
        }

        public Vec3 GetForwardFallback(Agent player, float range) =>
            player.Position + GetAimDirection(player) * range;

        internal static bool IsUsableTarget(Agent player, Agent target, bool enemyOnly)
        {
            if (player == null || target == null || target == player || !target.IsActive() || target.Health <= 0f)
                return false;
            if (enemyOnly && (player.Team == null || target.Team == null || !player.Team.IsEnemyOf(target.Team)))
                return false;
            return target.IsHuman || target.IsMount;
        }

        private Vec3 GetCameraRayDirection(Agent player)
        {
            try
            {
                var camera = _mission.GetCameraFrame();
                var direction = camera.rotation.f;
                if (direction.Normalize() >= 0.001f)
                    return direction;
            }
            catch
            {
            }

            var fallback = player != null ? player.LookDirection : Vec3.Forward;
            if (fallback.Normalize() < 0.001f)
                fallback = Vec3.Forward;
            return fallback;
        }

        private static bool IsTransientProjectileEntity(WeakGameEntity entity)
        {
            if (!entity.IsValid)
                return false;

            try
            {
                var flags = entity.BodyFlag | entity.PhysicsDescBodyFlag;
                if ((flags & (BodyFlags.MissileOnly | BodyFlags.DroppedItem)) != 0)
                    return true;
            }
            catch
            {
            }

            try
            {
                if (LooksLikeProjectileName(entity.Name) || LooksLikeProjectileName(entity.Root.Name))
                    return true;
                var tags = entity.Tags;
                if (tags != null)
                    for (var i = 0; i < tags.Length; i++)
                        if (LooksLikeProjectileName(tags[i])) return true;
            }
            catch
            {
            }
            return false;
        }

        private static bool LooksLikeProjectileName(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            value = value.ToLowerInvariant();
            return value.Contains("arrow") || value.Contains("bolt") || value.Contains("missile") ||
                   value.Contains("javelin") || value.Contains("throwing_axe") || value.Contains("throwing_knife");
        }

        private static Vec3 ClampToRange(Vec3 origin, Vec3 requested, float range)
        {
            var delta = requested - origin;
            delta.z = 0f;
            var planar = delta.Length;
            if (planar <= range || planar <= 0.001f)
                return requested;
            var clamped = origin + delta * (range / planar);
            clamped.z = requested.z;
            return clamped;
        }
    }
}
