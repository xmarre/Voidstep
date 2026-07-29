using System;
using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    internal sealed class DarkVisionService
    {
        private const uint UnawareColor = 0x5070FFFFu;
        private const uint AlertedColor = 0xE0A040FFu;
        private const uint EngagedColor = 0xE04060FFu;

        private readonly Mission _mission;
        private readonly VoidstepLogger _logger;
        private readonly MBList<Agent> _nearby = new MBList<Agent>();
        private readonly HashSet<int> _highlighted = new HashSet<int>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _staleBuffer = new List<int>(128);
        private float _refreshRemaining;
        private Agent _player;
        private bool _visibilityFailureLogged;
        private int _lastLoggedCount = -1;

        public DarkVisionService(Mission mission, VoidstepLogger logger)
        {
            _mission = mission;
            _logger = logger;
        }

        public bool Active { get; private set; }

        public bool Toggle(Agent player)
        {
            if (Active)
            {
                Disable();
                return false;
            }
            if (player == null || !player.IsActive() || player.Team == null)
                return false;
            _player = player;
            Active = true;
            _refreshRemaining = 0f;
            _visibilityFailureLogged = false;
            _lastLoggedCount = -1;
            Refresh();
            _logger.Debug($"Dark Vision started; initial highlights={_highlighted.Count}.");
            return true;
        }

        public void Tick(float dt)
        {
            if (!Active) return;
            if (_player == null || !_player.IsActive() || _player.Health <= 0f)
            {
                Disable();
                return;
            }
            _refreshRemaining -= Math.Max(0f, dt);
            if (_refreshRemaining > 0f) return;
            _refreshRemaining = Math.Max(0.1f, VoidstepSettings.Current.DarkVisionRefreshInterval);
            Refresh();
        }

        public void OnAgentDeleted(Agent agent)
        {
            if (agent == null || !_highlighted.Remove(agent.Index)) return;
            ClearContour(agent);
        }

        public void Disable()
        {
            var cleared = 0;
            _staleBuffer.Clear();
            foreach (var id in _highlighted) _staleBuffer.Add(id);
            for (var i = 0; i < _staleBuffer.Count; i++)
            {
                if (ClearContour(_mission.FindAgentWithIndex(_staleBuffer[i])))
                    cleared++;
            }
            _staleBuffer.Clear();
            _highlighted.Clear();
            _seen.Clear();
            _nearby.Clear();
            _refreshRemaining = 0f;
            _player = null;
            _visibilityFailureLogged = false;
            _lastLoggedCount = -1;
            Active = false;
            if (cleared > 0) _logger.Debug($"Dark Vision disabled; cleared={cleared}.");
        }

        private void Refresh()
        {
            _nearby.Clear();
            _seen.Clear();
            _mission.GetNearbyEnemyAgents(_player.Position.AsVec2, VoidstepSettings.Current.DarkVisionRange, _player.Team, _nearby);
            for (var i = 0; i < _nearby.Count; i++)
            {
                var agent = _nearby[i];
                if (!TargetingService.IsUsableTarget(_player, agent, true)) continue;
                _seen.Add(agent.Index);
                var color = ClassifyColor(agent);
                if (TrySetContour(agent, color))
                    _highlighted.Add(agent.Index);
            }

            _staleBuffer.Clear();
            foreach (var id in _highlighted)
                if (!_seen.Contains(id)) _staleBuffer.Add(id);
            for (var i = 0; i < _staleBuffer.Count; i++)
            {
                ClearContour(_mission.FindAgentWithIndex(_staleBuffer[i]));
                _highlighted.Remove(_staleBuffer[i]);
            }
            _staleBuffer.Clear();

            if (_lastLoggedCount != _highlighted.Count)
            {
                _logger.Debug($"Dark Vision refresh nearby={_nearby.Count}, highlighted={_highlighted.Count}.");
                _lastLoggedCount = _highlighted.Count;
            }
        }

        private uint ClassifyColor(Agent agent)
        {
            try
            {
                if (agent.GetLookAgent() == _player) return EngagedColor;
                if (agent.GetLastTargetVisibilityState() == AITargetVisibilityState.TargetIsClear)
                    return AlertedColor;
            }
            catch (Exception ex)
            {
                if (!_visibilityFailureLogged)
                {
                    _visibilityFailureLogged = true;
                    _logger.Debug("Dark Vision visibility-state lookup failed: " + ex.Message);
                }
            }
            return UnawareColor;
        }

        private bool TrySetContour(Agent agent, uint color)
        {
            if (agent?.AgentVisuals == null)
                return false;
            try
            {
                agent.AgentVisuals.SetContourColor(color, true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug("Dark Vision contour update failed: " + ex.Message);
                return false;
            }
        }

        private static bool ClearContour(Agent agent)
        {
            if (agent?.AgentVisuals == null)
                return false;
            try
            {
                agent.AgentVisuals.SetContourColor(null, false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
