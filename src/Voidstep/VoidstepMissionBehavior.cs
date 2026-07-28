using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class VoidstepMissionBehavior : MissionLogic
    {
        private VoidstepLogger _logger;
        private AbilityManager _manager;
        private InputRouter _input;
        private Agent _lastPlayer;
        private bool _cleaned;
        private bool _wasEnabled;
        private bool _readyNoticeShown;

        public VoidstepMissionBehavior(VoidstepLogger logger)
        {
            _logger = logger ?? new VoidstepLogger();
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _logger.Info("Mission behavior initialization started.");
            try
            {
                var context = new AbilityContext(Mission, _logger);
                _manager = new AbilityManager(context);
                _input = new InputRouter(Mission, _logger);
                _lastPlayer = Mission.MainAgent;
                _wasEnabled = VoidstepSettings.Current.Enabled;
                _logger.Info($"Mission behavior initialized. Log: {_logger.PrimaryPath ?? "engine log only"}.");
            }
            catch (Exception ex)
            {
                _cleaned = true;
                _manager = null;
                _input = null;
                _logger.Error("Mission behavior initialization failed; abilities were disabled for this mission.", ex);
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (_cleaned || _manager == null) return;
            try
            {
                var enabled = VoidstepSettings.Current.Enabled;
                if (!enabled)
                {
                    if (_wasEnabled) _manager.Cleanup(CancelReason.UserCancelled);
                    _wasEnabled = false;
                    return;
                }
                _wasEnabled = true;
                if (Mission.MissionEnded || Mission.MissionIsEnding)
                {
                    Cleanup(CancelReason.MissionEnded);
                    return;
                }

                var current = Mission.MainAgent;
                if (!_readyNoticeShown && current != null && current.IsActive())
                {
                    _readyNoticeShown = true;
                    _logger.Info("Runtime ready. Default controls are Ctrl+1 through Ctrl+6 unless changed in MCM.");
                    try
                    {
                        InformationManager.DisplayMessage(new InformationMessage("Voidstep v1.0.1 active — use Ctrl+1 through Ctrl+6 by default."));
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Readiness notification was unavailable: {ex.GetType().Name}.");
                    }
                }
                if (!ReferenceEquals(current, _lastPlayer))
                {
                    _manager.OnPlayerAgentChanged(_lastPlayer, current);
                    _lastPlayer = current;
                }

                _manager.Tick(dt);

                var ability = _input.PollAbility();
                if (ability.HasValue)
                    _manager.TryActivate(ability.Value);
            }
            catch (Exception ex)
            {
                _logger.Error("A mission tick failed; all owned ability state was cleaned up.", ex);
                Cleanup(CancelReason.Exception);
            }
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            base.OnAgentHit(affectedAgent, affectorAgent, in affectorWeapon, in blow, in attackCollisionData);
            if (_cleaned || _manager == null) return;
            var propagatedBlow = blow;
            try { _manager.OnAgentHit(affectedAgent, affectorAgent, ref propagatedBlow); }
            catch (Exception ex)
            {
                _logger.Error("Domino hit propagation failed safely.", ex);
            }
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);
            if (_cleaned || _manager == null) return;
            try
            {
                _manager.OnAgentRemoved(affectedAgent, affectorAgent, agentState);
                if (affectedAgent != null && _lastPlayer != null && affectedAgent.Index == _lastPlayer.Index)
                {
                    Cleanup(CancelReason.ActorDied);
                }
            }
            catch (Exception ex) { _logger.Error("Agent-removal cleanup failed safely.", ex); }
        }

        public override void OnAgentDeleted(Agent affectedAgent)
        {
            base.OnAgentDeleted(affectedAgent);
            if (_cleaned || _manager == null) return;
            try { _manager.OnAgentDeleted(affectedAgent); }
            catch (Exception ex) { _logger.Error("Agent-deletion cleanup failed safely.", ex); }
        }

        public override void OnAgentControllerSetToPlayer(Agent agent)
        {
            base.OnAgentControllerSetToPlayer(agent);
            if (_cleaned || _manager == null) return;
            try
            {
                if (!ReferenceEquals(agent, _lastPlayer))
                {
                    _manager.OnPlayerAgentChanged(_lastPlayer, agent);
                    _lastPlayer = agent;
                }
            }
            catch (Exception ex) { _logger.Error("Player-agent replacement cleanup failed safely.", ex); }
        }

        protected override void OnEndMission()
        {
            Cleanup(CancelReason.MissionEnded);
            base.OnEndMission();
        }

        private void Cleanup(CancelReason reason)
        {
            if (_cleaned) return;
            _cleaned = true;
            try { _manager?.Cleanup(reason); }
            catch (Exception ex) { _logger?.Error("Mission cleanup encountered an error.", ex); }
            _logger?.Info("Mission behavior cleaned up.");
            _manager = null;
            _input = null;
            _lastPlayer = null;
        }
    }
}
