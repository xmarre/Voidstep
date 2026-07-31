using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// TOR's targeting state can apply an idle spell stance on action channel 1 for Spell and
    /// Prayer abilities. Voidstep proxies use TOR only as a selector, so that presentation must
    /// never escape live targeting state 2. TOR intentionally caches CurrentAbility after closing;
    /// proxy identity by itself is therefore not ownership.
    /// </summary>
    internal static class TorProxyCastStanceFix
    {
        private const string HarmonyId = "xmarre.voidstep.tor-proxy-cast-stance";
        private static readonly VoidstepLogger Logger = new VoidstepLogger();

        private static bool _installed;
        private static FieldInfo _abilityComponentField;
        private static PropertyInfo _currentAbilityProperty;
        private static FieldInfo _shouldPlayIdleCastStanceAnimField;
        private static FieldInfo _shouldSheathWeaponField;
        private static FieldInfo _disableCombatActionsAfterCastField;
        private static FieldInfo _currentStateField;
        private static ActionIndexCache? _idleAnimation;

        internal static void Install()
        {
            if (_installed)
                return;

            try
            {
                var torAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly => string.Equals(
                        assembly.GetName().Name,
                        "TOR_Core",
                        StringComparison.OrdinalIgnoreCase));
                if (torAssembly == null)
                    return;

                var logicType = torAssembly.GetType(
                    "TOR_Core.AbilitySystem.AbilityManagerMissionLogic",
                    true,
                    false);
                var componentType = torAssembly.GetType(
                    "TOR_Core.AbilitySystem.AbilityComponent",
                    true,
                    false);

                _abilityComponentField = RequireField(logicType, "_abilityComponent");
                _shouldPlayIdleCastStanceAnimField = RequireField(logicType, "_shouldPlayIdleCastStanceAnim");
                _shouldSheathWeaponField = RequireField(logicType, "_shouldSheathWeapon");
                _disableCombatActionsAfterCastField = RequireField(logicType, "_disableCombatActionsAfterCast");
                _currentStateField = RequireField(logicType, "_currentState");
                _currentAbilityProperty = componentType.GetProperty(
                    "CurrentAbility",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_currentAbilityProperty == null)
                    throw new MissingMemberException(componentType.FullName, "CurrentAbility");

                _idleAnimation = ResolveIdleAnimation(logicType);

                var handleAnimations = RequireMethod(logicType, "HandleAnimations");
                var enableTargetingMode = RequireMethod(logicType, "EnableTargetingMode");
                var disableAbilityMode = logicType.GetMethod(
                    "DisableAbilityMode",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (disableAbilityMode == null)
                    throw new MissingMethodException(logicType.FullName, "DisableAbilityMode");

                var harmony = new Harmony(HarmonyId);
                harmony.Patch(
                    handleAnimations,
                    prefix: new HarmonyMethod(
                        typeof(TorProxyCastStanceFix),
                        nameof(BeforeTorHandleAnimations)));
                harmony.Patch(
                    enableTargetingMode,
                    postfix: new HarmonyMethod(
                        typeof(TorProxyCastStanceFix),
                        nameof(AfterTorEnableTargetingMode)));
                harmony.Patch(
                    disableAbilityMode,
                    postfix: new HarmonyMethod(
                        typeof(TorProxyCastStanceFix),
                        nameof(AfterTorDisableAbilityMode)));

                _installed = true;
                Logger.Info("Installed state-bounded TOR Voidstep proxy presentation cleanup.");
            }
            catch (Exception ex)
            {
                Logger.Error("TOR Voidstep proxy presentation fix could not be installed.", Unwrap(ex));
            }
        }

        internal static void ReleaseBeforeVoidstepActivation()
        {
            var actor = Agent.Main;
            if (actor == null || !actor.IsActive())
                return;

            // This boundary is deliberately action-only. Do not assign LookDirection, change
            // movement direction, or unlock look control here.
            ClearProxyCastAction(actor, "before Voidstep activation");
        }

        private static bool BeforeTorHandleAnimations(object __instance)
        {
            if (!TryGetCurrentVoidstepProxy(__instance, true, out var actor))
                return true;

            NeutralizeProxyFlags(__instance);
            ClearProxyCastAction(actor, "during live TOR targeting");

            // Preserve TOR's normal method. With the proxy idle flag disabled its 1.16 body is a
            // no-op, while any future non-orientation bookkeeping can still execute.
            return true;
        }

        private static void AfterTorEnableTargetingMode(object __instance)
        {
            if (!TryGetCurrentVoidstepProxy(__instance, true, out var actor))
                return;

            NeutralizeProxyFlags(__instance);
            ClearProxyCastAction(actor, "after live TOR targeting opened");
        }

        private static void AfterTorDisableAbilityMode(object __instance)
        {
            // DisableAbilityMode has already changed state 2 to state 0. CurrentAbility remains
            // cached, so proxy identity is valid only for this explicit cleanup boundary.
            if (!TryGetCurrentVoidstepProxy(__instance, false, out var actor))
                return;

            NeutralizeProxyFlags(__instance);
            ClearProxyCastAction(actor, "after TOR targeting closed");
        }

        private static bool TryGetCurrentVoidstepProxy(
            object logic,
            bool requireLiveTargeting,
            out Agent actor)
        {
            actor = Agent.Main;
            if (logic == null || actor == null || !actor.IsActive())
                return false;

            try
            {
                var state = Convert.ToInt32(_currentStateField.GetValue(logic));
                if (requireLiveTargeting && state != 2)
                    return false;

                var component = _abilityComponentField.GetValue(logic);
                var currentAbility = component == null
                    ? null
                    : _currentAbilityProperty.GetValue(component, null);
                var coordinator = VoidstepWheelRuntime.Current;
                return currentAbility != null && coordinator != null && coordinator.IsTorProxy(currentAbility);
            }
            catch (Exception ex)
            {
                Logger.Debug("TOR proxy ownership read failed safely: " + Unwrap(ex).Message);
                return false;
            }
        }

        private static void NeutralizeProxyFlags(object logic)
        {
            if (logic == null)
                return;

            try
            {
                _shouldPlayIdleCastStanceAnimField.SetValue(logic, false);
                _shouldSheathWeaponField.SetValue(logic, false);
                _disableCombatActionsAfterCastField.SetValue(logic, false);
            }
            catch (Exception ex)
            {
                Logger.Debug("TOR proxy presentation flags could not be cleared: " + Unwrap(ex).Message);
            }
        }

        private static void ClearProxyCastAction(Agent actor, string stage)
        {
            if (actor == null || !actor.IsActive() || !_idleAnimation.HasValue)
                return;

            try
            {
                var current = actor.GetCurrentAction(1);
                if (current != _idleAnimation.Value)
                    return;

                actor.SetCurrentActionSpeed(1, 1f);
                actor.SetActionChannel(1, ActionIndexCache.act_none);
                Logger.Debug("Cleared TOR proxy idle action " + stage + "; actor=" + actor.Index + ".");
            }
            catch (Exception ex)
            {
                Logger.Debug("TOR proxy action-channel cleanup failed safely: " + Unwrap(ex).Message);
            }
        }

        private static ActionIndexCache? ResolveIdleAnimation(Type logicType)
        {
            try
            {
                var property = logicType.GetProperty(
                    "IdleAnimation",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var value = property?.GetValue(null, null);
                if (value is ActionIndexCache action)
                    return action;

                var field = logicType.GetField(
                    "_idleAnimation",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                value = field?.GetValue(null);
                if (value is ActionIndexCache fieldAction)
                    return fieldAction;
            }
            catch
            {
            }

            try { return ActionIndexCache.Create("act_spellcasting_idle"); }
            catch { return null; }
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            var method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (method == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }
    }

    [HarmonyPatch(typeof(AbilitySelectionController), nameof(AbilitySelectionController.Confirm))]
    internal static class TorProxyReleaseBeforeConfirmPatch
    {
        private static void Prefix()
        {
            TorProxyCastStanceFix.ReleaseBeforeVoidstepActivation();
        }
    }

    [HarmonyPatch(typeof(VoidstepMissionBehavior), nameof(VoidstepMissionBehavior.EarlyStart))]
    internal static class TorProxyCastStanceFixInstallerPatch
    {
        private static void Postfix()
        {
            TorProxyCastStanceFix.Install();
        }
    }
}
