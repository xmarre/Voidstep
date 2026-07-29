using System;
using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class CleaveSweepController
    {
        private readonly Mission _mission;
        private readonly BlowFactory _blows;
        private readonly EffectController _effects;
        private readonly AnimationController _animation;
        private readonly VoidstepLogger _logger;
        private readonly MBList<Agent> _nearby = new MBList<Agent>();
        private readonly List<SweepTarget<Agent>> _candidates = new List<SweepTarget<Agent>>(128);
        private readonly List<ScheduledSweepTarget<Agent>> _schedule = new List<ScheduledSweepTarget<Agent>>(128);
        private readonly HitRegistry<int> _hits = new HitRegistry<int>();
        private readonly List<ScheduledSweepTarget<int>> _snapshotSchedule = new List<ScheduledSweepTarget<int>>(128);

        private Agent _actor;
        private MissionWeapon _weapon;
        private float _elapsed;
        private float _duration;
        private double _startAngle;
        private double _sweepRadians;
        private SweepDirection _direction;
        private bool _active;
        private float _lastProgress;
        private float _radius;
        private float _damageMultiplier;
        private float _knockback;
        private float _knockdownThreshold;
        private int _maximumTargets;
        private bool _friendlyFire;
        private bool _targetMounts;
        private bool _snapshotTargets;
        private float _trailAccumulator;
        private int _trailBursts;
        private int _successfulHits;
        private int _largestCandidateSet;

        public CleaveSweepController(Mission mission, BlowFactory blows, EffectController effects, AnimationController animation, VoidstepLogger logger)
        {
            _mission = mission;
            _blows = blows;
            _effects = effects;
            _animation = animation;
            _logger = logger;
        }

        public bool Active => _active;
        public float Progress => _duration <= 0f ? 1f : Math.Min(1f, _elapsed / _duration);
        public int SuccessfulHits => _successfulHits;

        public bool Begin(Agent actor, MissionWeapon weapon, out string failure)
        {
            Cleanup();
            failure = null;
            if (actor == null || !actor.IsActive())
            {
                failure = "No active player is available for the cleave.";
                return false;
            }
            if (!WeaponValidation.IsUsableMeleeWeapon(weapon))
            {
                failure = "Voidstep Cleave requires a currently wielded melee weapon.";
                return false;
            }

            _actor = actor;
            _weapon = weapon;
            var settings = VoidstepSettings.Current;
            _duration = 0.72f;
            _sweepRadians = settings.CleaveSweepDegrees * Math.PI / 180.0;
            _direction = settings.CleaveClockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise;
            _radius = settings.CleaveRadius;
            _damageMultiplier = settings.CleaveDamageMultiplier;
            _knockback = settings.CleaveKnockback;
            _knockdownThreshold = settings.CleaveKnockdownThreshold;
            _maximumTargets = settings.MaximumCleaveTargets;
            _friendlyFire = settings.CleaveFriendlyFire;
            _targetMounts = settings.CleaveMounts;
            _snapshotTargets = settings.CleaveSnapshotTargets;
            _trailAccumulator = 0f;
            _trailBursts = 0;
            _successfulHits = 0;
            _largestCandidateSet = 0;
            var look = actor.LookDirection;
            look.z = 0f;
            if (look.Normalize() < 0.001f) look = Vec3.Forward;
            var facing = Math.Atan2(look.y, look.x);
            _startAngle = AngleMath.NormalizeRadians(facing);
            _active = true;
            _elapsed = 0f;
            _lastProgress = 0f;
            _animation.BeginCleave(actor);

            if (_snapshotTargets)
                CaptureSnapshot();

            _logger.Debug($"Cleave started actor={actor.Index}, radius={_radius:0.00}, sweep={settings.CleaveSweepDegrees:0}, snapshot={_snapshotTargets}.");
            return true;
        }

        public bool Tick(float dt)
        {
            if (!_active) return true;
            if (_actor == null || !_actor.IsActive() || _actor.Health <= 0f)
            {
                Cleanup();
                return true;
            }

            _elapsed += Math.Max(0f, dt);
            var progress = Progress;
            var deltaProgress = Math.Max(0f, progress - _lastProgress);
            var rotation = (float)((int)_direction * _sweepRadians * deltaProgress);
            _animation.RotateActor(_actor, rotation);
            _animation.SetCleaveProgress(_actor, progress);
            _trailAccumulator += Math.Max(0f, dt);
            if (_trailAccumulator >= 0.06f && _trailBursts < 12)
            {
                _trailAccumulator = 0f;
                _trailBursts++;
                var tip = _actor.GetChestGlobalPosition() + _actor.LookDirection * Math.Min(1.5f, _radius * 0.35f);
                _effects.WeaponTrail(tip);
            }

            try
            {
                if (_snapshotTargets)
                    ProcessSnapshot(progress);
                else
                    ProcessLive(progress);
            }
            catch (Exception ex)
            {
                _logger.Error("Cleave sweep was cancelled after an exception.", ex);
                Cleanup();
                return true;
            }

            _lastProgress = progress;
            if (progress >= 1f)
            {
                _logger.Debug($"Cleave completed hits={_successfulHits}, largestCandidateSet={_largestCandidateSet}.");
                Cleanup(false);
                return true;
            }
            return false;
        }

        public void Cleanup() => Cleanup(true);

        private void Cleanup(bool resetMetrics)
        {
            _active = false;
            _elapsed = 0f;
            _duration = 0f;
            _lastProgress = 0f;
            _snapshotSchedule.Clear();
            _hits.Clear();
            _candidates.Clear();
            _schedule.Clear();
            _nearby.Clear();
            _trailAccumulator = 0f;
            _trailBursts = 0;
            if (_actor != null)
                _animation.ResetActionSpeed(_actor);
            _actor = null;
            _weapon = default(MissionWeapon);
            if (resetMetrics)
            {
                _successfulHits = 0;
                _largestCandidateSet = 0;
            }
        }

        private void CaptureSnapshot()
        {
            BuildSchedule(0f);
            for (var i = 0; i < _schedule.Count; i++)
            {
                var target = _schedule[i];
                _snapshotSchedule.Add(new ScheduledSweepTarget<int>(
                    target.Value.Index,
                    target.Progress,
                    target.DistanceSquared,
                    target.Ordinal));
            }
        }

        private void ProcessSnapshot(float progress)
        {
            if (_snapshotSchedule.Count == 0) return;
            for (var i = 0; i < _snapshotSchedule.Count; i++)
            {
                var planned = _snapshotSchedule[i];
                if (planned.Progress > progress || _hits.Contains(planned.Value))
                    continue;
                var target = _mission.FindAgentWithIndex(planned.Value);
                if (IsEligible(target, false))
                    TryHit(target, (float)planned.Progress);
            }
        }

        private void ProcessLive(float progress)
        {
            BuildSchedule(_lastProgress);
            for (var i = 0; i < _schedule.Count; i++)
            {
                var target = _schedule[i];
                if (target.Progress <= progress + 0.018)
                    TryHit(target.Value, (float)target.Progress);
            }
        }

        private void BuildSchedule(float progress)
        {
            _nearby.Clear();
            if (_friendlyFire)
                _mission.GetNearbyAgents(_actor.Position.AsVec2, _radius, _nearby);
            else
                _mission.GetNearbyEnemyAgents(_actor.Position.AsVec2, _radius, _actor.Team, _nearby);

            _candidates.Clear();
            for (var i = 0; i < _nearby.Count; i++)
            {
                var target = _nearby[i];
                if (!IsEligible(target, true)) continue;
                var delta = target.Position - _actor.Position;
                var distanceSquared = delta.x * delta.x + delta.y * delta.y;
                var angle = AngleMath.NormalizeRadians(Math.Atan2(delta.y, delta.x));
                if (AngleMath.HasSweepPassed(_startAngle, angle, progress, _sweepRadians, _direction))
                    continue;
                _candidates.Add(new SweepTarget<Agent>(target, angle, distanceSquared, target.Index));
            }

            _largestCandidateSet = Math.Max(_largestCandidateSet, _candidates.Count);
            SweepPlanner.BuildSchedule(
                _candidates,
                _startAngle,
                _sweepRadians,
                _direction,
                _radius,
                0,
                _schedule);
        }

        private bool IsEligible(Agent target, bool enforceRegistry)
        {
            if (target == null || target == _actor || !target.IsActive() || target.Health <= 0f)
                return false;
            if (enforceRegistry && _hits.Contains(target.Index))
                return false;
            if (!_targetMounts && target.IsMount)
                return false;
            if (!_friendlyFire && (_actor.Team == null || target.Team == null || !_actor.Team.IsEnemyOf(target.Team)))
                return false;
            return target.IsHuman || target.IsMount;
        }

        private void TryHit(Agent target, float expectedProgress)
        {
            if (!IsEligible(target, true))
                return;
            if (_maximumTargets > 0 && _successfulHits >= _maximumTargets)
                return;
            if (!_blows.ApplyMeleeBlow(
                    _actor,
                    target,
                    _weapon,
                    _damageMultiplier,
                    _knockback,
                    _knockdownThreshold,
                    expectedProgress))
                return;

            _hits.TryRegister(target.Index);
            _successfulHits++;
            _effects.Impact(target.GetChestGlobalPosition());
        }
    }
}
