using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// TOR copies the agent's known abilities into its radial-menu view model only when
    /// AbilityHUDMissionView.CheckMainAgent runs. Voidstep proxies are injected later,
    /// so the already-created menu must be rebuilt immediately before it is opened.
    /// </summary>
    internal static class TorRadialMenuRefresh
    {
        private const string HarmonyId = "xmarre.voidstep";
        private static readonly object Sync = new object();
        private static readonly VoidstepLogger Logger = new VoidstepLogger();

        private static Type _abilityComponentType;
        private static Type _abilityHudMissionViewType;
        private static Type _abilityType;
        private static MethodInfo _agentGetComponent;
        private static MethodInfo _missionGetBehavior;
        private static MethodInfo _checkMainAgent;
        private static PropertyInfo _knownAbilitiesProperty;
        private static PropertyInfo _stringIdProperty;
        private static Mission _lastMission;
        private static bool _loggedVisibleRefresh;
        private static bool _loggedMissingProxies;

        internal static void Install()
        {
            lock (Sync)
            {
                try
                {
                    var torAssembly = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(assembly => string.Equals(
                            assembly.GetName().Name,
                            "TOR_Core",
                            StringComparison.OrdinalIgnoreCase));
                    if (torAssembly == null)
                        return;

                    var managerType = torAssembly.GetType(
                        "TOR_Core.AbilitySystem.AbilityManagerMissionLogic",
                        false,
                        false);
                    var openMethod = managerType?.GetMethod(
                        "EnableQuickSelectionMenuMode",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null);
                    if (openMethod == null)
                    {
                        Logger.Debug("TOR radial refresh was not installed because EnableQuickSelectionMenuMode was not found.");
                        return;
                    }

                    var patchInfo = Harmony.GetPatchInfo(openMethod);
                    if (patchInfo != null && patchInfo.Prefixes.Any(patch =>
                            string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal)))
                    {
                        return;
                    }

                    ResolveApi(torAssembly);
                    var prefix = typeof(TorRadialMenuRefresh).GetMethod(
                        nameof(RefreshBeforeOpen),
                        BindingFlags.Static | BindingFlags.NonPublic);
                    if (prefix == null)
                        throw new MissingMethodException(typeof(TorRadialMenuRefresh).FullName, nameof(RefreshBeforeOpen));

                    new Harmony(HarmonyId).Patch(openMethod, prefix: new HarmonyMethod(prefix));
                    Logger.Info("Installed TOR Q-wheel live refresh for late-injected Voidstep abilities.");
                }
                catch (Exception ex)
                {
                    Logger.Error("TOR Q-wheel live refresh could not be installed.", Unwrap(ex));
                }
            }
        }

        private static void ResolveApi(Assembly torAssembly)
        {
            _abilityComponentType = torAssembly.GetType(
                "TOR_Core.AbilitySystem.AbilityComponent",
                true,
                false);
            _abilityHudMissionViewType = torAssembly.GetType(
                "TOR_Core.AbilitySystem.AbilityHUDMissionView",
                true,
                false);
            _abilityType = torAssembly.GetType(
                "TOR_Core.AbilitySystem.Ability",
                true,
                false);

            _agentGetComponent = typeof(Agent).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(method => method.Name == "GetComponent" &&
                                  method.IsGenericMethodDefinition &&
                                  method.GetParameters().Length == 0);
            _missionGetBehavior = typeof(Mission).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(method => method.Name == "GetMissionBehavior" &&
                                  method.IsGenericMethodDefinition &&
                                  method.GetParameters().Length == 0);
            _checkMainAgent = _abilityHudMissionViewType.GetMethod(
                "CheckMainAgent",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Agent) },
                null);
            _knownAbilitiesProperty = _abilityComponentType.GetProperty(
                "KnownAbilitySystem",
                BindingFlags.Instance | BindingFlags.Public);
            _stringIdProperty = _abilityType.GetProperty(
                "StringID",
                BindingFlags.Instance | BindingFlags.Public);

            if (_checkMainAgent == null || _knownAbilitiesProperty == null || _stringIdProperty == null)
                throw new MissingMemberException("TOR radial-menu refresh API surface is incomplete.");
        }

        private static void RefreshBeforeOpen()
        {
            lock (Sync)
            {
                try
                {
                    var mission = Mission.Current;
                    var agent = mission?.MainAgent;
                    if (mission == null || agent == null || !agent.IsActive())
                        return;

                    if (!ReferenceEquals(_lastMission, mission))
                    {
                        _lastMission = mission;
                        _loggedVisibleRefresh = false;
                        _loggedMissingProxies = false;
                    }

                    if (_abilityComponentType == null || _abilityHudMissionViewType == null)
                        return;

                    var component = _agentGetComponent
                        .MakeGenericMethod(_abilityComponentType)
                        .Invoke(agent, null);
                    var knownAbilities = component == null
                        ? null
                        : _knownAbilitiesProperty.GetValue(component, null) as IList;
                    if (knownAbilities == null)
                        return;

                    var voidstepCount = CountVoidstepAbilities(knownAbilities);
                    if (voidstepCount == 0)
                    {
                        if (!_loggedMissingProxies)
                        {
                            _loggedMissingProxies = true;
                            Logger.Info(
                                "TOR Q wheel opened before any Voidstep proxies were present; " +
                                "known ability count=" + knownAbilities.Count + ".");
                        }
                        return;
                    }

                    var view = _missionGetBehavior
                        .MakeGenericMethod(_abilityHudMissionViewType)
                        .Invoke(mission, null);
                    if (view == null)
                    {
                        Logger.Debug("TOR AbilityHUDMissionView is not ready; radial refresh will retry on the next Q open.");
                        return;
                    }

                    _checkMainAgent.Invoke(view, new object[] { agent });
                    if (!_loggedVisibleRefresh)
                    {
                        _loggedVisibleRefresh = true;
                        Logger.Info(
                            "Refreshed TOR Q wheel from the live ability list: " +
                            "known=" + knownAbilities.Count + ", Voidstep=" + voidstepCount + ".");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug("TOR Q-wheel live refresh failed safely: " + Unwrap(ex).Message);
                }
            }
        }

        private static int CountVoidstepAbilities(IList abilities)
        {
            var count = 0;
            for (var i = 0; i < abilities.Count; i++)
            {
                var ability = abilities[i];
                if (ability == null || !_abilityType.IsInstanceOfType(ability))
                    continue;
                var stringId = _stringIdProperty.GetValue(ability, null) as string;
                if (stringId != null && stringId.StartsWith("voidstep_", StringComparison.Ordinal))
                    count++;
            }
            return count;
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }
    }

    [HarmonyPatch(typeof(VoidstepMissionBehavior), nameof(VoidstepMissionBehavior.EarlyStart))]
    internal static class TorRadialMenuRefreshInstallerPatch
    {
        private static void Postfix()
        {
            TorRadialMenuRefresh.Install();
        }
    }
}
