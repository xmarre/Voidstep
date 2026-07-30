using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Applies stable, distinct TOR sprite names to the six late-created Voidstep
    /// proxies, then rebuilds the repaired radial view from those templates.
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
        private static WeakReference<Mission> _lastLoggedMission;

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

                    EnsureApi(manager.GetType().Assembly);
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

                    if (IsNewMission(mission))
                    {
                        _lastLoggedMission = new WeakReference<Mission>(mission);
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

        private static bool IsNewMission(Mission mission)
        {
            Mission previous;
            return _lastLoggedMission == null ||
                   !_lastLoggedMission.TryGetTarget(out previous) ||
                   !ReferenceEquals(previous, mission);
        }

        private static void EnsureApi(Assembly torAssembly)
        {
            if (_apiResolved)
                return;

            var managerType = torAssembly.GetType(
                "TOR_Core.AbilitySystem.AbilityManagerMissionLogic",
                true,
                false);
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
}
