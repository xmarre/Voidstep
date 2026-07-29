using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    internal sealed class TargetingService
    {
        private readonly Mission _mission;
        private readonly MBList<Agent> _nearby = new MBList<Agent>();

        public TargetingService(Mission mission) => _mission = mission;

        public Vec3 GetAimDirection(Agent player)
        {
            try
            {
                var camera = _mission.GetCameraFrame();
                var direction = camera.rotation.f;
                direction.z = 0f;
                if (direction.Normalize() >= 0.001f)
                    return direction;
            }
            catch
            {
            }

            var fallback = player != null ? player.LookDirection : Vec3.Forward;
            fallback.z = 0f;
            if (fallback.Normalize() < 0.001f)
                fallback = Vec3.Forward;
            return fallback;
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

            var direction = GetAimDirection(player);
            Vec3 source;
            try { source = _mission.GetCameraFrame().origin; }
            catch { source = player.GetEyeGlobalPosition(); }

            var sourceOffset = source - player.Position;
            sourceOffset.z = 0f;
            var rayLength = range + Math.Min(8f, sourceOffset.Length) + 3f;
            var target = source + direction * rayLength;
            var distance = 1f;
            var point = target;
            WeakGameEntity entity = default(WeakGameEntity);
            if (_mission.Scene.RayCastForClosestEntityOrTerrain(
                    source,
                    target,
                    out distance,
                    out point,
                    out entity,
                    0.05f,
                    BodyFlags.CommonCollisionExcludeFlagsForAgent))
            {
                position = ClampToRange(player.Position, point, range);
                return true;
            }

            target = player.Position + direction * range;
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
