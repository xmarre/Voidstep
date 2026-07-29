using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class DominoLinkService
    {
        private readonly Mission _mission;
        private readonly BlowFactory _blows;
        private readonly EffectController _effects;
        private readonly VoidstepLogger _logger;
        private readonly MBList<Agent> _nearby = new MBList<Agent>();
        private readonly List<AgentDistance> _sorted = new List<AgentDistance>(32);
        private readonly Dictionary<int, Agent> _linked = new Dictionary<int, Agent>();
        private readonly Dictionary<int, GameEntity> _markers = new Dictionary<int, GameEntity>();
        private readonly RecursionGuard<int> _guard = new RecursionGuard<int>();
        private readonly List<int> _removeBuffer = new List<int>(16);
        private readonly List<int> _snapshotBuffer = new List<int>(32);
        private readonly List<GameEntity> _markerBuffer = new List<GameEntity>(32);
        private Agent _player;

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

            // Propagated Domino blows are tagged NoSound. The engine can deliver
            // OnAgentHit after RegisterBlow returns, so the synchronous guard alone
            // is insufficient to identify a delayed propagated callback.
            if ((blow.BlowFlag & BlowFlags.NoSound) != 0)
                return;

            using (var lease = _guard.Enter(1))
            {
                if (lease == null) return;
                var damage = Math.Max(0, (int)Math.Round(blow.InflictedDamage * settings.DominoDamageFactor));
                var flags = BlowFlags.NoSound;
                if (settings.DominoPropagateKnockdown && (blow.BlowFlag & BlowFlags.KnockDown) != 0)
                    flags |= BlowFlags.KnockDown;
                CopyLinkedIds();
                for (var i = 0; i < _snapshotBuffer.Count; i++)
                {
                    var id = _snapshotBuffer[i];
                    if (id == affectedAgent.Index) continue;
                    if (!_linked.TryGetValue(id, out var identity)) continue;
                    var target = ResolveLinkedAgent(id, identity);
                    if (!IsLinkedAgentValid(target)) { Remove(id); continue; }
                    _blows.ApplyDirectBlow(_player, target, damage, blow.DamageType, flags, Math.Max(1f, blow.BaseMagnitude * settings.DominoDamageFactor));
                }
            }
        }

        public void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState state)
        {
            if (affectedAgent == null || !MatchesLinkedAgent(affectedAgent))
                return;
            var shouldPropagateDeath = VoidstepSettings.Current.DominoPropagateDeath && affectorAgent == _player;
            Remove(affectedAgent.Index);
            if (!shouldPropagateDeath || _player == null || !_player.IsActive())
                return;

            using (var lease = _guard.Enter(1))
            {
                if (lease == null) return;
                CopyLinkedIds();
                for (var i = 0; i < _snapshotBuffer.Count; i++)
                {
                    var id = _snapshotBuffer[i];
                    if (!_linked.TryGetValue(id, out var identity)) continue;
                    var target = ResolveLinkedAgent(id, identity);
                    if (!IsLinkedAgentValid(target)) { Remove(id); continue; }
                    _blows.ApplyDirectBlow(_player, target, (int)Math.Ceiling(target.Health + 1f), DamageTypes.Blunt, BlowFlags.NoSound, target.Health + 1f);
                }
            }
        }

        public void OnAgentDeleted(Agent agent)
        {
            if (agent != null && MatchesLinkedAgent(agent)) Remove(agent.Index);
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
            _player = null;
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

        private readonly struct AgentDistance
        {
            public AgentDistance(Agent agent, float distanceSquared) { Agent = agent; DistanceSquared = distanceSquared; }
            public Agent Agent { get; }
            public float DistanceSquared { get; }
        }
    }
}
