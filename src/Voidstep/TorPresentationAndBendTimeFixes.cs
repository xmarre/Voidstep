using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    /// <summary>
    /// Applies stable, distinct TOR sprite names to the six late-created Voidstep
    /// proxies, then rebuilds the already repaired radial view from those templates.
    /// </summary>
    internal static class TorVoidstepPresentation
    {
        private static readonly IReadOnlyDictionary<string, string> SpriteByStringId =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "voidstep_voidstepcleave", "runemagic_symbol" },
                { "voidstep_blink", "highmagic_symbol" },
                { "voidstep_windblast", "lightmagic_symbol" },
                { "voidstep_bendtime", "lifemagic_symbol" },
                { "voidstep_domino", "darkmagic_symbol" },
                { "voidstep_darkvision", "deathmagic_symbol" }
            };

        private static readonly object Sync = new object();
        private static readonly VoidstepLogger Logger = new VoidstepLogger();
        private static bool _apiResolved;
        private static Type _abilityComponentType;
        private static Type _abilityType;
        private static MethodInfo _agentGetComponent;
        private static PropertyInfo _knownAbilitiesProperty;
        private static PropertyInfo _stringIdProperty;
        private static PropertyInfo _templateProperty;
        private static PropertyInfo _spriteNameProperty;
        private static FieldInfo _managerAbilityViewField;
        private static FieldInfo _viewRadialVmField;
        private static MethodInfo _fillAbilities;
        private static Mission _lastLoggedMission;

        internal static void ApplyAndRefill(object manager)
        {
            if (manager == null)
                return;

            lock (Sync)
            {
                try
                {
                    var mission = Mission.Current;
                    var agent = mission?.MainAgent;
                    if (mission == null || agent == null || !agent.IsActive())
                        return;

                    EnsureApi(manager.GetType());
                    var component = _agentGetComponent
                        .MakeGenericMethod(_abilityComponentType)
                        .Invoke(agent, null);
                    var knownAbilities = component == null
                        ? null
                        : _knownAbilitiesProperty.GetValue(component, null) as IList;
                    if (knownAbilities == null)
                        return;

                    var changed = 0;
                    for (var i = 0; i < knownAbilities.Count; i++)
                    {
                        var ability = knownAbilities[i];
                        if (ability == null || !_abilityType.IsInstanceOfType(ability))
                            continue;

                        var stringId = _stringIdProperty.GetValue(ability, null) as string;
                        if (stringId == null || !SpriteByStringId.TryGetValue(stringId, out var sprite))
                            continue;

                        var template = _templateProperty.GetValue(ability, null);
                        if (template == null)
                            continue;

                        var current = _spriteNameProperty.GetValue(template, null) as string;
                        if (!string.Equals(current, sprite, StringComparison.Ordinal))
                        {
                            _spriteNameProperty.SetValue(template, sprite, null);
                            changed++;
                        }
                    }

                    var abilityView = _managerAbilityViewField.GetValue(manager);
                    var radialVm = abilityView == null ? null : _viewRadialVmField.GetValue(abilityView);
                    if (radialVm != null)
                        _fillAbilities.Invoke(radialVm, new object[] { agent });

                    if (!ReferenceEquals(_lastLoggedMission, mission))
                    {
                        _lastLoggedMission = mission;
                        Logger.Info("Applied six distinct TOR icons to the Voidstep Q-wheel entries and rebuilt the radial view.");
                    }
                    else if (changed > 0)
                    {
                        Logger.Debug("Restored distinct TOR icons on " + changed + " rebuilt Voidstep proxy template(s).");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug("TOR Voidstep icon refresh failed safely: " + Unwrap(ex).Message);
                }
            }
        }

        private static void EnsureApi(Type managerType)
        {
            if (_apiResolved)
                return;

            var torAssembly = managerType.Assembly;
            _abilityComponentType = torAssembly.GetType(
                "TOR_Core.AbilitySystem.AbilityComponent",
                true,
                false);
            _abilityType = torAssembly.GetType(
                "TOR_Core.AbilitySystem.Ability",
                true,
                false);
            var abilityTemplateType = torAssembly.GetType(
                "TOR_Core.AbilitySystem.AbilityTemplate",
                true,
                false);
            var abilityHudMissionViewType = torAssembly.GetType(
                "TOR_Core.AbilitySystem.AbilityHUDMissionView",
                true,
                false);
            var radialVmType = torAssembly.GetType(
                "TOR_Core.AbilitySystem.AbilityRadialSelection_VM",
                true,
                false);

            _agentGetComponent = typeof(Agent).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(method => method.Name == "GetComponent" &&
                                  method.IsGenericMethodDefinition &&
                                  method.GetParameters().Length == 0);
            _knownAbilitiesProperty = _abilityComponentType.GetProperty(
                "KnownAbilitySystem",
                BindingFlags.Instance | BindingFlags.Public);
            _stringIdProperty = _abilityType.GetProperty(
                "StringID",
                BindingFlags.Instance | BindingFlags.Public);
            _templateProperty = _abilityType.GetProperty(
                "Template",
                BindingFlags.Instance | BindingFlags.Public);
            _spriteNameProperty = abilityTemplateType.GetProperty(
                "SpriteName",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _managerAbilityViewField = managerType.GetField(
                "_abilityView",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _viewRadialVmField = abilityHudMissionViewType.GetField(
                "_abilityRadialSelection_VM",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _fillAbilities = radialVmType.GetMethod(
                "FillAbilities",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Agent) },
                null);

            if (_knownAbilitiesProperty == null || _stringIdProperty == null ||
                _templateProperty == null || _spriteNameProperty == null ||
                !_spriteNameProperty.CanWrite || _managerAbilityViewField == null ||
                _viewRadialVmField == null || _fillAbilities == null)
            {
                throw new MissingMemberException("TOR Voidstep presentation API surface is incomplete.");
            }

            _apiResolved = true;
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }
    }

    [HarmonyPatch(typeof(TorRadialMenuRefresh), "RepairBeforeOpen")]
    internal static class TorVoidstepPresentationRefreshPatch
    {
        private static void Postfix(object __0)
        {
            TorVoidstepPresentation.ApplyAndRefill(__0);
        }
    }

    /// <summary>
    /// TOR's targeting state owns the two-handed spell stance. Cleave must retain its
    /// melee weapon after selection, so hand the selection to Voidstep and immediately
    /// release only TOR's stance while keeping Voidstep's preview active.
    /// </summary>
    [HarmonyPatch(typeof(TorAbilityWheelAdapter), nameof(TorAbilityWheelAdapter.Tick))]
    internal static class TorCleaveStanceReleasePatch
    {
        private static readonly AccessTools.FieldRef<TorAbilityWheelAdapter, AbilitySelectionController> Selection =
            AccessTools.FieldRefAccess<TorAbilityWheelAdapter, AbilitySelectionController>("_selection");

        private static void Postfix(TorAbilityWheelAdapter __instance)
        {
            if (__instance == null || !__instance.OwnsTargeting)
                return;

            var selection = Selection(__instance);
            if (selection == null || !selection.SelectedAbility.HasValue ||
                selection.SelectedAbility.Value != AbilityId.VoidstepCleave)
            {
                return;
            }

            __instance.CloseTargetingMode();
        }
    }

    /// <summary>
    /// AgentDrivenProperties alone cannot exceed Bannerlord's native per-agent maximum
    /// speed cap. Bend Time therefore also owns that cap for the player and current mount.
    /// Existing limits are refreshed when the engine or another mod changes them and are
    /// restored only while the live value still equals Voidstep's applied value.
    /// </summary>
    internal static class BendTimeMaximumSpeedOwnership
    {
        private sealed class State
        {
            internal Agent Player;
            internal Agent Mount;
            internal float Factor = 1f;
            internal float OriginalPlayerLimit;
            internal float AppliedPlayerLimit;
            internal bool PlayerApplied;
            internal float OriginalMountLimit;
            internal float AppliedMountLimit;
            internal bool MountApplied;
            internal bool FailureLogged;
        }

        private static readonly ConditionalWeakTable<TimeControlService, State> States =
            new ConditionalWeakTable<TimeControlService, State>();
        private static readonly VoidstepLogger Logger = new VoidstepLogger();

        internal static void Begin(TimeControlService service, Agent player, float requestedFactor, bool succeeded)
        {
            if (service == null)
                return;

            if (!succeeded)
            {
                Restore(service, false);
                return;
            }

            var state = States.GetOrCreateValue(service);
            RestoreState(state);
            state.Player = player;
            state.Factor = Math.Max(0.001f, Math.Min(1f, requestedFactor));
            state.FailureLogged = false;
            CapturePlayer(state);
        }

        internal static void Tick(TimeControlService service)
        {
            if (service == null || !States.TryGetValue(service, out var state))
                return;

            if (!service.Active || !VoidstepSettings.Current.PreservePlayerSpeed ||
                state.Player == null || !state.Player.IsActive())
            {
                RestoreState(state);
                return;
            }

            try
            {
                var compensation = Math.Min(8f, 1f / Math.Max(0.001f, state.Factor));
                ApplyLimit(state.Player, compensation,
                    ref state.OriginalPlayerLimit,
                    ref state.AppliedPlayerLimit,
                    ref state.PlayerApplied);

                var currentMount = state.Player.MountAgent;
                if (currentMount != null && !currentMount.IsActive())
                    currentMount = null;
                if (!ReferenceEquals(currentMount, state.Mount))
                {
                    RestoreMount(state);
                    state.Mount = currentMount;
                    CaptureMount(state);
                }

                if (state.Mount != null)
                {
                    ApplyLimit(state.Mount, compensation,
                        ref state.OriginalMountLimit,
                        ref state.AppliedMountLimit,
                        ref state.MountApplied);
                }
            }
            catch (Exception ex)
            {
                if (!state.FailureLogged)
                {
                    state.FailureLogged = true;
                    Logger.Debug("Bend Time maximum-speed ownership failed safely: " + ex.Message);
                }
            }
        }

        internal static void Restore(TimeControlService service, bool remove)
        {
            if (service == null || !States.TryGetValue(service, out var state))
                return;

            RestoreState(state);
            if (remove)
                States.Remove(service);
        }

        private static void CapturePlayer(State state)
        {
            state.PlayerApplied = false;
            if (state.Player == null || !state.Player.IsActive())
                return;
            state.OriginalPlayerLimit = state.Player.GetMaximumSpeedLimit();
        }

        private static void CaptureMount(State state)
        {
            state.MountApplied = false;
            if (state.Mount == null || !state.Mount.IsActive())
                return;
            state.OriginalMountLimit = state.Mount.GetMaximumSpeedLimit();
        }

        private static void ApplyLimit(
            Agent agent,
            float compensation,
            ref float original,
            ref float applied,
            ref bool ownsApplied)
        {
            if (agent == null || !agent.IsActive())
                return;

            var current = agent.GetMaximumSpeedLimit();
            if (ownsApplied && Approximately(current, applied))
                return;

            if (ownsApplied)
                original = current;
            else if (float.IsNaN(original) || float.IsInfinity(original))
                original = current;

            agent.SetMaximumSpeedLimit(compensation, true);
            applied = agent.GetMaximumSpeedLimit();
            ownsApplied = true;
        }

        private static void RestoreState(State state)
        {
            RestorePlayer(state);
            RestoreMount(state);
            state.Player = null;
            state.Mount = null;
            state.Factor = 1f;
        }

        private static void RestorePlayer(State state)
        {
            if (!state.PlayerApplied)
                return;

            try
            {
                if (state.Player != null && state.Player.IsActive() &&
                    Approximately(state.Player.GetMaximumSpeedLimit(), state.AppliedPlayerLimit))
                {
                    state.Player.SetMaximumSpeedLimit(state.OriginalPlayerLimit, false);
                }
            }
            finally
            {
                state.PlayerApplied = false;
            }
        }

        private static void RestoreMount(State state)
        {
            if (!state.MountApplied)
                return;

            try
            {
                if (state.Mount != null && state.Mount.IsActive() &&
                    Approximately(state.Mount.GetMaximumSpeedLimit(), state.AppliedMountLimit))
                {
                    state.Mount.SetMaximumSpeedLimit(state.OriginalMountLimit, false);
                }
            }
            finally
            {
                state.MountApplied = false;
            }
        }

        private static bool Approximately(float left, float right)
        {
            return Math.Abs(left - right) <=
                   0.001f * Math.Max(1f, Math.Max(Math.Abs(left), Math.Abs(right)));
        }
    }

    [HarmonyPatch(typeof(TimeControlService), nameof(TimeControlService.Begin))]
    internal static class BendTimeMaximumSpeedBeginPatch
    {
        private static void Postfix(
            TimeControlService __instance,
            Agent player,
            float requestedFactor,
            bool __result)
        {
            BendTimeMaximumSpeedOwnership.Begin(__instance, player, requestedFactor, __result);
        }
    }

    [HarmonyPatch(typeof(TimeControlService), nameof(TimeControlService.Tick))]
    internal static class BendTimeMaximumSpeedTickPatch
    {
        private static void Postfix(TimeControlService __instance)
        {
            BendTimeMaximumSpeedOwnership.Tick(__instance);
        }
    }

    [HarmonyPatch(typeof(TimeControlService), nameof(TimeControlService.Release))]
    internal static class BendTimeMaximumSpeedReleasePatch
    {
        private static void Prefix(TimeControlService __instance)
        {
            BendTimeMaximumSpeedOwnership.Restore(__instance, false);
        }
    }

    [HarmonyPatch(typeof(TimeControlService), nameof(TimeControlService.Cleanup))]
    internal static class BendTimeMaximumSpeedCleanupPatch
    {
        private static void Prefix(TimeControlService __instance)
        {
            BendTimeMaximumSpeedOwnership.Restore(__instance, true);
        }
    }
}
