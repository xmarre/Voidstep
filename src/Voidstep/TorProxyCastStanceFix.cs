using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// TOR's targeting state repeatedly applies act_spellcasting_idle on action channel 1
    /// for Spell and Prayer abilities. Voidstep proxies never complete TOR's own cast path,
    /// so that action can survive DisableAbilityMode and continue owning the skeleton/facing
    /// until another combat input interrupts it. Suppress only that TOR presentation for
    /// Voidstep proxies and explicitly clear any already-applied proxy stance before activation.
    /// </summary>
    internal static class TorProxyCastStanceFix
    {
        private const string HarmonyId = "xmarre.voidstep.tor-proxy-cast-stance";
        private static readonly VoidstepLogger Logger = new VoidstepLogger();
        private static readonly ActionIndexCache SpellcastingIdle =
            ActionIndexCache.Create("act_spellcasting_idle");

        private static bool _installed;
        private static FieldInfo _abilityComponentField;
        private static PropertyInfo _currentAbilityProperty;
        private static FieldInfo _shouldPlayIdleCastStanceAnimField;
        private static FieldInfo _shouldSheathWeaponField;
        private static FieldInfo _disableCombatActionsAfterCastField;
        private static FieldInfo _currentStateField;

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
                Logger.Info("Installed TOR Voidstep proxy cast-stance suppression and action-channel cleanup.");
            }
            catch (Exception ex)
            {
                Logger.Error("TOR Voidstep proxy cast-stance fix could not be installed.", Unwrap(ex));
            }
        }

        internal static void ReleaseBeforeVoidstepActivation()
        {
            var actor = Agent.Main;
            if (actor == null || !actor.IsActive())
                return;

            ClearProxyCastAction(actor, "before Voidstep activation");
        }

        private static bool BeforeTorHandleAnimations(object __instance)
        {
            if (!TryGetCurrentVoidstepProxy(__instance, out var actor))
                return true;

            NeutralizeProxyFlags(__instance);
            ClearProxyCastAction(actor, "during TOR targeting");
            return false;
        }

        private static void AfterTorEnableTargetingMode(object __instance)
        {
            if (!TryGetCurrentVoidstepProxy(__instance, out var actor))
                return;

            // EnableTargetingMode sets these for Spell/Prayer templates before the next
            // animation tick. Voidstep owns its own presentation and must not inherit them.
            NeutralizeProxyFlags(__instance);
            ClearProxyCastAction(actor, "after TOR targeting opened");
        }

        private static void AfterTorDisableAbilityMode(object __instance)
        {
            if (!TryGetCurrentVoidstepProxy(__instance, out var actor))
                return;

            NeutralizeProxyFlags(__instance);
            ClearProxyCastAction(actor, "after TOR targeting closed");
        }

        private static bool TryGetCurrentVoidstepProxy(object logic, out Agent actor)
        {
            actor = Agent.Main;
            if (logic == null || actor == null || !actor.IsActive())
                return false;

            try
            {
                var component = _abilityComponentField?.GetValue(logic);
                var currentAbility = component == null
                    ? null
                    : _currentAbilityProperty?.GetValue(component, null);
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
                _shouldPlayIdleCastStanceAnimField?.SetValue(logic, false);
                _shouldSheathWeaponField?.SetValue(logic, false);
                _disableCombatActionsAfterCastField?.SetValue(logic, false);

                // DisableAbilityMode normally owns the state transition. This assignment is
                // only a guard for proxy cleanup paths where TOR retained state 2 for a tick.
                if (_currentStateField != null && Convert.ToInt32(_currentStateField.GetValue(logic)) == 2)
                    _currentStateField.SetValue(logic, 0);
            }
            catch (Exception ex)
            {
                Logger.Debug("TOR proxy presentation flags could not be cleared: " + Unwrap(ex).Message);
            }
        }

        private static void ClearProxyCastAction(Agent actor, string stage)
        {
            if (actor == null || !actor.IsActive())
                return;

            try
            {
                var preservedLook = Normalize(actor.LookDirection);
                var mount = actor.MountAgent;
                var preservedMountLook = mount != null && mount.IsActive()
                    ? Normalize(mount.LookDirection)
                    : Vec3.Zero;

                var current = actor.GetCurrentAction(1);
                if (current == SpellcastingIdle)
                {
                    actor.SetCurrentActionSpeed(1, 1f);
                    actor.SetActionChannel(1, ActionIndexCache.act_none);
                }

                actor.IsLookDirectionLocked = false;
                actor.LookDirection = preservedLook;
                if (mount != null && mount.IsActive())
                {
                    mount.IsLookDirectionLocked = false;
                    mount.LookDirection = preservedMountLook;
                }

                Logger.Debug(
                    $"TOR proxy cast stance released {stage}; actor={actor.Index}, " +
                    $"clearedIdle={current == SpellcastingIdle}, facing={Format(preservedLook)}.");
            }
            catch (Exception ex)
            {
                Logger.Debug("TOR proxy action-channel cleanup failed safely: " + Unwrap(ex).Message);
            }
        }

        private static Vec3 Normalize(Vec3 direction)
        {
            direction.z = 0f;
            if (direction.Normalize() < 0.001f)
                direction = Vec3.Forward;
            return direction;
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

        private static string Format(Vec3 value) =>
            $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";
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
