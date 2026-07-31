using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Bend Time leaves mission time itself at native 1.0x and slows only non-player mission
    /// agents. It uses Bannerlord's public custom-driven-property push, native per-agent maximum
    /// speed limits and the two verified action channels. The player and controlled mount are
    /// never mutated.
    /// </summary>
    internal sealed class TimeControlService
    {
        private const int NativeActionChannelCount = 2;
        private const int RefreshBudgetPerTick = 192;
        private const float MinimumFactor = 0.02f;

        private readonly Mission _mission;
        private readonly VoidstepLogger _logger;
        private readonly Dictionary<int, SlowState> _states = new Dictionary<int, SlowState>();
        private readonly List<int> _refreshOrder = new List<int>();

        private Agent _player;
        private Agent _mount;
        private bool _active;
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
            if (player == null || !player.IsActive() || duration <= 0f)
                return false;

            var requestedMinimum = allowCompleteSuspension ? 0.01f : MinimumFactor;
            _factor = Math.Max(requestedMinimum, Math.Min(1f, requestedFactor));
            _remaining = duration;
            _lastApplicationTime = MBCommon.GetApplicationTime();
            _player = player;
            _mount = GetActiveMount(player);
            _active = true;
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
            if (_player == null || !_player.IsActive() || _player.Health <= 0f)
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

        public void RegisterAgent(Agent agent)
        {
            if (agent == null || agent.Index < 0)
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
            if (!_active || speed <= 0f || IsExempt(shooter))
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
        }

        public void Cleanup()
        {
            Release();
            _states.Clear();
            _refreshOrder.Clear();
            _refreshCursor = 0;
        }

        private void ReconcileKnownAgents()
        {
            if (_mission == null)
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
                {
                    _states.Remove(index);
                    continue;
                }

                if (IsExempt(agent))
                    Restore(state);
                else
                    Apply(state);
                refreshed++;
            }
        }

        private bool IsExempt(Agent agent)
        {
            if (agent == null)
                return false;
            return ReferenceEquals(agent, _player) || ReferenceEquals(agent, _mount);
        }

        private bool Apply(SlowState state)
        {
            var agent = state?.Agent;
            if (agent == null || !agent.IsActive() || IsExempt(agent))
            {
                Restore(state);
                return false;
            }

            try
            {
                // Rebuild the unmodified baseline first. Bannerlord may recalculate driven values
                // between Voidstep refreshes; starting from native values prevents multiplier stacking.
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
                agent.SetMaximumSpeedLimit(_factor, true);
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
                if (!state.FailureLogged)
                {
                    state.FailureLogged = true;
                    _logger.Debug(
                        "Bend Time could not slow mission agent=" + agent.Index +
                        " safely: " + Unwrap(ex).Message);
                }
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
                    // Native recalculation is the safest restoration boundary because it preserves
                    // current equipment, perks and other model changes instead of replaying stale data.
                    if (state.PropertiesOwned)
                        agent.UpdateAgentProperties();

                    if (state.ActionSpeedOwned)
                    {
                        for (var channel = 0; channel < NativeActionChannelCount; channel++)
                            agent.SetCurrentActionSpeed(channel, 1f);
                    }

                    if (state.SpeedLimitOwned)
                    {
                        var current = agent.GetMaximumSpeedLimit();
                        if (Approximately(current, state.AppliedMaximumSpeedLimit))
                            agent.SetMaximumSpeedLimit(state.OriginalMaximumSpeedLimit, false);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!state.FailureLogged)
                {
                    state.FailureLogged = true;
                    _logger.Debug(
                        "Bend Time agent restoration failed safely for index=" +
                        (agent != null ? agent.Index.ToString() : "none") + ": " +
                        Unwrap(ex).Message);
                }
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

        private static Agent GetActiveMount(Agent player)
        {
            var mount = player?.MountAgent;
            return mount != null && mount.IsActive() ? mount : null;
        }

        private static bool Approximately(float left, float right)
        {
            return Math.Abs(left - right) <=
                   0.001f * Math.Max(1f, Math.Max(Math.Abs(left), Math.Abs(right)));
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is System.Reflection.TargetInvocationException invocation &&
                   invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }

        // Compatibility targets retained for dormant branch-local Harmony metadata. The selective
        // implementation never invokes player compensation.
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
