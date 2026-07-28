using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal readonly struct TeleportValidationResult
    {
        public TeleportValidationResult(bool success, Vec3 position, string reason, bool usedFallback)
        {
            Success = success;
            Position = position;
            Reason = reason;
            UsedFallback = usedFallback;
        }

        public bool Success { get; }
        public Vec3 Position { get; }
        public string Reason { get; }
        public bool UsedFallback { get; }
    }

    internal sealed class TeleportValidator
    {
        private readonly Mission _mission;
        private readonly MBList<Agent> _nearby = new MBList<Agent>();
        private readonly List<CandidateScore<Vec3>> _fallback = new List<CandidateScore<Vec3>>(48);
        private readonly float[] _ringRadii = { 0.45f, 0.9f, 1.4f, 2.0f };

        public TeleportValidator(Mission mission) => _mission = mission;

        public TeleportValidationResult Validate(Agent actor, Vec3 requested, float maximumRange, bool allowThroughWalls)
        {
            if (actor == null || !actor.IsActive())
                return Fail("No active controlled agent.");

            var delta = requested - actor.Position;
            delta.z = 0f;
            var planarDistance = delta.Length;
            if (planarDistance > maximumRange && planarDistance > 0.001f)
                requested = actor.Position + delta * (maximumRange / planarDistance);

            if (TryValidateExact(actor, requested, allowThroughWalls, out var exact, out var reason))
                return new TeleportValidationResult(true, exact, null, false);

            BuildFallbackCandidates(requested, actor.Position.z);
            _fallback.Sort(CompareCandidate);
            var fallbackChecks = Math.Min(16, _fallback.Count);
            for (var i = 0; i < fallbackChecks; i++)
            {
                if (TryValidateExact(actor, _fallback[i].Value, allowThroughWalls, out var validated, out _))
                    return new TeleportValidationResult(true, validated, null, true);
            }

            return Fail(reason ?? "No safe destination was found.");
        }

        private bool TryValidateExact(Agent actor, Vec3 candidate, bool allowThroughWalls, out Vec3 validated, out string reason)
        {
            validated = candidate;
            reason = null;

            if (!_mission.IsPositionInsideBoundaries(candidate.AsVec2) || !_mission.IsPositionInsideHardBoundaries(candidate.AsVec2))
            {
                reason = "Destination is outside the mission boundary.";
                return false;
            }

            var normal = Vec3.Up;
            var ground = _mission.Scene.GetGroundHeightAtPosition(candidate, out normal, BodyFlags.CommonCollisionExcludeFlagsForAgent);
            if (float.IsNaN(ground) || float.IsInfinity(ground))
            {
                reason = "No valid ground surface was found.";
                return false;
            }
            candidate.z = ground + 0.05f;

            var mounted = actor.MountAgent != null && actor.MountAgent.IsActive();
            if (normal.z < (mounted ? 0.80f : 0.72f))
            {
                reason = "The slope is too steep.";
                return false;
            }

            var water = _mission.Scene.GetWaterLevelAtPosition(candidate.AsVec2, true, true);
            if (!float.IsNaN(water) && water > candidate.z + 0.15f)
            {
                reason = "The destination is under water.";
                return false;
            }

            if (_mission.IsPositionOnAnyBlockerNavMeshFace(candidate) || _mission.IsPositionInsideAnyBlockerNavMeshFace2D(candidate.AsVec2))
            {
                reason = "The destination is inside a navmesh blocker.";
                return false;
            }

            var navigationProbe = candidate;
            if (_mission.Scene.GetNearestNavigationMeshForPosition(in navigationProbe, 1.5f, false) == UIntPtr.Zero)
            {
                reason = "The destination is not on accessible navigation geometry.";
                return false;
            }

            if (!allowThroughWalls && IsWallBetween(actor, candidate))
            {
                reason = "A sealed obstacle blocks the teleport path.";
                return false;
            }

            if (IsNearCliff(candidate, ground))
            {
                reason = "The destination is too close to a cliff or sharp height discontinuity.";
                return false;
            }

            if (IsOccupied(actor, candidate))
            {
                reason = "The destination is occupied.";
                return false;
            }

            var head = candidate + Vec3.Up * (mounted ? 3.1f : 1.8f);
            var verticalDistance = 1f;
            var closest = head;
            WeakGameEntity entity = default(WeakGameEntity);
            if (_mission.Scene.RayCastForClosestEntityOrTerrain(
                    candidate + Vec3.Up * 0.15f,
                    head,
                    out verticalDistance,
                    out closest,
                    out entity,
                    0.25f,
                    BodyFlags.CommonCollisionExcludeFlagsForAgent))
            {
                reason = "There is insufficient standing clearance.";
                return false;
            }

            validated = candidate;
            return true;
        }

        private bool IsWallBetween(Agent actor, Vec3 candidate)
        {
            var source = actor.GetChestGlobalPosition();
            var target = candidate + Vec3.Up * 0.9f;
            var distance = 1f;
            var closest = target;
            WeakGameEntity entity = default(WeakGameEntity);
            return _mission.Scene.RayCastForClosestEntityOrTerrain(
                source,
                target,
                out distance,
                out closest,
                out entity,
                0.22f,
                BodyFlags.CommonCollisionExcludeFlagsForAgent);
        }

        private bool IsOccupied(Agent actor, Vec3 candidate)
        {
            _nearby.Clear();
            _mission.GetNearbyAgents(candidate.AsVec2, 0.75f, _nearby);
            for (var i = 0; i < _nearby.Count; i++)
            {
                var agent = _nearby[i];
                if (agent == null || agent == actor || agent == actor.MountAgent || agent == actor.RiderAgent || !agent.IsActive())
                    continue;
                if (Math.Abs(agent.Position.z - candidate.z) <= 1.5f)
                    return true;
            }
            return false;
        }


        private bool IsNearCliff(Vec3 candidate, float centerGround)
        {
            const float probeRadius = 0.55f;
            const float maximumStep = 0.85f;
            for (var i = 0; i < 8; i++)
            {
                var angle = i * (Math.PI * 2.0 / 8.0);
                var probe = new Vec3(
                    candidate.x + (float)Math.Cos(angle) * probeRadius,
                    candidate.y + (float)Math.Sin(angle) * probeRadius,
                    candidate.z + 0.5f,
                    1f);
                var height = _mission.Scene.GetGroundHeightAtPosition(probe, BodyFlags.CommonCollisionExcludeFlagsForAgent);
                if (float.IsNaN(height) || float.IsInfinity(height) || Math.Abs(height - centerGround) > maximumStep)
                    return true;
            }
            return false;
        }

        private void BuildFallbackCandidates(Vec3 center, float actorZ)
        {
            _fallback.Clear();
            var ordinal = 0;
            _fallback.Add(new CandidateScore<Vec3>(center, 0d, center.z - actorZ, ordinal++));
            for (var ring = 0; ring < _ringRadii.Length; ring++)
            {
                var radius = _ringRadii[ring];
                for (var i = 0; i < 12; i++)
                {
                    var angle = i * (Math.PI * 2.0 / 12.0);
                    var candidate = new Vec3(
                        center.x + (float)Math.Cos(angle) * radius,
                        center.y + (float)Math.Sin(angle) * radius,
                        center.z,
                        1f);
                    _fallback.Add(new CandidateScore<Vec3>(candidate, radius * radius, candidate.z - actorZ, ordinal++));
                }
            }
        }


        private static int CompareCandidate(CandidateScore<Vec3> left, CandidateScore<Vec3> right)
        {
            var horizontal = left.DistanceSquared.CompareTo(right.DistanceSquared);
            if (horizontal != 0) return horizontal;
            var vertical = Math.Abs(left.VerticalDelta).CompareTo(Math.Abs(right.VerticalDelta));
            return vertical != 0 ? vertical : left.Ordinal.CompareTo(right.Ordinal);
        }

        private static TeleportValidationResult Fail(string reason) =>
            new TeleportValidationResult(false, Vec3.Invalid, reason, false);
    }
}
