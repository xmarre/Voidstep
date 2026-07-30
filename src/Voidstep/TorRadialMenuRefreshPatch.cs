using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// TOR snapshots KnownAbilitySystem into AbilityRadialSelection_VM. Voidstep abilities
    /// are injected later, and TOR can also replace or rebuild the live AbilityComponent.
    /// Repair the current component and actual radial VM immediately before Q opens.
    /// </summary>
    internal static class TorRadialMenuRefresh
    {
        private const string HarmonyId = "xmarre.voidstep";
        private const int ExpectedVoidstepAbilityCount = 6;
        private static readonly object Sync = new object();
        private static readonly VoidstepLogger Logger = new VoidstepLogger();

        private static Type _abilityComponentType;
        private static Type _abilityType;
        private static MethodInfo _agentGetComponent;
        private static PropertyInfo _knownAbilitiesProperty;
        private static PropertyInfo _stringIdProperty;
        private static FieldInfo _managerAbilityViewField;
        private static FieldInfo _viewRadialVmField;
        private static MethodInfo _fillAbilities;
        private static FieldInfo _coordinatorTorField;
        private static MethodInfo _attachToAgent;
        private static PropertyInfo _radialAbilitiesProperty;
        private static WeakReference<Mission> _lastMission;
        private static int _lastKnownCount = -1;
        private static int _lastVoidstepCount = -1;
        private static int _lastRadialCount = -1;

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
                        true,
                        false);
                    var openMethod = managerType.GetMethod(
                        "EnableQuickSelectionMenuMode",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null);
                    if (openMethod == null)
                        throw new MissingMethodException(managerType.FullName, "EnableQuickSelectionMenuMode");

                    var patchInfo = Harmony.GetPatchInfo(openMethod);
                    if (patchInfo != null && patchInfo.Prefixes.Any(patch =>
                            string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal)))
                    {
                        return;
                    }

                    ResolveApi(torAssembly, managerType);
                    var prefix = typeof(TorRadialMenuRefresh).GetMethod(
                        nameof(RepairBeforeOpen),
                        BindingFlags.Static | BindingFlags.NonPublic);
                    if (prefix == null)
                        throw new MissingMethodException(typeof(TorRadialMenuRefresh).FullName, nameof(RepairBeforeOpen));

                    new Harmony(HarmonyId).Patch(openMethod, prefix: new HarmonyMethod(prefix));
                    Logger.Info("Installed TOR Q-wheel live component and radial-view repair.");
                }
                catch (Exception ex)
                {
                    Logger.Error("TOR Q-wheel live repair could not be installed.", Unwrap(ex));
                }
            }
        }

        private static void ResolveApi(Assembly torAssembly, Type managerType)
        {
            _abilityComponentType = torAssembly.GetType(
                "TOR_Core.AbilitySystem.AbilityComponent",
                true,
                false);
            _abilityType = torAssembly.GetType(
                "TOR_Core.AbilitySystem.Ability",
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
            _radialAbilitiesProperty = radialVmType.GetProperty(
                "Abilities",
                BindingFlags.Instance | BindingFlags.Public);

            _coordinatorTorField = typeof(AbilityWheelCoordinator).GetField(
                "_tor",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _attachToAgent = typeof(TorAbilityWheelAdapter).GetMethod(
                "AttachToAgent",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(Agent) },
                null);

            if (_knownAbilitiesProperty == null || _stringIdProperty == null ||
                _managerAbilityViewField == null || _viewRadialVmField == null ||
                _fillAbilities == null || _radialAbilitiesProperty == null ||
                _coordinatorTorField == null || _attachToAgent == null)
            {
                throw new MissingMemberException("TOR radial-menu live repair API surface is incomplete.");
            }
        }

        private static void RepairBeforeOpen(object __instance)
        {
            lock (Sync)
            {
                try
                {
                    var mission = Mission.Current;
                    var agent = mission?.MainAgent;
                    if (mission == null || agent == null || !agent.IsActive() || __instance == null)
                        return;

                    ResetCountersForNewMission(mission);

                    var component = GetAbilityComponent(agent);
                    var knownAbilities = GetKnownAbilities(component);
                    var voidstepCount = CountVoidstepAbilities(knownAbilities);

                    if (knownAbilities == null || voidstepCount != ExpectedVoidstepAbilityCount)
                    {
                        if (ForceAdapterReattach(agent))
                        {
                            component = GetAbilityComponent(agent);
                            knownAbilities = GetKnownAbilities(component);
                            voidstepCount = CountVoidstepAbilities(knownAbilities);
                        }
                    }

                    var abilityView = _managerAbilityViewField.GetValue(__instance);
                    var radialVm = abilityView == null ? null : _viewRadialVmField.GetValue(abilityView);
                    if (radialVm == null)
                    {
                        LogState(
                            knownAbilities?.Count ?? -1,
                            voidstepCount,
                            -1,
                            "TOR radial VM was not ready when Q opened");
                        return;
                    }

                    _fillAbilities.Invoke(radialVm, new object[] { agent });
                    var radialAbilities = _radialAbilitiesProperty.GetValue(radialVm, null);
                    var radialCount = ReadCount(radialAbilities);
                    LogState(
                        knownAbilities?.Count ?? -1,
                        voidstepCount,
                        radialCount,
                        voidstepCount == ExpectedVoidstepAbilityCount
                            ? "Rebuilt TOR Q wheel from the repaired live component"
                            : "TOR Q wheel repair could not restore all Voidstep proxies");
                }
                catch (Exception ex)
                {
                    Logger.Error("TOR Q-wheel live repair failed during Q open.", Unwrap(ex));
                }
            }
        }

        private static void ResetCountersForNewMission(Mission mission)
        {
            Mission previous;
            if (_lastMission != null &&
                _lastMission.TryGetTarget(out previous) &&
                ReferenceEquals(previous, mission))
            {
                return;
            }

            _lastMission = new WeakReference<Mission>(mission);
            _lastKnownCount = -1;
            _lastVoidstepCount = -1;
            _lastRadialCount = -1;
        }

        private static object GetAbilityComponent(Agent agent)
        {
            if (agent == null || _agentGetComponent == null || _abilityComponentType == null)
                return null;
            return _agentGetComponent.MakeGenericMethod(_abilityComponentType).Invoke(agent, null);
        }

        private static IList GetKnownAbilities(object component)
        {
            return component == null ? null : _knownAbilitiesProperty.GetValue(component, null) as IList;
        }

        private static bool ForceAdapterReattach(Agent agent)
        {
            var coordinator = VoidstepWheelRuntime.Current;
            if (coordinator == null)
            {
                Logger.Debug("Voidstep ability-wheel coordinator is not live yet during TOR Q open.");
                return false;
            }

            var adapter = _coordinatorTorField.GetValue(coordinator);
            if (adapter == null)
            {
                Logger.Debug("Voidstep TOR adapter is not live yet during TOR Q open.");
                return false;
            }

            _attachToAgent.Invoke(adapter, new object[] { agent });
            return true;
        }

        private static int CountVoidstepAbilities(IList abilities)
        {
            if (abilities == null)
                return 0;

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

        private static int ReadCount(object collection)
        {
            if (collection == null)
                return -1;
            if (collection is ICollection nonGeneric)
                return nonGeneric.Count;
            var property = collection.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            return property == null ? -1 : Convert.ToInt32(property.GetValue(collection, null));
        }

        private static void LogState(int knownCount, int voidstepCount, int radialCount, string prefix)
        {
            if (knownCount == _lastKnownCount &&
                voidstepCount == _lastVoidstepCount &&
                radialCount == _lastRadialCount)
            {
                return;
            }

            _lastKnownCount = knownCount;
            _lastVoidstepCount = voidstepCount;
            _lastRadialCount = radialCount;
            Logger.Info(prefix + ": known=" + knownCount +
                        ", Voidstep=" + voidstepCount +
                        ", radial=" + radialCount + ".");
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
