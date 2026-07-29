using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    internal sealed class DominoLinkService
    {
        private const int PropagatedDeathSuppressionTicks = 4;

        private readonly Mission _mission;
        private readonly BlowFactory _blows;
        private readonly EffectController _effects;
        private readonly VoidstepLogger _logger;
        private readonly MBList<Agent> _nearby = new MBList<Agent>();
        private readonly List<AgentDistance> _sorted = new List<AgentDistance>(32);
        private readonly Dictionary<int, Agent> _linked = new Dictionary<int, Agent>();
        private readonly Dictionary<int, GameEntity> _markers = new Dictionary<int, GameEntity>();
        private readonly List<int> _removeBuffer = new List<int>(16);
        private readonly List<int> _snapshotBuffer = new List<int>(32);
        private readonly List<GameEntity> _markerBuffer = new List<GameEntity>(32);
        private readonly List<PendingPropagation> _pending = new List<PendingPropagation>(32);
        private readonly List<PendingPropagation> _dispatchBuffer = new List<PendingPropagation>(32);
        private readonly Dictionary<int, int> _propagatedDeathSuppression = new Dictionary<int, int>();
        private readonly List<int> _suppressionRemoveBuffer = new List<int>(16);
        private Agent _player;
        private int _tickSerial;
        private bool _dispatching;

        public DominoLinkService(Mission mission, BlowFactory blows, EffectController effects, VoidstepLogger logger)
        {
            _mission = mission;
            _blows = blows;
            _effects = effects;
            _logger = logger;
        }

        public int Count => _linked.Count;

        public int Mark(Agent player)
        {
            Clear();
            if (player == null || !player.IsActive() || player.Team == null)
                return 0;
            _player = player;
            var settings = VoidstepSettings.Current;
            _nearby.Clear();
            _mission.GetNearbyEnemyAgents(player.Position.AsVec2, settings.DominoRange, player.Team, _nearby);
            _sorted.Clear();
            for (var i = 0; i < _nearby.Count; i++)
            {
                var target = _nearby[i];
                if (!TargetingService.IsUsableTarget(player, target, true) || !target.IsHuman)
                    continue;
                var delta = target.Position - player.Position;
                _sorted.Add(new AgentDistance(target, delta.x * delta.x + delta.y * delta.y));
            }
            _sorted.Sort(CompareAgentDistance);
            var limit = Math.Min(settings.DominoMaximumLinks, _sorted.Count);
            for (var i = 0; i < limit; i++)
            {
                var target = _sorted[i].Agent;
                if (_linked.ContainsKey(target.Index)) continue;
                _linked.Add(target.Index, target);
                var marker = _effects.CreateWorldMarker(target.GetChestGlobalPosition() + Vec3.Up * 0.75f, 0xC060FFFFu);
                if (marker != null) _markers[target.Index] = marker;
            }
            _nearby.Clear();
            _sorted.Clear();
            return _linked.Count;
        }

        public void Tick()
        {
            _tickSerial++;
            ExpirePropagationSuppressions();
            DispatchPendingPropagations();

            if (_linked.Count == 0) return;
            _removeBuffer.Clear();
            foreach (var pair in _linked)
            {
                var agent = ResolveLinkedAgent(pair.Key, pair.Value);
                if (!IsLinkedAgentValid(agent))
                {
                    _removeBuffer.Add(pair.Key);
                    continue;
                }
                if (_markers.TryGetValue(pair.Key, out var marker))
                    _effects.MoveMarker(marker, agent.GetChestGlobalPosition() + Vec3.Up * 0.75f);
            }
            for (var i = 0; i < _removeBuffer.Count; i++) Remove(_removeBuffer[i]);
            _removeBuffer.Clear();
        }

        public void OnAgentHit(Agent affectedAgent, Agent affectorAgent, ref Blow blow)
        {
            var settings = VoidstepSettings.Current;
            if (!settings.DominoPropagateDamage || affectedAgent == null || affectorAgent != _player || !MatchesLinkedAgent(affectedAgent))
                return;

            // Never register a new blow while Bannerlord is still inside Agent.HandleBlow.
            // Re-entering the native melee callback corrupts its by-ref collision state and
            // can surface as an AccessViolationException from MonoMod.Utils.
            if ((blow.BlowFlag & BlowFlags.NoSound) != 0)
                return;

            var damage = Math.Max(0, (int)Math.Round(blow.InflictedDamage * settings.DominoDamageFactor));
            var flags = BlowFlags.NoSound;
            if (settings.DominoPropagateKnockdown && (blow.BlowFlag & BlowFlags.KnockDown) != 0)
                flags |= BlowFlags.KnockDown;
            var magnitude = Math.Max(1f, blow.BaseMagnitude * settings.DominoDamageFactor);

            CopyLinkedIds();
            var queued = 0;
            for (var i = 0; i < _snapshotBuffer.Count; i++)
            {
                var id = _snapshotBuffer[i];
                if (id == affectedAgent.Index) continue;
                if (!_linked.TryGetValue(id, out var identity)) continue;
                var target = ResolveLinkedAgent(id, identity);
                if (!IsLinkedAgentValid(target)) { Remove(id); continue; }
                _pending.Add(new PendingPropagation(id, identity, damage, blow.DamageType, flags, magnitude, false));
                queued++;
            }
            if (queued > 0)
                _logger.Debug($"Queued Domino damage propagation source={affectedAgent.Index}, targets={queued}, damage={damage}.");
        }

        public void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState state)
        {
            if (affectedAgent == null || !MatchesLinkedAgent(affectedAgent))
                return;

            var propagatedRemoval = ConsumePropagatedDeathSuppression(affectedAgent.Index);
            var shouldPropagateDeath = !propagatedRemoval && VoidstepSettings.Current.DominoPropagateDeath && affectorAgent == _player;
            Remove(affectedAgent.Index);
            if (!shouldPropagateDeath || _player == null || !_player.IsActive())
                return;

            CopyLinkedIds();
            var queued = 0;
            for (var i = 0; i < _snapshotBuffer.Count; i++)
            {
                var id = _snapshotBuffer[i];
                if (!_linked.TryGetValue(id, out var identity)) continue;
                var target = ResolveLinkedAgent(id, identity);
                if (!IsLinkedAgentValid(target)) { Remove(id); continue; }
                var lethalDamage = (int)Math.Ceiling(target.Health + 1f);
                _pending.Add(new PendingPropagation(id, identity, lethalDamage, DamageTypes.Blunt, BlowFlags.NoSound, target.Health + 1f, true));
                queued++;
            }
            if (queued > 0)
                _logger.Debug($"Queued Domino death propagation source={affectedAgent.Index}, targets={queued}.");
        }

        public void OnAgentDeleted(Agent agent)
        {
            if (agent == null) return;
            _propagatedDeathSuppression.Remove(agent.Index);
            if (MatchesLinkedAgent(agent)) Remove(agent.Index);
        }

        public void Clear()
        {
            _markerBuffer.Clear();
            foreach (var marker in _markers.Values) _markerBuffer.Add(marker);
            for (var i = 0; i < _markerBuffer.Count; i++) _effects.RemoveMarker(_markerBuffer[i]);
            _markerBuffer.Clear();
            _markers.Clear();
            _linked.Clear();
            _nearby.Clear();
            _sorted.Clear();
            _pending.Clear();
            _dispatchBuffer.Clear();
            _propagatedDeathSuppression.Clear();
            _suppressionRemoveBuffer.Clear();
            _dispatching = false;
            _player = null;
        }

        private void DispatchPendingPropagations()
        {
            if (_dispatching || _pending.Count == 0 || _player == null || !_player.IsActive())
                return;

            _dispatching = true;
            _dispatchBuffer.Clear();
            for (var i = 0; i < _pending.Count; i++)
                _dispatchBuffer.Add(_pending[i]);
            _pending.Clear();

            var applied = 0;
            try
            {
                for (var i = 0; i < _dispatchBuffer.Count; i++)
                {
                    var entry = _dispatchBuffer[i];
                    if (!_linked.TryGetValue(entry.TargetId, out var identity) || !ReferenceEquals(identity, entry.Identity))
                        continue;
                    var target = ResolveLinkedAgent(entry.TargetId, identity);
                    if (!IsLinkedAgentValid(target)) { Remove(entry.TargetId); continue; }

                    var mayKill = entry.Lethal || entry.Damage >= Math.Ceiling(target.Health);
                    if (mayKill)
                        _propagatedDeathSuppression[entry.TargetId] = _tickSerial + PropagatedDeathSuppressionTicks;
                    if (_blows.ApplyDirectBlow(_player, target, entry.Damage, entry.DamageType, entry.Flags, entry.Magnitude))
                    {
                        applied++;
                    }
                    else if (mayKill)
                    {
                        _propagatedDeathSuppression.Remove(entry.TargetId);
                    }
                }
            }
            finally
            {
                _dispatchBuffer.Clear();
                _dispatching = false;
            }

            if (applied > 0)
                _logger.Debug($"Dispatched {applied} deferred Domino propagation blow{(applied == 1 ? string.Empty : "s")} after the native hit callback completed.");
        }

        private void ExpirePropagationSuppressions()
        {
            if (_propagatedDeathSuppression.Count == 0) return;
            _suppressionRemoveBuffer.Clear();
            foreach (var pair in _propagatedDeathSuppression)
                if (pair.Value < _tickSerial) _suppressionRemoveBuffer.Add(pair.Key);
            for (var i = 0; i < _suppressionRemoveBuffer.Count; i++)
                _propagatedDeathSuppression.Remove(_suppressionRemoveBuffer[i]);
            _suppressionRemoveBuffer.Clear();
        }

        private bool ConsumePropagatedDeathSuppression(int agentId)
        {
            if (!_propagatedDeathSuppression.TryGetValue(agentId, out var expiry))
                return false;
            _propagatedDeathSuppression.Remove(agentId);
            return expiry >= _tickSerial;
        }

        private void CopyLinkedIds()
        {
            _snapshotBuffer.Clear();
            foreach (var id in _linked.Keys) _snapshotBuffer.Add(id);
            _snapshotBuffer.Sort();
        }

        private Agent ResolveLinkedAgent(int id, Agent identity)
        {
            var resolved = _mission.FindAgentWithIndex(id);
            return ReferenceEquals(resolved, identity) ? resolved : null;
        }

        private bool MatchesLinkedAgent(Agent agent) =>
            agent != null && _linked.TryGetValue(agent.Index, out var identity) && ReferenceEquals(agent, identity);

        private bool IsLinkedAgentValid(Agent agent) =>
            agent != null && agent.IsActive() && agent.Health > 0f && agent != _player &&
            _player != null && _player.Team != null && agent.Team != null && _player.Team.IsEnemyOf(agent.Team);

        private void Remove(int id)
        {
            _linked.Remove(id);
            if (_markers.TryGetValue(id, out var marker))
            {
                _effects.RemoveMarker(marker);
                _markers.Remove(id);
            }
        }

        private static int CompareAgentDistance(AgentDistance left, AgentDistance right)
        {
            var distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
            return distance != 0 ? distance : left.Agent.Index.CompareTo(right.Agent.Index);
        }

        private readonly struct PendingPropagation
        {
            public PendingPropagation(int targetId, Agent identity, int damage, DamageTypes damageType, BlowFlags flags, float magnitude, bool lethal)
            {
                TargetId = targetId;
                Identity = identity;
                Damage = damage;
                DamageType = damageType;
                Flags = flags;
                Magnitude = magnitude;
                Lethal = lethal;
            }

            public int TargetId { get; }
            public Agent Identity { get; }
            public int Damage { get; }
            public DamageTypes DamageType { get; }
            public BlowFlags Flags { get; }
            public float Magnitude { get; }
            public bool Lethal { get; }
        }

        private readonly struct AgentDistance
        {
            public AgentDistance(Agent agent, float distanceSquared) { Agent = agent; DistanceSquared = distanceSquared; }
            public Agent Agent { get; }
            public float DistanceSquared { get; }
        }
    }
}
