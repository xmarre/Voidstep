using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Bannerlord performs agent/property/action updates after mission behaviours have ticked.
    /// Writes made only from TimeControlService.Tick can therefore be accepted in managed code and
    /// still be overwritten before native simulation uses them. This runtime reasserts selective
    /// Bend Time only at those native reset boundaries and only for exact registered non-player
    /// agents owned by the active mission service.
    /// </summary>
    internal static class BendTimeNativeEnforcementRuntime
    {
        private const int NativeActionChannelCount = 2;
        private const float MinimumAbsoluteSpeed = 0.05f;

        private static readonly FieldInfo ActiveField =
            AccessTools.Field(typeof(TimeControlService), "_active");
        private static readonly FieldInfo FactorField =
            AccessTools.Field(typeof(TimeControlService), "_factor");
        private static readonly FieldInfo MissionField =
            AccessTools.Field(typeof(TimeControlService), "_mission");
        private static readonly FieldInfo PlayerField =
            AccessTools.Field(typeof(TimeControlService), "_player");
        private static readonly FieldInfo MountField =
            AccessTools.Field(typeof(TimeControlService), "_mount");
        private static readonly FieldInfo StatesField =
            AccessTools.Field(typeof(TimeControlService), "_states");
        private static readonly FieldInfo LoggerField =
            AccessTools.Field(typeof(TimeControlService), "_logger");
        private static readonly Type SlowStateType =
            typeof(TimeControlService).GetNestedType("SlowState", BindingFlags.NonPublic);
        private static readonly FieldInfo SlowStateAgentField =
            SlowStateType == null ? null : AccessTools.Field(SlowStateType, "Agent");

        private static readonly ConditionalWeakTable<TimeControlService, RuntimeState> RuntimeStates =
            new ConditionalWeakTable<TimeControlService, RuntimeState>();

        private static WeakReference<TimeControlService> _activeService;
        private static WeakReference<Mission> _activeMission;

        [ThreadStatic]
        private static int _bypassDepth;

        internal sealed class RuntimeState
        {
            internal readonly Dictionary<int, float> BaselineMaximumSpeeds =
                new Dictionary<int, float>();
            internal readonly Dictionary<int, float> OriginalMaximumSpeedLimits =
                new Dictionary<int, float>();
            internal bool ArmedLogWritten;
            internal bool FailureLogged;

            public RuntimeState()
            {
            }
        }

        internal static bool IsBypassed => _bypassDepth > 0;

        internal static BypassScope EnterBypass()
        {
            _bypassDepth++;
            return new BypassScope();
        }

        internal static void ExitOneBypassLevel()
        {
            if (_bypassDepth > 0)
                _bypassDepth--;
        }

        internal static void Track(TimeControlService service)
        {
            if (service == null || !ReadBoolean(ActiveField, service))
                return;

            var mission = MissionField?.GetValue(service) as Mission;
            if (mission == null || !ReferenceEquals(Mission.Current, mission))
                return;

            _activeService = new WeakReference<TimeControlService>(service);
            _activeMission = new WeakReference<Mission>(mission);
            var state = RuntimeStates.GetOrCreateValue(service);
            state.BaselineMaximumSpeeds.Clear();
            state.OriginalMaximumSpeedLimits.Clear();
            state.FailureLogged = false;

            var enforced = RefreshAllEligible(service, state);
            if (!state.ArmedLogWritten)
            {
                state.ArmedLogWritten = true;
                GetLogger(service)?.Debug(
                    "Bend Time native enforcement armed factor=" +
                    ReadFactor(service).ToString("0.00") +
                    ", enforcedAgents=" + enforced +
                    "; absolute movement caps, driven-property refresh and action-speed guards are active.");
            }
        }

        internal static void RestoreAndUntrack(TimeControlService service)
        {
            if (service != null && RuntimeStates.TryGetValue(service, out var state))
            {
                RestoreOriginalMaximumSpeedLimits(service, state);
                state.BaselineMaximumSpeeds.Clear();
                state.OriginalMaximumSpeedLimits.Clear();
                state.FailureLogged = false;
                state.ArmedLogWritten = false;
                RuntimeStates.Remove(service);
            }

            TimeControlService active;
            if (_activeService != null && _activeService.TryGetTarget(out active) &&
                ReferenceEquals(active, service))
            {
                _activeService = null;
                _activeMission = null;
            }
        }

        internal static bool TryGetEligible(
            Agent agent,
            out TimeControlService service,
            out float factor,
            out RuntimeState state)
        {
            service = null;
            state = null;
            factor = 1f;
            if (IsBypassed || agent == null || !agent.IsActive() || agent.Index < 0)
                return false;

            Mission mission;
            if (_activeService == null || !_activeService.TryGetTarget(out service) ||
                _activeMission == null || !_activeMission.TryGetTarget(out mission) ||
                mission == null || !ReferenceEquals(Mission.Current, mission) ||
                !ReferenceEquals(MissionField?.GetValue(service), mission) ||
                !ReadBoolean(ActiveField, service))
            {
                return false;
            }

            var player = PlayerField?.GetValue(service) as Agent;
            var mount = MountField?.GetValue(service) as Agent;
            if (ReferenceEquals(agent, player) || ReferenceEquals(agent, mount))
                return false;

            var states = StatesField?.GetValue(service) as IDictionary;
            if (states == null || !states.Contains(agent.Index))
                return false;

            var slowState = states[agent.Index];
            if (slowState == null || SlowStateAgentField == null ||
                !ReferenceEquals(SlowStateAgentField.GetValue(slowState), agent))
            {
                return false;
            }

            factor = ReadFactor(service);
            if (factor <= 0f || factor >= 0.999f)
                return false;

            state = RuntimeStates.GetOrCreateValue(service);
            return true;
        }

        internal static void CaptureBeforeServiceApply(TimeControlService service, object slowState)
        {
            if (service == null || slowState == null || SlowStateAgentField == null)
                return;

            var agent = SlowStateAgentField.GetValue(slowState) as Agent;
            TimeControlService resolved;
            RuntimeState state;
            float factor;
            if (!TryGetEligible(agent, out resolved, out factor, out state) ||
                !ReferenceEquals(resolved, service))
            {
                return;
            }

            CaptureOriginalMaximumSpeedLimit(agent, state);
        }

        internal static bool RefreshAndEnforce(Agent agent)
        {
            TimeControlService service;
            RuntimeState state;
            float factor;
            if (!TryGetEligible(agent, out service, out factor, out state))
                return false;

            try
            {
                using (EnterBypass())
                {
                    CaptureOriginalMaximumSpeedLimit(agent, state);
                    agent.UpdateAgentProperties();
                    EnforceCurrentBaselineCore(agent, factor, state);
                }
                state.FailureLogged = false;
                return true;
            }
            catch (Exception ex)
            {
                LogFailureOnce(service, state, agent, ex);
                return false;
            }
        }

        internal static void EnforceAfterPropertyUpdate(Agent agent)
        {
            TimeControlService service;
            RuntimeState state;
            float factor;
            if (!TryGetEligible(agent, out service, out factor, out state))
                return;

            try
            {
                using (EnterBypass())
                {
                    CaptureOriginalMaximumSpeedLimit(agent, state);
                    EnforceCurrentBaselineCore(agent, factor, state);
                }
                state.FailureLogged = false;
            }
            catch (Exception ex)
            {
                LogFailureOnce(service, state, agent, ex);
            }
        }

        internal static void EnforceAfterServiceApply(TimeControlService service, object slowState)
        {
            if (service == null || slowState == null || SlowStateAgentField == null)
                return;

            var agent = SlowStateAgentField.GetValue(slowState) as Agent;
            TimeControlService resolved;
            RuntimeState state;
            float factor;
            if (!TryGetEligible(agent, out resolved, out factor, out state) ||
                !ReferenceEquals(resolved, service))
            {
                return;
            }

            RefreshAndEnforce(agent);
        }

        internal static void ReassertMaximumSpeed(Agent agent)
        {
            TimeControlService service;
            RuntimeState state;
            float factor;
            if (!TryGetEligible(agent, out service, out factor, out state))
                return;

            try
            {
                using (EnterBypass())
                    ApplyAbsoluteMovementCap(agent, factor, state);
                state.FailureLogged = false;
            }
            catch (Exception ex)
            {
                LogFailureOnce(service, state, agent, ex);
            }
        }

        internal static void ScaleActionSpeed(Agent agent, int channel, ref float speed)
        {
            if (channel < 0 || channel >= NativeActionChannelCount || speed <= 0f)
                return;

            TimeControlService service;
            RuntimeState state;
            float factor;
            if (!TryGetEligible(agent, out service, out factor, out state))
                return;
            speed = Math.Max(0.001f, speed * factor);
        }

        private static int RefreshAllEligible(TimeControlService service, RuntimeState state)
        {
            var states = StatesField?.GetValue(service) as IDictionary;
            if (states == null)
                return 0;

            var snapshot = new List<Agent>(states.Count);
            foreach (DictionaryEntry entry in states)
            {
                var slowState = entry.Value;
                var agent = slowState == null || SlowStateAgentField == null
                    ? null
                    : SlowStateAgentField.GetValue(slowState) as Agent;
                if (agent != null)
                    snapshot.Add(agent);
            }

            var enforced = 0;
            for (var i = 0; i < snapshot.Count; i++)
            {
                if (RefreshAndEnforce(snapshot[i]))
                    enforced++;
            }
            return enforced;
        }

        private static void EnforceCurrentBaselineCore(
            Agent agent,
            float factor,
            RuntimeState state)
        {
            CaptureBaselineMaximumSpeed(agent, state);
            ScaleDrivenProperties(agent.AgentDrivenProperties, factor);
            agent.UpdateCustomDrivenProperties();
            ApplyAbsoluteMovementCap(agent, factor, state);
            ApplyCurrentActionSpeeds(agent, factor);
        }

        private static void CaptureOriginalMaximumSpeedLimit(Agent agent, RuntimeState state)
        {
            if (agent == null || state == null ||
                state.OriginalMaximumSpeedLimits.ContainsKey(agent.Index))
            {
                return;
            }

            state.OriginalMaximumSpeedLimits[agent.Index] = agent.GetMaximumSpeedLimit();
        }

        private static void CaptureBaselineMaximumSpeed(Agent agent, RuntimeState state)
        {
            if (agent == null || state == null)
                return;

            var baseline = agent.MaximumForwardUnlimitedSpeed;
            if (float.IsNaN(baseline) || float.IsInfinity(baseline) || baseline <= 0.001f)
                baseline = agent.GetCurrentSpeedLimit();
            if (float.IsNaN(baseline) || float.IsInfinity(baseline) || baseline <= 0.001f)
                baseline = 0.5f;
            state.BaselineMaximumSpeeds[agent.Index] = baseline;
        }

        private static void ApplyAbsoluteMovementCap(
            Agent agent,
            float factor,
            RuntimeState state)
        {
            float baseline;
            if (!state.BaselineMaximumSpeeds.TryGetValue(agent.Index, out baseline) ||
                baseline <= 0.001f)
            {
                CaptureBaselineMaximumSpeed(agent, state);
                if (!state.BaselineMaximumSpeeds.TryGetValue(agent.Index, out baseline))
                    baseline = 0.5f;
            }

            agent.SetMaximumSpeedLimit(
                Math.Max(MinimumAbsoluteSpeed, baseline * factor),
                false);
        }

        private static void ApplyCurrentActionSpeeds(Agent agent, float factor)
        {
            for (var channel = 0; channel < NativeActionChannelCount; channel++)
                agent.SetCurrentActionSpeed(channel, factor);
        }

        private static void RestoreOriginalMaximumSpeedLimits(
            TimeControlService service,
            RuntimeState state)
        {
            var states = StatesField?.GetValue(service) as IDictionary;
            if (states == null)
                return;

            using (EnterBypass())
            {
                foreach (DictionaryEntry entry in states)
                {
                    var slowState = entry.Value;
                    var agent = slowState == null || SlowStateAgentField == null
                        ? null
                        : SlowStateAgentField.GetValue(slowState) as Agent;
                    if (agent == null || !agent.IsActive())
                        continue;

                    float original;
                    if (state.OriginalMaximumSpeedLimits.TryGetValue(agent.Index, out original))
                        agent.SetMaximumSpeedLimit(original, false);
                }
            }
        }

        private static void ScaleDrivenProperties(AgentDrivenProperties driven, float factor)
        {
            if (driven == null)
                return;

            var inverse = 1f / Math.Max(0.02f, factor);
            driven.MaxSpeedMultiplier *= factor;
            driven.CombatMaxSpeedMultiplier *= factor;
            driven.TopSpeedReachDuration = Math.Max(0.01f, driven.TopSpeedReachDuration * inverse);
            driven.SwingSpeedMultiplier *= factor;
            driven.ThrustOrRangedReadySpeedMultiplier *= factor;
            driven.ReloadSpeed *= factor;
            driven.BipedalRangedReadySpeedMultiplier *= factor;
            driven.BipedalRangedReloadSpeedMultiplier *= factor;
            driven.HandlingMultiplier *= factor;
            driven.MountSpeed *= factor;
            driven.MountManeuver *= factor;
            driven.MountDashAccelerationMultiplier *= factor;
        }

        private static float ReadFactor(TimeControlService service)
        {
            try { return FactorField == null ? 1f : (float)FactorField.GetValue(service); }
            catch { return 1f; }
        }

        private static bool ReadBoolean(FieldInfo field, object instance)
        {
            try { return field != null && (bool)field.GetValue(instance); }
            catch { return false; }
        }

        private static VoidstepLogger GetLogger(TimeControlService service)
        {
            try { return LoggerField?.GetValue(service) as VoidstepLogger; }
            catch { return null; }
        }

        private static void LogFailureOnce(
            TimeControlService service,
            RuntimeState state,
            Agent agent,
            Exception exception)
        {
            if (state == null || state.FailureLogged)
                return;
            state.FailureLogged = true;
            GetLogger(service)?.Debug(
                "Bend Time native enforcement failed safely for agent=" +
                (agent == null ? "none" : agent.Index.ToString()) + ": " +
                Unwrap(exception).Message);
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }

        internal readonly struct BypassScope : IDisposable
        {
            public void Dispose()
            {
                ExitOneBypassLevel();
            }
        }
    }

    [HarmonyPatch(typeof(TimeControlService), nameof(TimeControlService.Begin))]
    internal static class BendTimeNativeEnforcementBeginPatch
    {
        private static void Postfix(TimeControlService __instance, bool __result)
        {
            if (__result)
                BendTimeNativeEnforcementRuntime.Track(__instance);
        }
    }

    [HarmonyPatch(typeof(TimeControlService), nameof(TimeControlService.Release))]
    internal static class BendTimeNativeEnforcementReleasePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(TimeControlService __instance)
        {
            BendTimeNativeEnforcementRuntime.RestoreAndUntrack(__instance);
        }
    }

    [HarmonyPatch]
    internal static class BendTimeServiceApplyBoundaryPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(TimeControlService), "Apply");
        }

        private static void Prefix(TimeControlService __instance, object __0)
        {
            BendTimeNativeEnforcementRuntime.CaptureBeforeServiceApply(__instance, __0);
            BendTimeNativeEnforcementRuntime.EnterBypass();
        }

        private static Exception Finalizer(
            TimeControlService __instance,
            object __0,
            Exception __exception)
        {
            BendTimeNativeEnforcementRuntime.ExitOneBypassLevel();
            if (__exception == null)
                BendTimeNativeEnforcementRuntime.EnforceAfterServiceApply(__instance, __0);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.UpdateAgentProperties))]
    internal static class BendTimePostAgentPropertyUpdatePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Agent __instance)
        {
            BendTimeNativeEnforcementRuntime.EnforceAfterPropertyUpdate(__instance);
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.SetMaximumSpeedLimit))]
    internal static class BendTimeMaximumSpeedResetGuardPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Agent __instance)
        {
            BendTimeNativeEnforcementRuntime.ReassertMaximumSpeed(__instance);
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.SetCurrentActionSpeed))]
    internal static class BendTimeCurrentActionSpeedGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Agent __instance, int __0, ref float __1)
        {
            BendTimeNativeEnforcementRuntime.ScaleActionSpeed(__instance, __0, ref __1);
        }
    }

    [HarmonyPatch]
    internal static class BendTimeNewActionSpeedGuardPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var methods = typeof(Agent).GetMethods(BindingFlags.Instance | BindingFlags.Public);
            for (var i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                if (!string.Equals(method.Name, nameof(Agent.SetActionChannel), StringComparison.Ordinal))
                    continue;
                var parameters = method.GetParameters();
                if (parameters.Length > 5 && parameters[0].ParameterType == typeof(int) &&
                    parameters[5].ParameterType == typeof(float))
                    yield return method;
            }
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(Agent __instance, int __0, ref float __5)
        {
            BendTimeNativeEnforcementRuntime.ScaleActionSpeed(__instance, __0, ref __5);
        }
    }
}
