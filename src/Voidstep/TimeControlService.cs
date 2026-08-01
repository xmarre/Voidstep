using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Bend Time leaves mission time itself at native 1.0x and slows only registered non-player
    /// agents belonging to this exact Mission. No Agent method is Harmony-patched. The owned
    /// mission behavior performs one normal refresh and one late-frame reassertion while active.
    /// </summary>
    internal sealed class TimeControlService
    {
        private const int NativeActionChannelCount = 2;
        private const int RefreshBudgetPerTick = 192;
        private const float MinimumFactor = 0.02f;
        private const float MinimumAbsoluteSpeed = 0.05f;

        private readonly Mission _mission;
        private readonly VoidstepLogger _logger;
        private readonly Dictionary<int, SlowState> _states = new Dictionary<int, SlowState>();
        private readonly List<int> _refreshOrder = new List<int>();

        private Agent _player;
        private Agent _mount;
        private bool _active;
        private bool _lateEnforcementLogged;
        private float _remaining;
        private float _factor = 1f;
        private float _lastApplicationTime;
        private int _refreshCursor;
        private int _lastAppliedCount;

        private sealed class SlowState
        {
            internal Agent Agent;
            internal bool PropertiesOwned;
            internal bool ActionSpeedOwned;
            internal bool SpeedLimitOwned;
            internal bool FailureLogged;

            internal float OriginalMaxSpeedMultiplier;
            internal float OriginalCombatMaxSpeedMultiplier;
            internal float OriginalTopSpeedReachDuration;
            internal float OriginalSwingSpeedMultiplier;
            internal float OriginalReadySpeedMultiplier;
            internal float OriginalReloadSpeed;
            internal float OriginalRangedReadySpeedMultiplier;
            internal float OriginalRangedReloadSpeedMultiplier;
            internal float OriginalHandlingMultiplier;
            internal float OriginalMountSpeed;
            internal float OriginalMountManeuver;
            internal float OriginalMountDashAcceleration;
            internal float OriginalMaximumSpeedLimit;

            internal float AppliedMaxSpeedMultiplier;
            internal float AppliedCombatMaxSpeedMultiplier;
            internal float AppliedTopSpeedReachDuration;
            internal float AppliedSwingSpeedMultiplier;
            internal float AppliedReadySpeedMultiplier;
            internal float AppliedReloadSpeed;
            internal float AppliedRangedReadySpeedMultiplier;
            internal float AppliedRangedReloadSpeedMultiplier;
            internal float AppliedHandlingMultiplier;
            internal float AppliedMountSpeed;
            internal float AppliedMountManeuver;
            internal float AppliedMountDashAcceleration;
            internal float AppliedMaximumSpeedLimit;
        }

        public TimeControlService(Mission mission, VoidstepLogger logger)
        {
            _mission = mission;
            _logger = logger;
            ReconcileKnownAgents();
        }

        public bool Active => _active;
        public float Remaining => _remaining;
        internal float Factor => _factor;

        public bool Begin(Agent player, float requestedFactor, float duration, bool allowCompleteSuspension)
        {
            Release();
            if (player == null || !player.IsActive() || duration <= 0f ||
                _mission == null || !ReferenceEquals(Mission.Current, _mission))
                return false;

            var requestedMinimum = allowCompleteSuspension ? 0.01f : MinimumFactor;
            _factor = Math.Max(requestedMinimum, Math.Min(1f, requestedFactor));
            _remaining = duration;
            _lastApplicationTime = MBCommon.GetApplicationTime();
            _player = player;
            _mount = GetActiveMount(player);
            _active = true;
            _lateEnforcementLogged = false;
            _refreshCursor = 0;

            ReconcileKnownAgents();
            _lastAppliedCount = ApplyToAllKnownAgents();

            _logger.Debug(
                "Bend Time selective dilation started factor=" + _factor.ToString("0.00") +
                ", registeredAgents=" + _states.Count +
                ", slowedAgents=" + _lastAppliedCount +
                "; scene, player and controlled mount remain native 1.00x.");
            if (_lastAppliedCount == 0)
                _logger.Debug("Bend Time found no eligible non-player mission agents to slow.");
            return true;
        }

        public void Tick(float dt)
        {
            if (!_active)
                return;
            if (!OwnsCurrentMission() || _player == null || !_player.IsActive() || _player.Health <= 0f)
            {
                Release();
                return;
            }

            var now = MBCommon.GetApplicationTime();
            var realDt = _lastApplicationTime > 0f
                ? Math.Max(0f, now - _lastApplicationTime)
                : Math.Max(0f, dt);
            _lastApplicationTime = now;
            _remaining -= realDt;

            RefreshPlayerExemptions();
            RefreshBudgetedAgents();

            if (_remaining <= 0f)
                Release();
        }

        /// <summary>
        /// Called only by VoidstepMissionBehavior.OnPreDisplayMissionTick. Bannerlord may rebuild
        /// driven properties or actions after normal mission behavior ticks, so this reasserts the
        /// already captured values without patching Agent.UpdateAgentProperties, SetActionChannel,
        /// SetCurrentActionSpeed or SetMaximumSpeedLimit globally.
        /// </summary>
        internal void LateTick()
        {
            if (!_active || !OwnsCurrentMission())
                return;

            RefreshPlayerExemptions();
            var enforced = 0;
            foreach (var pair in _states)
            {
                var state = pair.Value;
                if (state == null || state.Agent == null || !state.Agent.IsActive() ||
                    IsExempt(state.Agent) || !state.PropertiesOwned)
                    continue;
                if (ReassertOwnedState(state))
                    enforced++;
            }

            if (!_lateEnforcementLogged)
            {
                _lateEnforcementLogged = true;
                _logger.Debug(
                    "Bend Time mission-owned late enforcement armed; agents=" + enforced +
                    ", globalAgentPatches=0.");
            }
        }

        public void RegisterAgent(Agent agent)
        {
            if (agent == null || agent.Index < 0 || !OwnsCurrentMission())
                return;

            SlowState state;
            if (_states.TryGetValue(agent.Index, out state))
            {
                if (ReferenceEquals(state.Agent, agent))
                    return;
                Restore(state);
                state = new SlowState { Agent = agent };
                _states[agent.Index] = state;
            }
            else
            {
                state = new SlowState { Agent = agent };
                _states.Add(agent.Index, state);
                _refreshOrder.Add(agent.Index);
            }

            if (_active && !IsExempt(agent))
                Apply(state);
        }

        public void UnregisterAgent(Agent agent)
        {
            if (agent == null)
                return;

            SlowState state;
            if (!_states.TryGetValue(agent.Index, out state) ||
                !ReferenceEquals(state.Agent, agent))
                return;

            Restore(state);
            _states.Remove(agent.Index);
            if (ReferenceEquals(agent, _mount))
                _mount = null;
        }

        internal void ScaleMissile(Agent shooter, ref float speed)
        {
            if (!_active || !OwnsCurrentMission() || speed <= 0f || IsExempt(shooter))
                return;
            speed = Math.Max(0.01f, speed * _factor);
        }

        public void Release()
        {
            if (_active)
            {
                _active = false;
                foreach (var pair in _states)
                    Restore(pair.Value);
                _logger.Debug("Bend Time selective dilation released; all owned non-player state restored.");
            }

            _remaining = 0f;
            _factor = 1f;
            _lastApplicationTime = 0f;
            _player = null;
            _mount = null;
            _refreshCursor = 0;
            _lastAppliedCount = 0;
            _lateEnforcementLogged = false;
        }

        public void Cleanup()
        {
            Release();
            _states.Clear();
            _refreshOrder.Clear();
            _refreshCursor = 0;
        }

        private bool OwnsCurrentMission()
        {
            return _mission != null && ReferenceEquals(Mission.Current, _mission);
        }

        private void ReconcileKnownAgents()
        {
            if (!OwnsCurrentMission())
                return;

            var agents = _mission.AllAgents;
            for (var i = 0; i < agents.Count; i++)
                RegisterAgent(agents[i]);
        }

        private int ApplyToAllKnownAgents()
        {
            var applied = 0;
            foreach (var pair in _states)
            {
                var state = pair.Value;
                if (state == null || state.Agent == null || IsExempt(state.Agent))
                {
                    Restore(state);
                    continue;
                }
                if (Apply(state))
                    applied++;
            }
            return applied;
        }

        private void RefreshPlayerExemptions()
        {
            var currentMount = GetActiveMount(_player);
            if (ReferenceEquals(currentMount, _mount))
                return;

            var previousMount = _mount;
            _mount = currentMount;

            SlowState state;
            if (currentMount != null && _states.TryGetValue(currentMount.Index, out state))
                Restore(state);
            if (previousMount != null && previousMount.IsActive() &&
                _states.TryGetValue(previousMount.Index, out state) && !IsExempt(previousMount))
                Apply(state);
        }

        private void RefreshBudgetedAgents()
        {
            var count = _refreshOrder.Count;
            if (count == 0)
                return;

            var inspected = 0;
            var refreshed = 0;
            while (inspected < count && refreshed < RefreshBudgetPerTick)
            {
                if (_refreshCursor >= count)
                    _refreshCursor = 0;
                var index = _refreshOrder[_refreshCursor++];
                inspected++;

                SlowState state;
                if (!_states.TryGetValue(index, out state) || state == null || state.Agent == null)
                    continue;

                var agent = state.Agent;
                if (!agent.IsActive())
                    continue;

                if (IsExempt(agent))
                    Restore(state);
                else
                    Apply(state);
                refreshed++;
            }
        }

        private bool IsExempt(Agent agent)
        {
            return agent != null &&
                   (ReferenceEquals(agent, _player) || ReferenceEquals(agent, _mount));
        }

        private bool Apply(SlowState state)
        {
            var agent = state?.Agent;
            if (agent == null || !agent.IsActive() || IsExempt(agent) || !OwnsCurrentMission())
            {
                Restore(state);
                return false;
            }

            try
            {
                if (state.SpeedLimitOwned)
                    agent.SetMaximumSpeedLimit(state.OriginalMaximumSpeedLimit, false);

                agent.UpdateAgentProperties();
                var driven = agent.AgentDrivenProperties;
                if (driven == null)
                    return false;

                CaptureBaseline(state, driven);
                ApplyScaledValues(state, driven);
                agent.UpdateCustomDrivenProperties();
                state.PropertiesOwned = true;

                if (!state.SpeedLimitOwned)
                    state.OriginalMaximumSpeedLimit = agent.GetMaximumSpeedLimit();
                var baselineSpeed = agent.GetCurrentSpeedLimit();
                if (float.IsNaN(baselineSpeed) || float.IsInfinity(baselineSpeed) || baselineSpeed <= 0.001f)
                    baselineSpeed = 0.5f;
                var absoluteLimit = Math.Max(MinimumAbsoluteSpeed, baselineSpeed * _factor);
                agent.SetMaximumSpeedLimit(absoluteLimit, false);
                state.AppliedMaximumSpeedLimit = agent.GetMaximumSpeedLimit();
                state.SpeedLimitOwned = true;

                for (var channel = 0; channel < NativeActionChannelCount; channel++)
                    agent.SetCurrentActionSpeed(channel, _factor);
                state.ActionSpeedOwned = true;
                state.FailureLogged = false;
                return true;
            }
            catch (Exception ex)
            {
                LogFailure(state, "apply", ex);
                return false;
            }
        }

        private bool ReassertOwnedState(SlowState state)
        {
            var agent = state?.Agent;
            if (agent == null || !agent.IsActive() || IsExempt(agent) || !OwnsCurrentMission())
                return false;

            try
            {
                var driven = agent.AgentDrivenProperties;
                if (driven == null)
                    return false;

                WriteAppliedValues(state, driven);
                agent.UpdateCustomDrivenProperties();
                if (state.SpeedLimitOwned)
                    agent.SetMaximumSpeedLimit(state.AppliedMaximumSpeedLimit, false);
                if (state.ActionSpeedOwned)
                {
                    for (var channel = 0; channel < NativeActionChannelCount; channel++)
                        agent.SetCurrentActionSpeed(channel, _factor);
                }
                state.FailureLogged = false;
                return true;
            }
            catch (Exception ex)
            {
                LogFailure(state, "late reassert", ex);
                return false;
            }
        }

        private void Restore(SlowState state)
        {
            if (state == null ||
                (!state.PropertiesOwned && !state.ActionSpeedOwned && !state.SpeedLimitOwned))
                return;

            var agent = state.Agent;
            try
            {
                if (agent != null && agent.IsActive())
                {
                    if (state.PropertiesOwned)
                        agent.UpdateAgentProperties();

                    if (state.ActionSpeedOwned)
                    {
                        for (var channel = 0; channel < NativeActionChannelCount; channel++)
                            agent.SetCurrentActionSpeed(channel, 1f);
                    }

                    if (state.SpeedLimitOwned)
                        agent.SetMaximumSpeedLimit(state.OriginalMaximumSpeedLimit, false);
                }
            }
            catch (Exception ex)
            {
                LogFailure(state, "restore", ex);
            }
            finally
            {
                state.PropertiesOwned = false;
                state.ActionSpeedOwned = false;
                state.SpeedLimitOwned = false;
            }
        }

        private static void CaptureBaseline(SlowState state, AgentDrivenProperties driven)
        {
            state.OriginalMaxSpeedMultiplier = driven.MaxSpeedMultiplier;
            state.OriginalCombatMaxSpeedMultiplier = driven.CombatMaxSpeedMultiplier;
            state.OriginalTopSpeedReachDuration = driven.TopSpeedReachDuration;
            state.OriginalSwingSpeedMultiplier = driven.SwingSpeedMultiplier;
            state.OriginalReadySpeedMultiplier = driven.ThrustOrRangedReadySpeedMultiplier;
            state.OriginalReloadSpeed = driven.ReloadSpeed;
            state.OriginalRangedReadySpeedMultiplier = driven.BipedalRangedReadySpeedMultiplier;
            state.OriginalRangedReloadSpeedMultiplier = driven.BipedalRangedReloadSpeedMultiplier;
            state.OriginalHandlingMultiplier = driven.HandlingMultiplier;
            state.OriginalMountSpeed = driven.MountSpeed;
            state.OriginalMountManeuver = driven.MountManeuver;
            state.OriginalMountDashAcceleration = driven.MountDashAccelerationMultiplier;
        }

        private void ApplyScaledValues(SlowState state, AgentDrivenProperties driven)
        {
            var inverse = 1f / Math.Max(MinimumFactor, _factor);
            state.AppliedMaxSpeedMultiplier = state.OriginalMaxSpeedMultiplier * _factor;
            state.AppliedCombatMaxSpeedMultiplier = state.OriginalCombatMaxSpeedMultiplier * _factor;
            state.AppliedTopSpeedReachDuration = Math.Max(0.01f, state.OriginalTopSpeedReachDuration * inverse);
            state.AppliedSwingSpeedMultiplier = state.OriginalSwingSpeedMultiplier * _factor;
            state.AppliedReadySpeedMultiplier = state.OriginalReadySpeedMultiplier * _factor;
            state.AppliedReloadSpeed = state.OriginalReloadSpeed * _factor;
            state.AppliedRangedReadySpeedMultiplier = state.OriginalRangedReadySpeedMultiplier * _factor;
            state.AppliedRangedReloadSpeedMultiplier = state.OriginalRangedReloadSpeedMultiplier * _factor;
            state.AppliedHandlingMultiplier = state.OriginalHandlingMultiplier * _factor;
            state.AppliedMountSpeed = state.OriginalMountSpeed * _factor;
            state.AppliedMountManeuver = state.OriginalMountManeuver * _factor;
            state.AppliedMountDashAcceleration = state.OriginalMountDashAcceleration * _factor;
            WriteAppliedValues(state, driven);
        }

        private static void WriteAppliedValues(SlowState state, AgentDrivenProperties driven)
        {
            driven.MaxSpeedMultiplier = state.AppliedMaxSpeedMultiplier;
            driven.CombatMaxSpeedMultiplier = state.AppliedCombatMaxSpeedMultiplier;
            driven.TopSpeedReachDuration = state.AppliedTopSpeedReachDuration;
            driven.SwingSpeedMultiplier = state.AppliedSwingSpeedMultiplier;
            driven.ThrustOrRangedReadySpeedMultiplier = state.AppliedReadySpeedMultiplier;
            driven.ReloadSpeed = state.AppliedReloadSpeed;
            driven.BipedalRangedReadySpeedMultiplier = state.AppliedRangedReadySpeedMultiplier;
            driven.BipedalRangedReloadSpeedMultiplier = state.AppliedRangedReloadSpeedMultiplier;
            driven.HandlingMultiplier = state.AppliedHandlingMultiplier;
            driven.MountSpeed = state.AppliedMountSpeed;
            driven.MountManeuver = state.AppliedMountManeuver;
            driven.MountDashAccelerationMultiplier = state.AppliedMountDashAcceleration;
        }

        private void LogFailure(SlowState state, string stage, Exception exception)
        {
            if (state == null || state.FailureLogged)
                return;
            state.FailureLogged = true;
            var agent = state.Agent;
            _logger.Debug(
                "Bend Time mission-owned " + stage + " failed safely for agent=" +
                (agent == null ? "none" : agent.Index.ToString()) + ": " +
                Unwrap(exception).Message);
        }

        private static Agent GetActiveMount(Agent player)
        {
            var mount = player?.MountAgent;
            return mount != null && mount.IsActive() ? mount : null;
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is System.Reflection.TargetInvocationException invocation &&
                   invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }

        // Compatibility targets retained for dormant branch-local Harmony metadata.
        private void ApplyPlayerCompensation(float compensation)
        {
        }

        private void RestoreCompensation()
        {
        }
    }

    [HarmonyPatch(typeof(Mission), "AddMissileAux")]
    internal static class BendTimeRegularMissileSpeedPatch
    {
        private static void Prefix(Mission __instance, Agent shooterAgent, ref float speed)
        {
            var service = __instance?.GetMissionBehavior<VoidstepMissionBehavior>()?.TimeControl;
            service?.ScaleMissile(shooterAgent, ref speed);
        }
    }

    [HarmonyPatch(typeof(Mission), "AddMissileSingleUsageAux")]
    internal static class BendTimeSingleUsageMissileSpeedPatch
    {
        private static void Prefix(Mission __instance, Agent shooterAgent, ref float speed)
        {
            var service = __instance?.GetMissionBehavior<VoidstepMissionBehavior>()?.TimeControl;
            service?.ScaleMissile(shooterAgent, ref speed);
        }
    }
}
