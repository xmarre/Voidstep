using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class VoidstepMissionBehavior : MissionLogic
    {
        private const float BindingRefreshInterval = 0.25f;
        private static readonly FieldInfo TimeField =
            AccessTools.Field(typeof(AbilityManager), "_time");

        private readonly VoidstepLogger _logger;
        private AbilityManager _manager;
        private InputRouter _input;
        private AbilityWheelCoordinator _wheel;
        private Agent _lastPlayer;
        private bool _initializationAttempted;
        private bool _cleaned;
        private bool _wasEnabled;
        private bool _readyNoticeShown;
        private bool _bindingConflictInitialized;
        private float _bindingRefreshRemaining;
        private string _lastBindingConflict;

        internal TimeControlService TimeControl
        {
            get
            {
                if (_manager == null || TimeField == null)
                    return null;
                try { return TimeField.GetValue(_manager) as TimeControlService; }
                catch { return null; }
            }
        }

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
            EnsureInitialized("EarlyStart");
        }

        private void EnsureInitialized(string lifecycleStage)
        {
            if (_initializationAttempted || _cleaned) return;
            _initializationAttempted = true;
            _logger.Info($"Mission behavior initialization started during {lifecycleStage}.");
            try
            {
                if (!VoidstepSubModule.NativeHotkeysReady || VoidstepHotKeyContext.Current == null)
                    throw new InvalidOperationException("Native Voidstep hotkeys are not registered.");

                VoidstepInputBindings.RefreshCacheIfChanged();
                var settings = VoidstepSettings.Current;
                var context = new AbilityContext(Mission, _logger);
                _manager = new AbilityManager(context);
                _input = new InputRouter(Mission, _logger);
                _wheel = new AbilityWheelCoordinator(Mission, _manager, _input, _logger);
                _lastPlayer = Mission.MainAgent;
                _wasEnabled = settings.Enabled;
                _bindingRefreshRemaining = BindingRefreshInterval;
                _logger.Info($"Mission behavior initialized. Q opens the ability wheel; Right Mouse Button confirms the selected cast. Direct bindings select abilities: {VoidstepInputBindings.GetSummary()}. Primary keys: Options > Keybindings > Voidstep. Modifiers: MCM > Controls. Log: {_logger.PrimaryPath ?? "engine log only"}.");
                CheckBindingConflict();
            }
            catch (Exception ex)
            {
                _cleaned = true;
                try { _wheel?.Cleanup(); } catch { }
                _wheel = null;
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
            if (_cleaned || _manager == null || _wheel == null) return;
            try
            {
                var settings = VoidstepSettings.Current;
                var enabled = settings.Enabled;
                if (!enabled)
                {
                    if (_wasEnabled) _manager.Cleanup(CancelReason.UserCancelled);
                    _wasEnabled = false;
                    InputConflictSuppression.Reset();
                    return;
                }
                _wasEnabled = true;
                if (Mission.MissionEnded || Mission.MissionIsEnding)
                {
                    Cleanup(CancelReason.MissionEnded);
                    return;
                }

                RefreshBindings(dt);

                var current = Mission.MainAgent;
                if (!_readyNoticeShown && current != null && current.IsActive())
                {
                    _readyNoticeShown = true;
                    var wheelName = _wheel.UsesTorWheel ? "TOR Q cast wheel" : "Voidstep Q cast wheel";
                    _logger.Info($"Runtime ready. Selection: {wheelName}; confirm=RightMouseButton. Direct selectors: {VoidstepInputBindings.GetSummary()}.");
                    TryDisplayNotice($"Voidstep v1.2.3 active — Q selects an ability; Right Mouse Button casts. Using {wheelName}.");
                }
                if (!ReferenceEquals(current, _lastPlayer))
                {
                    _manager.OnPlayerAgentChanged(_lastPlayer, current);
                    _lastPlayer = current;
                }

                _manager.Tick(dt);
                _wheel.Tick(dt);
            }
            catch (Exception ex)
            {
                _logger.Error("A mission tick failed; all owned ability state was cleaned up.", ex);
                Cleanup(CancelReason.Exception);
            }
        }

        private void RefreshBindings(float dt)
        {
            _bindingRefreshRemaining -= dt;
            if (!VoidstepInputBindings.IsCacheDirty && _bindingRefreshRemaining > 0f)
                return;

            _bindingRefreshRemaining = BindingRefreshInterval;
            if (!VoidstepInputBindings.RefreshCacheIfChanged())
                return;

            _logger.Info("Control bindings refreshed. Direct ability selectors: " + VoidstepInputBindings.GetSummary() + ".");
            CheckBindingConflict();
        }

        private void CheckBindingConflict()
        {
            var conflict = VoidstepInputBindings.GetConflictWarning();
            if (_bindingConflictInitialized &&
                string.Equals(conflict, _lastBindingConflict, StringComparison.Ordinal))
            {
                return;
            }

            var hadConflict = _bindingConflictInitialized && !string.IsNullOrEmpty(_lastBindingConflict);
            _bindingConflictInitialized = true;
            _lastBindingConflict = conflict;
            if (string.IsNullOrEmpty(conflict))
            {
                if (hadConflict)
                    _logger.Info("Voidstep ability-selector chord conflicts cleared.");
                return;
            }

            _logger.Info("Control conflict: " + conflict);
            TryDisplayNotice("Voidstep: " + conflict);
        }

        private void TryDisplayNotice(string message)
        {
            try { InformationManager.DisplayMessage(new InformationMessage(message)); }
            catch (Exception ex) { _logger.Debug($"Runtime notification was unavailable: {ex.GetType().Name}."); }
        }

        public override void OnAgentBuild(Agent agent, Banner banner)
        {
            base.OnAgentBuild(agent, banner);
            if (_cleaned) return;
            try { TimeControl?.RegisterAgent(agent); }
            catch (Exception ex) { _logger.Error("Bend Time agent registration failed safely.", ex); }
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            base.OnAgentHit(affectedAgent, affectorAgent, in affectorWeapon, in blow, in attackCollisionData);
            if (_cleaned || _manager == null) return;
            var propagatedBlow = blow;
            try { _manager.OnAgentHit(affectedAgent, affectorAgent, ref propagatedBlow); }
            catch (Exception ex) { _logger.Error("Domino hit propagation failed safely.", ex); }
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);
            if (_cleaned || _manager == null) return;
            try
            {
                TimeControl?.UnregisterAgent(affectedAgent);
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
            try
            {
                TimeControl?.UnregisterAgent(affectedAgent);
                _manager.OnAgentDeleted(affectedAgent);
            }
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
            try { _wheel?.Cleanup(); }
            catch (Exception ex) { _logger?.Error("Ability-wheel cleanup encountered an error.", ex); }
            try { _manager?.Cleanup(reason); }
            catch (Exception ex) { _logger?.Error("Mission cleanup encountered an error.", ex); }
            try { _input?.Cleanup(); }
            catch (Exception ex) { _logger?.Error("Input cleanup encountered an error.", ex); }
            _logger?.Info("Mission behavior cleaned up.");
            _wheel = null;
            _manager = null;
            _input = null;
            _lastPlayer = null;
            _lastBindingConflict = null;
            _bindingConflictInitialized = false;
        }
    }
}
