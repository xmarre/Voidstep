using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Bend Time deliberately leaves the mission scene at native 1.0x speed. Slowing the whole
    /// scene and trying to multiply the player back up cannot exempt native player simulation.
    /// Instead this service owns a mission-local registry and slows only non-player agents and
    /// newly launched non-player missiles. The main agent and current controlled mount are never
    /// mutated, so their movement, camera, attacks and animation remain genuinely real-time.
    /// </summary>
    internal sealed class TimeControlService
    {
        private const int NativeActionChannelCount = 2;
        private const int RefreshBudgetPerTick = 128;
        private const float MinimumFactor = 0.02f;

        private static readonly PropertyInfo DrivenValuesProperty =
            AccessTools.Property(typeof(AgentDrivenProperties), "Values");
        private static readonly MethodInfo PushDrivenPropertiesMethod =
            AccessTools.Method(typeof(Agent), "UpdateDrivenProperties", new[] { typeof(float[]) });

        private readonly Mission _mission;
        private readonly VoidstepLogger _logger;
        private readonly Dictionary<int, SlowState> _states = new Dictionary<int, SlowState>();
        private readonly List<int> _refreshOrder = new List<int>();

        private Agent _player;
        private Agent _exemptMount;
        private bool _active;
        private float _remaining;
        private float _factor = 1f;
        private float _lastApplicationTime;
        private int _refreshCursor;
        private bool _nativePushFailureLogged;

        private sealed class SlowState
        {
            internal Agent Agent;
            internal bool Applied;
            internal bool ActionSpeedOwned;
            internal bool FailureLogged;

            internal float OriginalMaxSpeed;
            internal float OriginalCombatMaxSpeed;
            internal float OriginalTopSpeedReachDuration;
            internal float OriginalSwingSpeed;
            internal float OriginalReadySpeed;
            internal float OriginalReloadSpeed;
            internal float OriginalRangedReadySpeed;
            internal float OriginalRangedReloadSpeed;
            internal float OriginalHandling;
            internal float OriginalMountSpeed;
            internal float OriginalMountManeuver;
            internal float OriginalMountDashAcceleration;

            internal float AppliedMaxSpeed;
            internal float AppliedCombatMaxSpeed;
            internal float AppliedTopSpeedReachDuration;
            internal float AppliedSwingSpeed;
            internal float AppliedReadySpeed;
            internal float AppliedReloadSpeed;
            internal float AppliedRangedReadySpeed;
            internal float AppliedRangedReloadSpeed;
            internal float AppliedHandling;
            internal float AppliedMountSpeed;
            internal float AppliedMountManeuver;
            internal float AppliedMountDashAcceleration;
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
            _exemptMount = GetActiveMount(player);
            _active = true;
            _refreshCursor = 0;
            _nativePushFailureLogged = false;

            ReconcileKnownAgents();
            ApplyToAllKnownAgents();

            _logger.Debug(
                "Bend Time selective dilation started factor=" + _factor.ToString("0.00") +
                ", registeredAgents=" + _states.Count +
                "; scene and controlled player remain native 1.00x.");
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

            SlowState existing;
            if (_states.TryGetValue(agent.Index, out existing))
            {
                if (ReferenceEquals(existing.Agent, agent))
                    return;
                Restore(existing);
                existing = new SlowState { Agent = agent };
                _states[agent.Index] = existing;
            }
            else
            {
                existing = new SlowState { Agent = agent };
                _states.Add(agent.Index, existing);
                _refreshOrder.Add(agent.Index);
            }

            if (_active && !IsExempt(agent))
                Apply(existing);
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
            if (ReferenceEquals(agent, _exemptMount))
                _exemptMount = null;
        }

        internal void ScaleMissile(Agent shooter, ref float speed)
        {
            if (!_active || speed <= 0f || IsExempt(shooter))
                return;
            speed = Math.Max(0.01f, speed * _factor);
        }

        public void Release()
        {
            if (!_active)
            {
                _remaining = 0f;
                _factor = 1f;
                _lastApplicationTime = 0f;
                _player = null;
                _exemptMount = null;
                return;
            }

            _active = false;
            foreach (var pair in _states)
                Restore(pair.Value);

            _remaining = 0f;
            _factor = 1f;
            _lastApplicationTime = 0f;
            _player = null;
            _exemptMount = null;
            _refreshCursor = 0;
            _logger.Debug("Bend Time selective dilation released; all owned agent state restored.");
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

        private void ApplyToAllKnownAgents()
        {
            foreach (var pair in _states)
            {
                var state = pair.Value;
                if (state?.Agent == null || IsExempt(state.Agent))
                {
                    Restore(state);
                    continue;
                }
                Apply(state);
            }
        }

        private void RefreshPlayerExemptions()
        {
            var currentMount = GetActiveMount(_player);
            if (ReferenceEquals(currentMount, _exemptMount))
                return;

            var previousMount = _exemptMount;
            _exemptMount = currentMount;

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
                if (!_states.TryGetValue(index, out state) || state?.Agent == null)
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
            return ReferenceEquals(agent, _player) || ReferenceEquals(agent, _exemptMount);
        }

        private void Apply(SlowState state)
        {
            var agent = state?.Agent;
            if (agent == null || !agent.IsActive() || IsExempt(agent))
            {
                Restore(state);
                return;
            }

            try
            {
                var driven = agent.AgentDrivenProperties;
                if (driven != null)
                {
                    var needsPush = !state.Applied;
                    if (!state.Applied)
                    {
                        try { agent.UpdateAgentProperties(); }
                        catch { }
                        Capture(state, driven);
                    }
                    else
                    {
                        needsPush = RefreshBaselines(state, driven);
                    }

                    if (needsPush)
                    {
                        ApplyDrivenValues(state, driven);
                        PushDrivenProperties(agent, driven);
                    }
                }

                for (var channel = 0; channel < NativeActionChannelCount; channel++)
                    agent.SetCurrentActionSpeed(channel, _factor);
                state.ActionSpeedOwned = true;
                state.Applied = true;
                state.FailureLogged = false;
            }
            catch (Exception ex)
            {
                if (!state.FailureLogged)
                {
                    state.FailureLogged = true;
                    _logger.Debug(
                        "Bend Time could not slow mission agent=" + agent.Index +
                        " safely: " + ex.Message);
                }
            }
        }

        private void Restore(SlowState state)
        {
            if (state == null || (!state.Applied && !state.ActionSpeedOwned))
                return;

            var agent = state.Agent;
            try
            {
                if (agent != null && agent.IsActive())
                {
                    var driven = agent.AgentDrivenProperties;
                    if (driven != null && state.Applied)
                    {
                        var changed = RestoreDrivenValues(state, driven);
                        if (changed)
                            PushDrivenProperties(agent, driven);
                    }

                    if (state.ActionSpeedOwned)
                    {
                        for (var channel = 0; channel < NativeActionChannelCount; channel++)
                            agent.SetCurrentActionSpeed(channel, 1f);
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
                        (agent != null ? agent.Index.ToString() : "none") + ": " + ex.Message);
                }
            }
            finally
            {
                state.Applied = false;
                state.ActionSpeedOwned = false;
            }
        }

        private static void Capture(SlowState state, AgentDrivenProperties driven)
        {
            state.OriginalMaxSpeed = driven.MaxSpeedMultiplier;
            state.OriginalCombatMaxSpeed = driven.CombatMaxSpeedMultiplier;
            state.OriginalTopSpeedReachDuration = driven.TopSpeedReachDuration;
            state.OriginalSwingSpeed = driven.SwingSpeedMultiplier;
            state.OriginalReadySpeed = driven.ThrustOrRangedReadySpeedMultiplier;
            state.OriginalReloadSpeed = driven.ReloadSpeed;
            state.OriginalRangedReadySpeed = driven.BipedalRangedReadySpeedMultiplier;
            state.OriginalRangedReloadSpeed = driven.BipedalRangedReloadSpeedMultiplier;
            state.OriginalHandling = driven.HandlingMultiplier;
            state.OriginalMountSpeed = driven.MountSpeed;
            state.OriginalMountManeuver = driven.MountManeuver;
            state.OriginalMountDashAcceleration = driven.MountDashAccelerationMultiplier;
        }

        private static bool RefreshBaselines(SlowState state, AgentDrivenProperties driven)
        {
            var changed = false;
            changed |= Refresh(ref state.OriginalMaxSpeed, driven.MaxSpeedMultiplier, state.AppliedMaxSpeed);
            changed |= Refresh(ref state.OriginalCombatMaxSpeed, driven.CombatMaxSpeedMultiplier, state.AppliedCombatMaxSpeed);
            changed |= Refresh(ref state.OriginalTopSpeedReachDuration, driven.TopSpeedReachDuration, state.AppliedTopSpeedReachDuration);
            changed |= Refresh(ref state.OriginalSwingSpeed, driven.SwingSpeedMultiplier, state.AppliedSwingSpeed);
            changed |= Refresh(ref state.OriginalReadySpeed, driven.ThrustOrRangedReadySpeedMultiplier, state.AppliedReadySpeed);
            changed |= Refresh(ref state.OriginalReloadSpeed, driven.ReloadSpeed, state.AppliedReloadSpeed);
            changed |= Refresh(ref state.OriginalRangedReadySpeed, driven.BipedalRangedReadySpeedMultiplier, state.AppliedRangedReadySpeed);
            changed |= Refresh(ref state.OriginalRangedReloadSpeed, driven.BipedalRangedReloadSpeedMultiplier, state.AppliedRangedReloadSpeed);
            changed |= Refresh(ref state.OriginalHandling, driven.HandlingMultiplier, state.AppliedHandling);
            changed |= Refresh(ref state.OriginalMountSpeed, driven.MountSpeed, state.AppliedMountSpeed);
            changed |= Refresh(ref state.OriginalMountManeuver, driven.MountManeuver, state.AppliedMountManeuver);
            changed |= Refresh(ref state.OriginalMountDashAcceleration, driven.MountDashAccelerationMultiplier, state.AppliedMountDashAcceleration);
            return changed;
        }

        private void ApplyDrivenValues(SlowState state, AgentDrivenProperties driven)
        {
            var inverse = 1f / Math.Max(MinimumFactor, _factor);
            state.AppliedMaxSpeed = state.OriginalMaxSpeed * _factor;
            state.AppliedCombatMaxSpeed = state.OriginalCombatMaxSpeed * _factor;
            state.AppliedTopSpeedReachDuration = Math.Max(0.01f, state.OriginalTopSpeedReachDuration * inverse);
            state.AppliedSwingSpeed = state.OriginalSwingSpeed * _factor;
            state.AppliedReadySpeed = state.OriginalReadySpeed * _factor;
            state.AppliedReloadSpeed = state.OriginalReloadSpeed * _factor;
            state.AppliedRangedReadySpeed = state.OriginalRangedReadySpeed * _factor;
            state.AppliedRangedReloadSpeed = state.OriginalRangedReloadSpeed * _factor;
            state.AppliedHandling = state.OriginalHandling * _factor;
            state.AppliedMountSpeed = state.OriginalMountSpeed * _factor;
            state.AppliedMountManeuver = state.OriginalMountManeuver * _factor;
            state.AppliedMountDashAcceleration = state.OriginalMountDashAcceleration * _factor;

            driven.MaxSpeedMultiplier = state.AppliedMaxSpeed;
            driven.CombatMaxSpeedMultiplier = state.AppliedCombatMaxSpeed;
            driven.TopSpeedReachDuration = state.AppliedTopSpeedReachDuration;
            driven.SwingSpeedMultiplier = state.AppliedSwingSpeed;
            driven.ThrustOrRangedReadySpeedMultiplier = state.AppliedReadySpeed;
            driven.ReloadSpeed = state.AppliedReloadSpeed;
            driven.BipedalRangedReadySpeedMultiplier = state.AppliedRangedReadySpeed;
            driven.BipedalRangedReloadSpeedMultiplier = state.AppliedRangedReloadSpeed;
            driven.HandlingMultiplier = state.AppliedHandling;
            driven.MountSpeed = state.AppliedMountSpeed;
            driven.MountManeuver = state.AppliedMountManeuver;
            driven.MountDashAccelerationMultiplier = state.AppliedMountDashAcceleration;
        }

        private static bool RestoreDrivenValues(SlowState state, AgentDrivenProperties driven)
        {
            var changed = false;
            changed |= Restore(ref driven.MaxSpeedMultiplier, state.AppliedMaxSpeed, state.OriginalMaxSpeed);
            changed |= Restore(ref driven.CombatMaxSpeedMultiplier, state.AppliedCombatMaxSpeed, state.OriginalCombatMaxSpeed);
            changed |= Restore(ref driven.TopSpeedReachDuration, state.AppliedTopSpeedReachDuration, state.OriginalTopSpeedReachDuration);
            changed |= Restore(ref driven.SwingSpeedMultiplier, state.AppliedSwingSpeed, state.OriginalSwingSpeed);
            changed |= Restore(ref driven.ThrustOrRangedReadySpeedMultiplier, state.AppliedReadySpeed, state.OriginalReadySpeed);
            changed |= Restore(ref driven.ReloadSpeed, state.AppliedReloadSpeed, state.OriginalReloadSpeed);
            changed |= Restore(ref driven.BipedalRangedReadySpeedMultiplier, state.AppliedRangedReadySpeed, state.OriginalRangedReadySpeed);
            changed |= Restore(ref driven.BipedalRangedReloadSpeedMultiplier, state.AppliedRangedReloadSpeed, state.OriginalRangedReloadSpeed);
            changed |= Restore(ref driven.HandlingMultiplier, state.AppliedHandling, state.OriginalHandling);
            changed |= Restore(ref driven.MountSpeed, state.AppliedMountSpeed, state.OriginalMountSpeed);
            changed |= Restore(ref driven.MountManeuver, state.AppliedMountManeuver, state.OriginalMountManeuver);
            changed |= Restore(ref driven.MountDashAccelerationMultiplier, state.AppliedMountDashAcceleration, state.OriginalMountDashAcceleration);
            return changed;
        }

        private void PushDrivenProperties(Agent agent, AgentDrivenProperties driven)
        {
            if (DrivenValuesProperty == null || PushDrivenPropertiesMethod == null)
            {
                if (!_nativePushFailureLogged)
                {
                    _nativePushFailureLogged = true;
                    _logger.Debug("Bend Time native driven-property push API was unavailable.");
                }
                return;
            }

            try
            {
                var values = DrivenValuesProperty.GetValue(driven, null) as float[];
                if (values == null)
                    throw new InvalidOperationException("AgentDrivenProperties.Values was unavailable.");
                PushDrivenPropertiesMethod.Invoke(agent, new object[] { values });
                _nativePushFailureLogged = false;
            }
            catch (Exception ex)
            {
                if (_nativePushFailureLogged)
                    return;
                _nativePushFailureLogged = true;
                _logger.Debug("Bend Time native driven-property push failed safely: " + Unwrap(ex).Message);
            }
        }

        private static Agent GetActiveMount(Agent player)
        {
            var mount = player?.MountAgent;
            return mount != null && mount.IsActive() ? mount : null;
        }

        private static bool Refresh(ref float original, float current, float applied)
        {
            if (Approximately(current, applied))
                return false;
            original = current;
            return true;
        }

        private static bool Restore(ref float current, float applied, float original)
        {
            if (!Approximately(current, applied))
                return false;
            current = original;
            return true;
        }

        private static bool Approximately(float left, float right)
        {
            return Math.Abs(left - right) <=
                   0.001f * Math.Max(1f, Math.Max(Math.Abs(left), Math.Abs(right)));
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
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
