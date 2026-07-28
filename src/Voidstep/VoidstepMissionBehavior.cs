using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class VoidstepMissionBehavior : MissionLogic
    {
        private readonly VoidstepLogger _logger;
        private AbilityManager _manager;
        private InputRouter _input;
        private Agent _lastPlayer;
        private bool _initializationAttempted;
        private bool _cleaned;
        private bool _wasEnabled;
        private bool _readyNoticeShown;
        private bool _controlConflictWarningShown;

        public VoidstepMissionBehavior(VoidstepLogger logger)
        {
            _logger = logger ?? new VoidstepLogger();
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            EnsureInitialized("OnBehaviorInitialize");
        }

        public override void EarlyStart()
        {
            base.EarlyStart();
            // Bannerlord 1.3.15 calls MBSubModuleBase.OnMissionBehaviorInitialize after
            // the normal OnBehaviorInitialize pass. Behaviors added by that hook first
            // receive EarlyStart, so this is the authoritative initialization path.
            EnsureInitialized("EarlyStart");
        }

        private void EnsureInitialized(string lifecycleStage)
        {
            if (_initializationAttempted || _cleaned) return;
            _initializationAttempted = true;
            _logger.Info($"Mission behavior initialization started during {lifecycleStage}.");
            try
            {
                var settings = VoidstepSettings.Current;
                if (settings.MigrateLegacyDefaultControls())
                    _logger.Info("Migrated legacy Ctrl+1 through Ctrl+6 defaults to Numpad1 through Numpad6 to avoid Bannerlord formation-selection commands.");

                var context = new AbilityContext(Mission, _logger);
                _manager = new AbilityManager(context);
                _input = new InputRouter(Mission, _logger);
                _lastPlayer = Mission.MainAgent;
                _wasEnabled = settings.Enabled;
                _logger.Info($"Mission behavior initialized. Controls: {settings.GetControlSummary()}. Log: {_logger.PrimaryPath ?? "engine log only"}.");
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
            if (!_initializationAttempted)
                EnsureInitialized("OnMissionTick fallback");
            if (_cleaned || _manager == null) return;
            try
            {
                var settings = VoidstepSettings.Current;
                var enabled = settings.Enabled;
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
                    var controls = settings.GetControlSummary();
                    _logger.Info($"Runtime ready. Controls: {controls}.");
                    TryDisplayNotice($"Voidstep v1.0.2 active — {controls}.");
                }
                if (!_controlConflictWarningShown && settings.HasNumberRowConflict())
                {
                    _controlConflictWarningShown = true;
                    const string warning = "Number-row Voidstep bindings also trigger Bannerlord formation selection. Rebind them to numpad or another unused key in MCM.";
                    _logger.Info("Control warning: " + warning);
                    TryDisplayNotice("Voidstep: " + warning);
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

        private void TryDisplayNotice(string message)
        {
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(message));
            }
            catch (Exception ex)
            {
                _logger.Debug($"Runtime notification was unavailable: {ex.GetType().Name}.");
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
                    Cleanup(CancelReason.ActorDied);
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
