using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    /// <summary>
    /// Replaces the earlier broad TOR cast-stance patch. TOR keeps CurrentAbility cached after
    /// targeting closes, so proxy identity alone is not ownership. Only live state 2 may alter
    /// TOR's targeting presentation. This fix never writes LookDirection or look-lock state.
    /// </summary>
    internal static class TorProxyOrientationOwnershipFix
    {
        private const string LegacyHarmonyId = "xmarre.voidstep.tor-proxy-cast-stance";
        private const string HarmonyId = "xmarre.voidstep.tor-proxy-orientation-ownership";
        private static readonly VoidstepLogger Logger = new VoidstepLogger();

        private static bool _installed;
        private static FieldInfo _abilityComponentField;
        private static PropertyInfo _currentAbilityProperty;
        private static FieldInfo _currentStateField;
        private static FieldInfo _shouldPlayIdleCastStanceAnimField;
        private static FieldInfo _shouldSheathWeaponField;
        private static FieldInfo _disableCombatActionsAfterCastField;
        private static ActionIndexCache? _idleAnimation;

        internal static void Install()
        {
            if (_installed)
                return;

            try
            {
                // Remove the previous dynamic TOR patches. Their proxy-only predicate remained true
                // after state 2 closed because TOR intentionally caches CurrentAbility.
                new Harmony(LegacyHarmonyId).UnpatchAll(LegacyHarmonyId);

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
                _currentStateField = RequireField(logicType, "_currentState");
                _shouldPlayIdleCastStanceAnimField = RequireField(logicType, "_shouldPlayIdleCastStanceAnim");
                _shouldSheathWeaponField = RequireField(logicType, "_shouldSheathWeapon");
                _disableCombatActionsAfterCastField = RequireField(logicType, "_disableCombatActionsAfterCast");
                _currentAbilityProperty = componentType.GetProperty(
                    "CurrentAbility",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_currentAbilityProperty == null)
                    throw new MissingMemberException(componentType.FullName, "CurrentAbility");

                _idleAnimation = ResolveIdleAnimation(logicType);

                var harmony = new Harmony(HarmonyId);
                harmony.Patch(
                    RequireMethod(logicType, "HandleAnimations"),
                    prefix: new HarmonyMethod(
                        typeof(TorProxyOrientationOwnershipFix),
                        nameof(BeforeHandleAnimations)));
                harmony.Patch(
                    RequireMethod(logicType, "EnableTargetingMode"),
                    postfix: new HarmonyMethod(
                        typeof(TorProxyOrientationOwnershipFix),
                        nameof(AfterEnableTargetingMode)));

                var disable = logicType.GetMethod(
                    "DisableAbilityMode",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (disable == null)
                    throw new MissingMethodException(logicType.FullName, "DisableAbilityMode");
                harmony.Patch(
                    disable,
                    postfix: new HarmonyMethod(
                        typeof(TorProxyOrientationOwnershipFix),
                        nameof(AfterDisableAbilityMode)));

                _installed = true;
                Logger.Info("Installed state-bounded TOR proxy presentation ownership; post-targeting look control is untouched.");
            }
            catch (Exception ex)
            {
                Logger.Error("State-bounded TOR proxy ownership fix could not be installed.", Unwrap(ex));
            }
        }

        private static bool BeforeHandleAnimations(object __instance)
        {
            if (!TryGetVoidstepProxy(__instance, true, out var actor))
                return true;

            NeutralizeProxyFlags(__instance);
            ClearExactIdle(actor, "during live TOR targeting");

            // Let TOR execute its method. With the proxy-only idle flag disabled, the exact
            // 1.16 implementation becomes a no-op while retaining compatibility with future
            // non-orientation animation bookkeeping.
            return true;
        }

        private static void AfterEnableTargetingMode(object __instance)
        {
            if (!TryGetVoidstepProxy(__instance, true, out var actor))
                return;
            NeutralizeProxyFlags(__instance);
            ClearExactIdle(actor, "after live TOR targeting opened");
        }

        private static void AfterDisableAbilityMode(object __instance)
        {
            // DisableAbilityMode has already changed state 2 to state 0. CurrentAbility remains
            // cached, so proxy identity is valid for this one cleanup boundary only.
            if (!TryGetVoidstepProxy(__instance, false, out var actor))
                return;
            NeutralizeProxyFlags(__instance);
            ClearExactIdle(actor, "after TOR targeting closed");
        }

        private static bool TryGetVoidstepProxy(object logic, bool requireLiveTargeting, out Agent actor)
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
                Logger.Debug("State-bounded TOR proxy ownership read failed safely: " + Unwrap(ex).Message);
                return false;
            }
        }

        private static void NeutralizeProxyFlags(object logic)
        {
            try
            {
                _shouldPlayIdleCastStanceAnimField.SetValue(logic, false);
                _shouldSheathWeaponField.SetValue(logic, false);
                _disableCombatActionsAfterCastField.SetValue(logic, false);
            }
            catch (Exception ex)
            {
                Logger.Debug("TOR proxy presentation flags could not be neutralized: " + Unwrap(ex).Message);
            }
        }

        private static void ClearExactIdle(Agent actor, string stage)
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
                Logger.Debug("TOR proxy idle cleanup failed safely: " + Unwrap(ex).Message);
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

    /// <summary>
    /// Captures the rendered body direction around a position-only teleport. The normal path never
    /// writes any direction. A narrowly gated correction is issued only if Bannerlord flips the body
    /// by more than 100 degrees while the player's look direction stayed within 60 degrees.
    /// </summary>
    internal static class PostTeleportOrientationGuard
    {
        private const float GuardSeconds = 0.24f;
        private const float PositionThresholdSquared = 0.01f;
        private const float BodyFlipDotThreshold = -0.17f;
        private const float StableLookDotThreshold = 0.50f;

        private static readonly ConditionalWeakTable<AbilityManager, State> States =
            new ConditionalWeakTable<AbilityManager, State>();

        private sealed class State
        {
            internal int ActorIndex = -1;
            internal int MountIndex = -1;
            internal Vec3 Facing = Vec3.Forward;
            internal Vec3 Look = Vec3.Forward;
            internal Vec3 MountFacing = Vec3.Forward;
            internal Vec3 TickPosition = Vec3.Invalid;
            internal Vec3 TickFacing = Vec3.Forward;
            internal Vec3 TickLook = Vec3.Forward;
            internal Vec3 TickMountFacing = Vec3.Forward;
            internal int TickMountIndex = -1;
            internal bool ObserveCleaveTick;
            internal float ExpiresAt;
            internal bool Armed;
            internal string Source;
            internal VoidstepLogger Logger;
        }

        internal static Snapshot Capture(Agent actor)
        {
            var mount = actor?.MountAgent;
            return new Snapshot(
                actor?.Index ?? -1,
                BodyAlignedCleaveRuntime.GetBodyFacing(actor),
                Normalize(actor != null ? actor.LookDirection : Vec3.Forward),
                mount != null && mount.IsActive() ? mount.Index : -1,
                mount != null && mount.IsActive()
                    ? BodyAlignedCleaveRuntime.GetBodyFacing(mount)
                    : Vec3.Forward,
                actor != null ? actor.Position : Vec3.Invalid);
        }

        internal static void Arm(AbilityManager manager, Agent actor, Snapshot snapshot, string source, VoidstepLogger logger)
        {
            if (manager == null || actor == null || !actor.IsActive() || snapshot.ActorIndex != actor.Index)
                return;

            var state = States.GetOrCreateValue(manager);
            state.ActorIndex = snapshot.ActorIndex;
            state.MountIndex = snapshot.MountIndex;
            state.Facing = snapshot.Facing;
            state.Look = snapshot.Look;
            state.MountFacing = snapshot.MountFacing;
            state.ExpiresAt = MBCommon.GetApplicationTime() + GuardSeconds;
            state.Armed = true;
            state.Source = source;
            state.Logger = logger;

            var bodyAfter = BodyAlignedCleaveRuntime.GetBodyFacing(actor);
            var lookAfter = Normalize(actor.LookDirection);
            logger?.Debug(
                source + " position-only teleport armed orientation guard; " +
                "bodyBefore=" + Format(snapshot.Facing) + ", bodyAfter=" + Format(bodyAfter) +
                ", lookBefore=" + Format(snapshot.Look) + ", lookAfter=" + Format(lookAfter) + ".");
        }

        internal static void BeforeManagerTick(AbilityManager manager, AbilityContext context)
        {
            if (manager == null || context?.Player == null ||
                !manager.IsBusy || manager.ActiveAbility != AbilityId.VoidstepCleave)
                return;

            var actor = context.Player;
            var state = States.GetOrCreateValue(manager);
            var snapshot = Capture(actor);
            state.TickPosition = snapshot.Position;
            state.TickFacing = snapshot.Facing;
            state.TickLook = snapshot.Look;
            state.TickMountIndex = snapshot.MountIndex;
            state.TickMountFacing = snapshot.MountFacing;
            state.ObserveCleaveTick = true;
            state.Logger = context.Logger;
        }

        internal static void AfterManagerTick(AbilityManager manager, AbilityContext context)
        {
            if (manager == null || context?.Player == null)
                return;

            var actor = context.Player;
            var state = States.GetOrCreateValue(manager);
            if (state.ObserveCleaveTick)
            {
                state.ObserveCleaveTick = false;
                if (state.TickPosition.IsValid &&
                    (actor.Position - state.TickPosition).LengthSquared > PositionThresholdSquared)
                {
                    Arm(
                        manager,
                        actor,
                        new Snapshot(
                            actor.Index,
                            state.TickFacing,
                            state.TickLook,
                            state.TickMountIndex,
                            state.TickMountFacing,
                            state.TickPosition),
                        "Voidstep Cleave",
                        state.Logger);
                }
            }

            Tick(manager, actor);
        }

        private static void Tick(AbilityManager manager, Agent actor)
        {
            var state = States.GetOrCreateValue(manager);
            if (!state.Armed)
                return;
            if (actor == null || !actor.IsActive() || actor.Index != state.ActorIndex)
            {
                state.Armed = false;
                return;
            }

            var body = BodyAlignedCleaveRuntime.GetBodyFacing(actor);
            var look = Normalize(actor.LookDirection);
            var bodyDot = Vec3.DotProduct(state.Facing, body);
            var lookDot = Vec3.DotProduct(state.Look, look);

            if (bodyDot < BodyFlipDotThreshold && lookDot > StableLookDotThreshold)
            {
                // Restore the exact pre-teleport body heading through the native movement-direction
                // channel. Unlike the old code, this never writes zero and never changes LookDirection.
                var restore = state.Facing.AsVec2;
                actor.SetMovementDirection(in restore);

                var mount = actor.MountAgent;
                if (mount != null && mount.IsActive() && mount.Index == state.MountIndex)
                {
                    var restoreMount = state.MountFacing.AsVec2;
                    mount.SetMovementDirection(in restoreMount);
                }

                var bodyDegrees = Math.Acos(Math.Max(-1f, Math.Min(1f, bodyDot))) * 180.0 / Math.PI;
                var lookDegrees = Math.Acos(Math.Max(-1f, Math.Min(1f, lookDot))) * 180.0 / Math.PI;
                state.Logger?.Debug(
                    state.Source + " corrected an independent post-teleport body flip; " +
                    "bodyDelta=" + bodyDegrees.ToString("0.0") + "deg, " +
                    "lookDelta=" + lookDegrees.ToString("0.0") + "deg, " +
                    "restored=" + Format(state.Facing) + ".");
                state.Armed = false;
                return;
            }

            if (MBCommon.GetApplicationTime() >= state.ExpiresAt)
            {
                state.Logger?.Debug(
                    state.Source + " post-teleport orientation remained stable; " +
                    "body=" + Format(body) + ", look=" + Format(look) + ".");
                state.Armed = false;
            }
        }

        private static Vec3 Normalize(Vec3 value)
        {
            value.z = 0f;
            if (value.Normalize() < 0.001f)
                value = Vec3.Forward;
            return value;
        }

        private static string Format(Vec3 value) =>
            "(" + value.x.ToString("0.00") + ", " + value.y.ToString("0.00") + ", " + value.z.ToString("0.00") + ")";

        internal readonly struct Snapshot
        {
            internal Snapshot(
                int actorIndex,
                Vec3 facing,
                Vec3 look,
                int mountIndex,
                Vec3 mountFacing,
                Vec3 position)
            {
                ActorIndex = actorIndex;
                Facing = facing;
                Look = look;
                MountIndex = mountIndex;
                MountFacing = mountFacing;
                Position = position;
            }

            internal int ActorIndex { get; }
            internal Vec3 Facing { get; }
            internal Vec3 Look { get; }
            internal int MountIndex { get; }
            internal Vec3 MountFacing { get; }
            internal Vec3 Position { get; }
        }
    }

    [HarmonyPatch(typeof(TorProxyCastStanceFix), "ReleaseBeforeVoidstepActivation")]
    internal static class DisableLegacyTorProxyReleasePatch
    {
        private static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(AbilityManager), "TeleportActor")]
    internal static class PositionOnlySharedTeleportPatch
    {
        private static bool Prefix(
            AbilityManager __instance,
            Agent actor,
            Vec3 position,
            AbilityContext ____context)
        {
            if (actor == null || !actor.IsActive())
                return false;

            var snapshot = PostTeleportOrientationGuard.Capture(actor);
            var mount = actor.MountAgent;
            if (mount != null && mount.IsActive())
            {
                mount.TeleportToPosition(position);
                actor.TeleportToPosition(position + Vec3.Up * 0.4f);
            }
            else
            {
                actor.TeleportToPosition(position);
            }

            // No MovementInputVector, SetMovementDirection, LookDirection, look-lock, action,
            // scripted-movement or facing mutation is performed on the normal teleport path.
            PostTeleportOrientationGuard.Arm(
                __instance,
                actor,
                snapshot,
                "Blink",
                ____context?.Logger);
            return false;
        }
    }

    [HarmonyPatch(typeof(AbilityManager), nameof(AbilityManager.Tick))]
    internal static class PostTeleportOrientationGuardTickPatch
    {
        private static void Prefix(AbilityManager __instance, AbilityContext ____context) =>
            PostTeleportOrientationGuard.BeforeManagerTick(__instance, ____context);

        private static void Postfix(AbilityManager __instance, AbilityContext ____context) =>
            PostTeleportOrientationGuard.AfterManagerTick(__instance, ____context);
    }

    [HarmonyPatch(typeof(VoidstepMissionBehavior), nameof(VoidstepMissionBehavior.EarlyStart))]
    internal static class TorProxyOrientationOwnershipInstallerPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            TorProxyOrientationOwnershipFix.Install();
        }
    }
}
