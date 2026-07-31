using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Bend Time leaves the mission scene at native 1.0x. Slowing the whole scene and trying to
    /// multiply the player back up cannot exempt native player simulation. This mission-owned
    /// service instead slows only registered non-player agents and newly launched non-player
    /// missiles. The controlled player and current mount are never mutated.
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
        // Compatibility field for older branch-local Harmony metadata. It mirrors the exempt mount
        // but no player-compensation method is called by this implementation.
        private Agent _mount;
        private bool _active;
        private float _remaining;
        private float _factor = 1f;
        private float _lastApplicationTime;
        private int _refreshCursor;
        private bool _nativePushFailureLogged;

        private sealed class SlowState
        {
            internal Agent Agent;
            internal float[] OriginalValues;
            internal float[] AppliedValues;
            internal bool Applied;
            internal bool ActionSpeedOwned;
            internal bool FailureLogged;
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
                _logger.Debug("Bend Time selective dilation released; all owned agent state restored.");
            }

            _remaining = 0f;
            _factor = 1f;
            _lastApplicationTime = 0f;
            _player = null;
            _mount = null;
            _refreshCursor = 0;
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
                if (state == null || state.Agent == null || IsExempt(state.Agent))
                    Restore(state);
                else
                    Apply(state);
            }
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
                var values = GetValues(driven);
                if (driven != null && values != null)
                {
                    var needsDrivenPush = !state.Applied ||
                        state.AppliedValues == null ||
                        !ArraysApproximatelyEqual(values, state.AppliedValues);
                    if (needsDrivenPush)
                    {
                        if (!state.Applied)
                        {
                            try { agent.UpdateAgentProperties(); }
                            catch { }
                            driven = agent.AgentDrivenProperties;
                            values = GetValues(driven);
                        }

                        if (driven != null && values != null)
                        {
                            state.OriginalValues = (float[])values.Clone();
                            ApplyDrivenScale(driven);
                            var applied = GetValues(driven);
                            state.AppliedValues = applied == null ? null : (float[])applied.Clone();
                            PushDrivenProperties(agent, driven);
                        }
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
                        " safely: " + Unwrap(ex).Message);
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
                    var current = GetValues(driven);
                    if (driven != null && current != null &&
                        state.OriginalValues != null && state.AppliedValues != null)
                    {
                        var length = Math.Min(current.Length,
                            Math.Min(state.OriginalValues.Length, state.AppliedValues.Length));
                        var changed = false;
                        for (var i = 0; i < length; i++)
                        {
                            if (!Approximately(current[i], state.AppliedValues[i]))
                                continue;
                            current[i] = state.OriginalValues[i];
                            changed = true;
                        }
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
                        (agent != null ? agent.Index.ToString() : "none") + ": " +
                        Unwrap(ex).Message);
                }
            }
            finally
            {
                state.Applied = false;
                state.ActionSpeedOwned = false;
                state.OriginalValues = null;
                state.AppliedValues = null;
            }
        }

        private void ApplyDrivenScale(AgentDrivenProperties driven)
        {
            var inverse = 1f / Math.Max(MinimumFactor, _factor);
            driven.MaxSpeedMultiplier *= _factor;
            driven.CombatMaxSpeedMultiplier *= _factor;
            driven.TopSpeedReachDuration = Math.Max(0.01f, driven.TopSpeedReachDuration * inverse);
            driven.SwingSpeedMultiplier *= _factor;
            driven.ThrustOrRangedReadySpeedMultiplier *= _factor;
            driven.ReloadSpeed *= _factor;
            driven.BipedalRangedReadySpeedMultiplier *= _factor;
            driven.BipedalRangedReloadSpeedMultiplier *= _factor;
            driven.HandlingMultiplier *= _factor;
            driven.MountSpeed *= _factor;
            driven.MountManeuver *= _factor;
            driven.MountDashAccelerationMultiplier *= _factor;
        }

        private static float[] GetValues(AgentDrivenProperties driven)
        {
            if (driven == null || DrivenValuesProperty == null)
                return null;
            try { return DrivenValuesProperty.GetValue(driven, null) as float[]; }
            catch { return null; }
        }

        private void PushDrivenProperties(Agent agent, AgentDrivenProperties driven)
        {
            if (PushDrivenPropertiesMethod == null)
            {
                LogNativePushFailure("Bend Time native driven-property push API was unavailable.");
                return;
            }

            try
            {
                var values = GetValues(driven);
                if (values == null)
                    throw new InvalidOperationException("AgentDrivenProperties.Values was unavailable.");
                PushDrivenPropertiesMethod.Invoke(agent, new object[] { values });
                _nativePushFailureLogged = false;
            }
            catch (Exception ex)
            {
                LogNativePushFailure(
                    "Bend Time native driven-property push failed safely: " + Unwrap(ex).Message);
            }
        }

        private void LogNativePushFailure(string message)
        {
            if (_nativePushFailureLogged)
                return;
            _nativePushFailureLogged = true;
            _logger.Debug(message);
        }

        private static Agent GetActiveMount(Agent player)
        {
            var mount = player?.MountAgent;
            return mount != null && mount.IsActive() ? mount : null;
        }

        private static bool ArraysApproximatelyEqual(float[] left, float[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (var i = 0; i < left.Length; i++)
            {
                if (!Approximately(left[i], right[i]))
                    return false;
            }
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

        // Compatibility targets for branch-local patches retained in another source file. The
        // selective implementation never invokes these methods; they prevent Harmony startup from
        // failing while the obsolete patches remain dormant.
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
