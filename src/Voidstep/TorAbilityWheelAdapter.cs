using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class TorAbilityWheelAdapter
    {
        private const string HarmonyId = "xmarre.voidstep.torwheel";
        private const float AttachRetryInterval = 0.5f;
        private readonly Mission _mission;
        private readonly AbilitySelectionController _selection;
        private readonly VoidstepLogger _logger;
        private readonly Dictionary<object, AbilityId> _proxies =
            new Dictionary<object, AbilityId>(ReferenceEqualityComparer.Instance);
        private readonly List<string> _donorSprites = new List<string>(8);
        private Harmony _harmony;
        private Assembly _torAssembly;
        private Type _abilityType;
        private Type _spellType;
        private Type _abilityTemplateType;
        private Type _abilityComponentType;
        private Type _abilityFactoryType;
        private Type _abilityManagerLogicType;
        private MethodInfo _agentGetComponent;
        private MethodInfo _missionGetBehavior;
        private MethodInfo _initializeAbility;
        private MethodInfo _initializeCrosshair;
        private MethodInfo _setCrosshair;
        private MethodInfo _disableAbilityMode;
        private PropertyInfo _knownAbilitiesProperty;
        private PropertyInfo _currentAbilityProperty;
        private PropertyInfo _currentStateProperty;
        private Agent _agent;
        private object _component;
        private object _logic;
        private IList _knownAbilities;
        private bool _apiReady;
        private bool _available;
        private bool _targetingOwned;
        private int _lastState = -1;
        private float _attachRetryRemaining;

        internal TorAbilityWheelAdapter(Mission mission, AbilitySelectionController selection, VoidstepLogger logger)
        {
            _mission = mission;
            _selection = selection;
            _logger = logger;
            TryInitialize();
        }

        internal bool IsAvailable => _available;
        internal bool OwnsTargeting => _targetingOwned;

        internal void Tick(float dt)
        {
            if (!_apiReady)
                return;

            var currentAgent = _mission.MainAgent;
            if (currentAgent == null || !currentAgent.IsActive())
            {
                if (_targetingOwned)
                    _selection.Cancel(true);
                _targetingOwned = false;
                _available = false;
                _lastState = -1;
                _attachRetryRemaining = 0f;
                return;
            }

            if (!ReferenceEquals(currentAgent, _agent))
                _attachRetryRemaining = 0f;

            var needsAttachment = !ReferenceEquals(currentAgent, _agent) ||
                                  _component == null || _knownAbilities == null ||
                                  _logic == null || !_available;
            if (needsAttachment)
            {
                _attachRetryRemaining -= Math.Max(0f, dt);
                if (_attachRetryRemaining <= 0f)
                {
                    _attachRetryRemaining = AttachRetryInterval;
                    AttachToAgent(currentAgent);
                }
            }
            if (!_available || _component == null || _logic == null)
                return;

            object currentAbility = null;
            var state = -1;
            try
            {
                currentAbility = _currentAbilityProperty.GetValue(_component, null);
                state = Convert.ToInt32(_currentStateProperty.GetValue(_logic, null));
            }
            catch (Exception ex)
            {
                _logger.Debug("TOR wheel state read failed safely: " + Unwrap(ex).Message);
                _available = false;
                _attachRetryRemaining = AttachRetryInterval;
                return;
            }

            if (state == 1 && _lastState != 1 && _selection.HasSelection)
            {
                _selection.Cancel(true);
                _logger.Debug("Opening TOR's Q wheel cancelled the previous Voidstep selection.");
            }

            if (state == 2 && TryGetProxyAbility(currentAbility, out var ability))
            {
                _targetingOwned = true;
                if (!_selection.SelectedAbility.HasValue || _selection.SelectedAbility.Value != ability)
                    _selection.Select(ability, "TOR Q cast wheel");
            }
            else if (_targetingOwned)
            {
                _targetingOwned = false;
                if (_selection.HasSelection)
                    _selection.Cancel(true);
            }

            _lastState = state;
        }

        internal bool TryGetProxyAbility(object instance, out AbilityId ability)
        {
            ability = default(AbilityId);
            return instance != null && _proxies.TryGetValue(instance, out ability);
        }

        internal void CloseTargetingMode()
        {
            if (!_targetingOwned || _logic == null || _disableAbilityMode == null)
                return;
            try
            {
                _disableAbilityMode.Invoke(_logic, new object[] { false, null });
                _logger.Debug("Closed TOR targeting mode after Voidstep RightMouseButton confirmation.");
            }
            catch (Exception ex)
            {
                _logger.Debug("TOR targeting-mode cleanup failed safely: " + Unwrap(ex).Message);
            }
            finally
            {
                _targetingOwned = false;
                _lastState = -1;
            }
        }

        internal void Cleanup()
        {
            _targetingOwned = false;
            RemoveInjectedProxies();
            _selection.Cancel(true);
            try { _harmony?.UnpatchAll(HarmonyId); }
            catch (Exception ex) { _logger.Debug("TOR wheel patch cleanup failed safely: " + ex.Message); }
            _harmony = null;
            _apiReady = false;
            _available = false;
            _logic = null;
            _component = null;
            _knownAbilities = null;
            _agent = null;
            _lastState = -1;
            _attachRetryRemaining = 0f;
            _donorSprites.Clear();
            _proxies.Clear();
        }

        private void TryInitialize()
        {
            try
            {
                _torAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "TOR_Core", StringComparison.OrdinalIgnoreCase));
                if (_torAssembly == null)
                    return;

                _abilityType = RequireType("TOR_Core.AbilitySystem.Ability");
                _spellType = RequireType("TOR_Core.AbilitySystem.Spells.Spell");
                _abilityTemplateType = RequireType("TOR_Core.AbilitySystem.AbilityTemplate");
                _abilityComponentType = RequireType("TOR_Core.AbilitySystem.AbilityComponent");
                _abilityFactoryType = RequireType("TOR_Core.AbilitySystem.AbilityFactory");
                _abilityManagerLogicType = RequireType("TOR_Core.AbilitySystem.AbilityManagerMissionLogic");

                _agentGetComponent = typeof(Agent).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Single(method => method.Name == "GetComponent" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);
                _missionGetBehavior = typeof(Mission).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Single(method => method.Name == "GetMissionBehavior" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);
                _knownAbilitiesProperty = _abilityComponentType.GetProperty("KnownAbilitySystem", BindingFlags.Instance | BindingFlags.Public);
                _currentAbilityProperty = _abilityComponentType.GetProperty("CurrentAbility", BindingFlags.Instance | BindingFlags.Public);
                _currentStateProperty = _abilityManagerLogicType.GetProperty("CurrentState", BindingFlags.Instance | BindingFlags.Public);
                _initializeAbility = _abilityFactoryType.GetMethod("InitializeAbility", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _initializeCrosshair = _abilityFactoryType.GetMethod("InitializeCrosshair", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _setCrosshair = _abilityType.GetMethod("SetCrosshair", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _disableAbilityMode = _abilityManagerLogicType.GetMethod("DisableAbilityMode", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_knownAbilitiesProperty == null || _currentAbilityProperty == null || _currentStateProperty == null ||
                    _initializeAbility == null || _initializeCrosshair == null || _setCrosshair == null || _disableAbilityMode == null)
                {
                    throw new MissingMemberException("TOR ability-wheel API surface is incomplete.");
                }

                _harmony = new Harmony(HarmonyId);
                var prefix = new HarmonyMethod(typeof(TorAbilityWheelAdapter).GetMethod(
                    nameof(IsDisabledPrefix), BindingFlags.Static | BindingFlags.NonPublic));
                var baseDisabled = _abilityType.GetMethod("IsDisabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var spellDisabled = _spellType.GetMethod("IsDisabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (baseDisabled != null) _harmony.Patch(baseDisabled, prefix: prefix);
                if (spellDisabled != null && spellDisabled != baseDisabled) _harmony.Patch(spellDisabled, prefix: prefix);

                _apiReady = true;
                _available = false;
                _attachRetryRemaining = 0f;
                _logger.Info("TOR 1.16 ability-wheel API detected; Voidstep injection will activate after the main agent's TOR ability component is available.");
            }
            catch (Exception ex)
            {
                _apiReady = false;
                _available = false;
                try { _harmony?.UnpatchAll(HarmonyId); } catch { }
                _harmony = null;
                _logger.Error("TOR ability-wheel integration failed; the standalone Voidstep Q wheel will be used instead.", Unwrap(ex));
            }
        }

        private void AttachToAgent(Agent agent)
        {
            RemoveInjectedProxies();
            _agent = agent;
            _component = null;
            _knownAbilities = null;
            _logic = null;
            _available = false;
            _lastState = -1;
            try
            {
                var getComponent = _agentGetComponent.MakeGenericMethod(_abilityComponentType);
                _component = getComponent.Invoke(agent, null);
                if (_component == null)
                {
                    _logger.Debug("TOR main agent has no AbilityComponent yet; standalone wheel remains active until the next throttled retry.");
                    return;
                }
                _knownAbilities = _knownAbilitiesProperty.GetValue(_component, null) as IList;
                if (_knownAbilities == null)
                    throw new InvalidCastException("TOR KnownAbilitySystem does not expose IList semantics.");
                _logic = GetMissionBehavior(_abilityManagerLogicType);
                if (_logic == null)
                {
                    _logger.Debug("TOR ability-manager mission logic is not ready; standalone wheel remains active until the next throttled retry.");
                    return;
                }
                InjectProxies(agent);
                _available = true;
                _attachRetryRemaining = 0f;
            }
            catch (Exception ex)
            {
                RemoveInjectedProxies();
                _component = null;
                _knownAbilities = null;
                _logic = null;
                _available = false;
                _logger.Debug("TOR main-agent wheel attachment failed safely; standalone wheel remains active: " + Unwrap(ex).Message);
            }
        }

        private void InjectProxies(Agent agent)
        {
            ResolveDonorSprites();
            for (var i = 0; i < VoidstepInputBindings.Abilities.Length; i++)
            {
                var abilityId = VoidstepInputBindings.Abilities[i];
                var existing = FindProxyByStringId(AbilityPresentation.TorStringId(abilityId));
                if (existing != null)
                {
                    _proxies[existing] = abilityId;
                    continue;
                }

                var template = Activator.CreateInstance(_abilityTemplateType);
                SetProperty(template, "StringID", AbilityPresentation.TorStringId(abilityId));
                SetProperty(template, "Name", "[Voidstep] " + AbilityPresentation.Name(abilityId));
                SetProperty(template, "SpriteName", _donorSprites[i % _donorSprites.Count]);
                SetProperty(template, "TooltipDescription", AbilityPresentation.Description(abilityId));
                SetProperty(template, "AbilityType", Enum.ToObject(RequireType("TOR_Core.AbilitySystem.AbilityType"), 1));
                SetProperty(template, "AbilityTargetType", Enum.ToObject(RequireType("TOR_Core.AbilitySystem.AbilityTargetType"), 5));
                SetProperty(template, "CrosshairType", Enum.ToObject(RequireType("TOR_Core.AbilitySystem.Crosshairs.CrosshairType"), 5));
                SetProperty(template, "CastType", Enum.ToObject(RequireType("TOR_Core.AbilitySystem.CastType"), 0));
                SetProperty(template, "CoolDown", 0);
                SetProperty(template, "WindsOfMagicCost", 0);
                SetProperty(template, "CastTime", 0f);
                SetProperty(template, "Duration", 0f);
                SetProperty(template, "Radius", PreviewRadius(abilityId));
                SetProperty(template, "MinDistance", 0f);
                SetProperty(template, "MaxDistance", PreviewRange(abilityId));
                SetProperty(template, "MaxDistanceSpecified", true);
                SetProperty(template, "TargetCapturingRadius", Math.Max(1f, PreviewRadius(abilityId)));
                SetProperty(template, "BelongsToLoreID", "voidstep");

                var proxy = _initializeAbility.Invoke(null, new[] { template, agent });
                if (proxy == null)
                    throw new InvalidOperationException("TOR AbilityFactory returned null for " + abilityId + ".");
                var crosshair = _initializeCrosshair.Invoke(null, new[] { template });
                if (crosshair != null)
                    _setCrosshair.Invoke(proxy, new[] { crosshair });
                _knownAbilities.Add(proxy);
                _proxies.Add(proxy, abilityId);
            }
            _logger.Info("Injected six distinguishable Voidstep selections into TOR's existing Q ability wheel.");
        }

        private object FindProxyByStringId(string stringId)
        {
            if (_knownAbilities == null) return null;
            var stringIdProperty = _abilityType.GetProperty("StringID", BindingFlags.Instance | BindingFlags.Public);
            for (var i = 0; i < _knownAbilities.Count; i++)
            {
                var entry = _knownAbilities[i];
                if (entry == null || !_abilityType.IsInstanceOfType(entry)) continue;
                var value = stringIdProperty?.GetValue(entry, null) as string;
                if (string.Equals(value, stringId, StringComparison.Ordinal))
                    return entry;
            }
            return null;
        }

        private void ResolveDonorSprites()
        {
            _donorSprites.Clear();
            if (_knownAbilities != null)
            {
                var templateProperty = _abilityType.GetProperty("Template", BindingFlags.Instance | BindingFlags.Public);
                var spriteProperty = _abilityTemplateType.GetProperty("SpriteName", BindingFlags.Instance | BindingFlags.Public);
                for (var i = 0; i < _knownAbilities.Count; i++)
                {
                    var template = templateProperty?.GetValue(_knownAbilities[i], null);
                    var sprite = template == null ? null : spriteProperty?.GetValue(template, null) as string;
                    if (string.IsNullOrWhiteSpace(sprite) || _donorSprites.Contains(sprite))
                        continue;
                    _donorSprites.Add(sprite);
                    if (_donorSprites.Count >= VoidstepInputBindings.Abilities.Length)
                        break;
                }
            }
            if (_donorSprites.Count == 0)
                _donorSprites.Add("default_spell");
            var uniqueCount = _donorSprites.Count;
            while (_donorSprites.Count < VoidstepInputBindings.Abilities.Length)
                _donorSprites.Add(_donorSprites[_donorSprites.Count % uniqueCount]);
        }

        private void RemoveInjectedProxies()
        {
            if (_knownAbilities != null && _proxies.Count > 0)
            {
                foreach (var proxy in _proxies.Keys.ToArray())
                {
                    try { if (_knownAbilities.Contains(proxy)) _knownAbilities.Remove(proxy); }
                    catch { }
                }
            }
            _proxies.Clear();
            _donorSprites.Clear();
        }

        private object GetMissionBehavior(Type behaviorType)
        {
            try { return _missionGetBehavior.MakeGenericMethod(behaviorType).Invoke(_mission, null); }
            catch { return null; }
        }

        private Type RequireType(string name) =>
            _torAssembly.GetType(name, true, false);

        private static void SetProperty(object instance, string name, object value)
        {
            var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
                property.SetValue(instance, value, null);
        }

        private static float PreviewRange(AbilityId ability)
        {
            var settings = VoidstepSettings.Current;
            switch (ability)
            {
                case AbilityId.VoidstepCleave: return settings.VoidstepRange;
                case AbilityId.Blink: return settings.BlinkRange;
                case AbilityId.Windblast: return settings.WindblastRange;
                case AbilityId.Domino: return settings.DominoRange;
                case AbilityId.DarkVision: return settings.DarkVisionRange;
                default: return 5f;
            }
        }

        private static float PreviewRadius(AbilityId ability)
        {
            var settings = VoidstepSettings.Current;
            switch (ability)
            {
                case AbilityId.VoidstepCleave: return settings.CleaveRadius;
                case AbilityId.Windblast: return settings.WindblastRange;
                case AbilityId.Domino: return settings.DominoRange;
                case AbilityId.DarkVision: return settings.DarkVisionRange;
                default: return 1f;
            }
        }

        private static bool IsDisabledPrefix(object __instance, ref bool __result)
        {
            var runtime = VoidstepWheelRuntime.Current;
            if (runtime == null || !runtime.IsTorProxy(__instance))
                return true;
            __result = false;
            return false;
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object left, object right) => ReferenceEquals(left, right);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
    }
}
