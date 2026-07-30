using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    // Retained as disabled documentation of the failed post-hoc restoration approach.
    // Restoring LookDirection after a native turn request cannot undo the visual turn.
    internal readonly struct CleaveFacingState
    {
        private CleaveFacingState(Agent actor, Vec3 actorFacing, Agent mount, Vec3 mountFacing)
        {
            Actor = actor;
            ActorFacing = actorFacing;
            Mount = mount;
            MountFacing = mountFacing;
        }

        private Agent Actor { get; }
        private Vec3 ActorFacing { get; }
        private Agent Mount { get; }
        private Vec3 MountFacing { get; }

        internal static CleaveFacingState Capture(Agent actor)
        {
            if (actor == null || !actor.IsActive())
                return default(CleaveFacingState);

            var mount = actor.MountAgent;
            if (mount != null && !mount.IsActive())
                mount = null;

            return new CleaveFacingState(
                actor,
                FluidCleaveRuntime.NormalizeFacing(actor.LookDirection),
                mount,
                mount != null ? FluidCleaveRuntime.NormalizeFacing(mount.LookDirection) : Vec3.Zero);
        }

        internal void Restore(VoidstepLogger logger, string stage)
        {
            if (Actor == null || !Actor.IsActive())
                return;

            try
            {
                if (Mount != null && Mount.IsActive())
                    Mount.LookDirection = MountFacing;
                Actor.LookDirection = ActorFacing;
            }
            catch (Exception ex)
            {
                logger?.Debug($"Cleave facing restoration failed during {stage}: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "TickVoidstep")]
    internal static class CleaveTickFacingGuardPatch
    {
        private static bool Prepare() => false;
        private static void Prefix(Agent player, out CleaveFacingState __state) =>
            __state = CleaveFacingState.Capture(player);
        private static Exception Finalizer(AbilityManager __instance, CleaveFacingState __state, Exception __exception)
        {
            __state.Restore(__instance?.Logger, "Cleave tick");
            return __exception;
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "CancelCurrent")]
    internal static class CleaveCancellationFacingGuardPatch
    {
        private static bool Prepare() => false;
        private static void Prefix(AbilityManager __instance, AbilityContext ____context, int ____castActorIndex, out CleaveFacingState __state)
        {
            __state = default(CleaveFacingState);
            if (__instance == null || !__instance.IsBusy || __instance.ActiveAbility != AbilityId.VoidstepCleave)
                return;
            var actor = ____context?.Player;
            if (actor != null && actor.Index == ____castActorIndex)
                __state = CleaveFacingState.Capture(actor);
        }
        private static Exception Finalizer(AbilityManager __instance, CleaveFacingState __state, Exception __exception)
        {
            __state.Restore(__instance?.Logger, "Cleave cancellation");
            return __exception;
        }
    }

    internal sealed class FluidCleaveCastState
    {
        internal readonly MBList<Agent> Nearby = new MBList<Agent>();
        internal Vec3 Facing = Vec3.Forward;
        internal Vec3 TargetPosition = Vec3.Invalid;
        internal int TargetIndex = -1;
        internal double StartAngle;
        internal VoidstepLogger Logger;
    }

    internal static class FluidCleaveRuntime
    {
        private const float LockHalfAngleDegrees = 20f;
        private const float TargetStandOff = 1.30f;
        private const float MinimumTargetAhead = 0.65f;
        private static readonly float[] BackOffsets = { 0f, 0.30f, 0.60f, 0.95f, 1.30f, 1.70f, 2.15f };
        private static readonly float[] SideOffsets = { 0f, 0.28f, -0.28f, 0.52f, -0.52f };
        private static readonly ConditionalWeakTable<AbilityManager, FluidCleaveCastState> States =
            new ConditionalWeakTable<AbilityManager, FluidCleaveCastState>();

        [ThreadStatic]
        private static AbilityManager _validationOwner;
        [ThreadStatic]
        private static bool _validationBypass;
        [ThreadStatic]
        private static FluidCleaveCastState _beginSweepState;
        [ThreadStatic]
        private static int _facingSuppressionDepth;

        internal static bool FacingWritesSuppressed => _facingSuppressionDepth > 0;
        internal static FluidCleaveCastState BeginSweepState => _beginSweepState;
        internal static AbilityManager ValidationOwner => _validationOwner;
        internal static bool ValidationBypass => _validationBypass;

        internal static FluidCleaveCastState Get(AbilityManager manager)
        {
            if (manager == null) return null;
            return States.GetValue(manager, _ => new FluidCleaveCastState());
        }

        internal static void Clear(AbilityManager manager)
        {
            if (manager != null)
                States.Remove(manager);
        }

        internal static void EnterValidation(AbilityManager manager) => _validationOwner = manager;
        internal static void ExitValidation(AbilityManager manager)
        {
            if (ReferenceEquals(_validationOwner, manager))
                _validationOwner = null;
        }
        internal static void EnterValidationBypass() => _validationBypass = true;
        internal static void ExitValidationBypass() => _validationBypass = false;
        internal static void EnterBeginSweep(FluidCleaveCastState state) => _beginSweepState = state;
        internal static void ExitBeginSweep() => _beginSweepState = null;
        internal static void EnterFacingSuppression() => _facingSuppressionDepth++;
        internal static void ExitFacingSuppression()
        {
            if (_facingSuppressionDepth > 0)
                _facingSuppressionDepth--;
        }

        internal static Vec3 NormalizeFacing(Vec3 facing)
        {
            facing.z = 0f;
            if (facing.Normalize() < 0.001f)
                facing = Vec3.Forward;
            return facing;
        }

        internal static Vec3 ResolveRequested(
            AbilityManager manager,
            AbilityContext context,
            Agent player,
            float range)
        {
            var state = Get(manager);
            if (state == null || context?.Mission == null || player == null)
                return player != null ? player.Position : Vec3.Invalid;

            state.Logger = context.Logger;
            state.Facing = NormalizeFacing(player.LookDirection);
            var target = FindFacingTarget(context.Mission, player, state.Facing, range, state.Nearby);
            state.TargetIndex = target != null ? target.Index : -1;
            state.TargetPosition = target != null ? target.Position : Vec3.Invalid;

            if (target == null)
                return player.Position + state.Facing * Math.Min(range, 5f);

            var toTarget = target.Position - player.Position;
            toTarget.z = 0f;
            var forwardDistance = Vec3.DotProduct(toTarget, state.Facing);
            if (forwardDistance <= TargetStandOff + 0.15f)
                return player.Position;

            var requested = target.Position - state.Facing * TargetStandOff;
            var travel = requested - player.Position;
            travel.z = 0f;
            var travelDistance = travel.Length;
            if (travelDistance > range && travelDistance > 0.001f)
                requested = player.Position + travel * (range / travelDistance);
            return requested;
        }

        internal static TeleportValidationResult ValidateDirectional(
            TeleportValidator validator,
            Agent actor,
            Vec3 requested,
            float maximumRange,
            bool allowThroughWalls,
            FluidCleaveCastState state)
        {
            if (validator == null || actor == null || state == null)
                return new TeleportValidationResult(false, Vec3.Invalid, "Cleave validation state was unavailable.", false);

            var displacement = requested - actor.Position;
            displacement.z = 0f;
            if (displacement.Length <= 0.08f)
                return new TeleportValidationResult(true, actor.Position, null, false);

            var facing = NormalizeFacing(state.Facing);
            var right = new Vec3(-facing.y, facing.x, 0f, 0f);
            var targetPosition = ResolveTargetPosition(actor, state);
            var hasTarget = targetPosition.IsValid;
            string lastReason = null;

            for (var backIndex = 0; backIndex < BackOffsets.Length; backIndex++)
            {
                for (var sideIndex = 0; sideIndex < SideOffsets.Length; sideIndex++)
                {
                    var candidate = requested - facing * BackOffsets[backIndex] + right * SideOffsets[sideIndex];
                    var fromActor = candidate - actor.Position;
                    fromActor.z = 0f;
                    if (Vec3.DotProduct(fromActor, facing) < -0.05f)
                        continue;
                    if (Math.Abs(Vec3.DotProduct(fromActor, right)) > 2.25f)
                        continue;
                    if (hasTarget)
                    {
                        var targetAhead = targetPosition - candidate;
                        targetAhead.z = 0f;
                        if (Vec3.DotProduct(targetAhead, facing) < MinimumTargetAhead)
                            continue;
                    }

                    TeleportValidationResult result;
                    try
                    {
                        EnterValidationBypass();
                        result = validator.Validate(actor, candidate, maximumRange, allowThroughWalls, 1);
                    }
                    finally
                    {
                        ExitValidationBypass();
                    }

                    if (result.Success)
                        return new TeleportValidationResult(true, result.Position, null, backIndex != 0 || sideIndex != 0);
                    lastReason = result.Reason;
                }
            }

            return new TeleportValidationResult(false, Vec3.Invalid, lastReason ?? "No facing-aligned Cleave destination was found.", false);
        }

        internal static void PrepareSweep(FluidCleaveCastState state, CleaveExecutionSnapshot snapshot)
        {
            if (state == null) return;
            var facingAngle = AngleMath.NormalizeRadians(Math.Atan2(state.Facing.y, state.Facing.x));
            state.StartAngle = AngleMath.NormalizeRadians(
                facingAngle - (int)snapshot.Direction * snapshot.SweepRadians * 0.5);
        }

        internal static void TeleportPositionOnly(Agent actor, Vec3 position)
        {
            if (actor == null || !actor.IsActive()) return;
            var delta = position - actor.Position;
            delta.z = 0f;
            if (delta.Length <= 0.08f) return;

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
        }

        private static Agent FindFacingTarget(
            Mission mission,
            Agent player,
            Vec3 facing,
            float range,
            MBList<Agent> nearby)
        {
            if (mission == null || player?.Team == null) return null;
            nearby.Clear();
            mission.GetNearbyEnemyAgents(player.Position.AsVec2, range, player.Team, nearby);
            var minimumDot = (float)Math.Cos(LockHalfAngleDegrees * Math.PI / 180.0);
            Agent best = null;
            var bestScore = float.MaxValue;
            for (var i = 0; i < nearby.Count; i++)
            {
                var candidate = nearby[i];
                if (!TargetingService.IsUsableTarget(player, candidate, true)) continue;
                var delta = candidate.GetChestGlobalPosition() - player.GetChestGlobalPosition();
                delta.z = 0f;
                var distance = delta.Normalize();
                if (distance <= 0.001f) continue;
                var dot = Vec3.DotProduct(facing, delta);
                if (dot < minimumDot) continue;
                var score = distance * (2f - dot);
                if (score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        private static Vec3 ResolveTargetPosition(Agent actor, FluidCleaveCastState state)
        {
            if (actor?.Mission != null && state.TargetIndex >= 0)
            {
                var target = actor.Mission.FindAgentWithIndex(state.TargetIndex);
                if (target != null && target.IsActive() && target.Health > 0f)
                    return target.Position;
            }
            return state.TargetPosition;
        }
    }

    internal static class NativeCleavePresentation
    {
        private sealed class PresentationState
        {
            internal string Direction;
            internal bool Released;
            internal VoidstepLogger Logger;
        }

        private static readonly Dictionary<int, PresentationState> Active = new Dictionary<int, PresentationState>();
        private static readonly object ResolveLock = new object();
        private static MethodInfo _setEventControlFlags;
        private static Type _eventFlagType;
        private static bool _resolved;
        private static bool _missingLogged;

        internal static void Begin(Agent actor, bool clockwise, VoidstepLogger logger)
        {
            if (actor == null || !actor.IsActive()) return;
            var state = new PresentationState
            {
                // AttackLeft swings from the actor's left through the forward line,
                // matching a clockwise arc when viewed from above.
                Direction = clockwise ? "AttackLeft" : "AttackRight",
                Logger = logger
            };
            Active[actor.Index] = state;
            TrySet(actor, state, false);
        }

        internal static void Tick(Agent actor, float progress)
        {
            if (actor == null || !actor.IsActive() || !Active.TryGetValue(actor.Index, out var state))
                return;
            if (progress < 0.10f)
            {
                TrySet(actor, state, false);
                return;
            }
            if (!state.Released)
            {
                TrySet(actor, state, true);
                state.Released = true;
            }
        }

        internal static void End(Agent actor)
        {
            if (actor != null)
                Active.Remove(actor.Index);
        }

        private static bool TrySet(Agent actor, PresentationState state, bool release)
        {
            Resolve(state.Logger);
            if (_setEventControlFlags == null || _eventFlagType == null)
                return false;
            try
            {
                ulong bits = Convert.ToUInt64(Enum.Parse(_eventFlagType, state.Direction, false));
                if (release)
                    bits |= Convert.ToUInt64(Enum.Parse(_eventFlagType, "AttackRelease", false));
                var flags = Enum.ToObject(_eventFlagType, bits);
                _setEventControlFlags.Invoke(actor, new[] { flags });
                return true;
            }
            catch (Exception ex)
            {
                state.Logger?.Debug("Native Cleave attack presentation was unavailable: " + ex.GetBaseException().Message);
                return false;
            }
        }

        private static void Resolve(VoidstepLogger logger)
        {
            if (_resolved) return;
            lock (ResolveLock)
            {
                if (_resolved) return;
                var methods = typeof(Agent).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (var i = 0; i < methods.Length; i++)
                {
                    var method = methods[i];
                    if (!string.Equals(method.Name, "SetEventControlFlags", StringComparison.Ordinal)) continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum) continue;
                    _setEventControlFlags = method;
                    _eventFlagType = parameters[0].ParameterType;
                    break;
                }
                _resolved = true;
                if (_setEventControlFlags == null && !_missingLogged)
                {
                    _missingLogged = true;
                    logger?.Debug("Native Cleave attack presentation method was not found; using the aligned particle arc only.");
                }
            }
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "BeginVoidstep")]
    internal static class FluidCleaveBeginScopePatch
    {
        private static void Prefix(AbilityManager __instance, AbilityContext ____context, Agent player)
        {
            var state = FluidCleaveRuntime.Get(__instance);
            if (state != null)
            {
                state.Logger = ____context?.Logger;
                state.Facing = FluidCleaveRuntime.NormalizeFacing(player != null ? player.LookDirection : Vec3.Forward);
            }
            FluidCleaveRuntime.EnterValidation(__instance);
        }

        private static Exception Finalizer(AbilityManager __instance, Exception __exception)
        {
            FluidCleaveRuntime.ExitValidation(__instance);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "ResolveVoidstepDestination")]
    internal static class FluidCleaveDestinationPatch
    {
        private static bool Prefix(
            AbilityManager __instance,
            AbilityContext ____context,
            Agent player,
            float range,
            ref Vec3 __result)
        {
            __result = FluidCleaveRuntime.ResolveRequested(__instance, ____context, player, range);
            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportValidator), nameof(TeleportValidator.Validate))]
    internal static class FluidCleaveValidationPatch
    {
        private static bool Prefix(
            TeleportValidator __instance,
            Agent actor,
            Vec3 requested,
            float maximumRange,
            bool allowThroughWalls,
            ref TeleportValidationResult __result)
        {
            if (FluidCleaveRuntime.ValidationBypass) return true;
            var owner = FluidCleaveRuntime.ValidationOwner;
            if (owner == null) return true;
            var state = FluidCleaveRuntime.Get(owner);
            __result = FluidCleaveRuntime.ValidateDirectional(
                __instance,
                actor,
                requested,
                maximumRange,
                allowThroughWalls,
                state);
            return false;
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "TickVoidstep")]
    internal static class FluidCleaveTickPatch
    {
        private const float WindUpSeconds = 0.10f;
        private const float RecoverySeconds = 0.06f;
        private static readonly MethodInfo RollbackPayment = AccessTools.Method(typeof(AbilityManager), "RollbackPayment");
        private static readonly MethodInfo Fail = AccessTools.Method(typeof(AbilityManager), "Fail");
        private static readonly MethodInfo CancelCurrent = AccessTools.Method(typeof(AbilityManager), "CancelCurrent");
        private static readonly MethodInfo CompleteCurrent = AccessTools.Method(typeof(AbilityManager), "CompleteCurrent");
        private static readonly MethodInfo RemoveCleaveMarker = AccessTools.Method(typeof(AbilityManager), "RemoveCleaveMarker");

        private static bool Prefix(
            AbilityManager __instance,
            Agent player,
            float dt,
            AbilityContext ____context,
            CastStateMachine ____state,
            ref CastToken ____token,
            TeleportValidator ____teleportValidator,
            EffectController ____effects,
            CleaveSweepController ____cleave,
            MissionWeapon ____cleaveWeapon,
            ref CleaveExecutionSnapshot ____cleaveSnapshot,
            ref Vec3 ____destination)
        {
            ____state.Tick(____token, Math.Max(0f, dt));
            switch (____state.Phase)
            {
                case AbilityPhase.WindUp:
                    if (____state.PhaseElapsed < WindUpSeconds)
                        return false;

                    var state = FluidCleaveRuntime.Get(__instance);
                    state.Facing = FluidCleaveRuntime.NormalizeFacing(player.LookDirection);
                    var requested = FluidCleaveRuntime.ResolveRequested(
                        __instance,
                        ____context,
                        player,
                        ____cleaveSnapshot.TeleportRange);
                    var validation = FluidCleaveRuntime.ValidateDirectional(
                        ____teleportValidator,
                        player,
                        requested,
                        ____cleaveSnapshot.TeleportRange,
                        false,
                        state);
                    if (!validation.Success)
                    {
                        RollbackPayment?.Invoke(__instance, new object[] { AbilityId.VoidstepCleave });
                        Fail?.Invoke(__instance, new object[] { validation.Reason ?? "No facing-aligned Cleave destination was found." });
                        CancelCurrent?.Invoke(__instance, new object[] { CancelReason.InvalidDestination });
                        return false;
                    }

                    ____destination = validation.Position;
                    ____effects.Departure(player.Position);
                    ____state.Transition(____token, AbilityPhase.Departing);
                    FluidCleaveRuntime.TeleportPositionOnly(player, ____destination);
                    RemoveCleaveMarker?.Invoke(__instance, null);
                    ____effects.Arrival(player.Position);
                    ____effects.PlaySound("event:/mission/combat/swing/weapon_swing", player.Position);
                    ____state.Transition(____token, AbilityPhase.Teleporting);
                    ____state.Transition(____token, AbilityPhase.Arriving);

                    FluidCleaveRuntime.PrepareSweep(state, ____cleaveSnapshot);
                    FluidCleaveRuntime.EnterBeginSweep(state);
                    try
                    {
                        if (!____cleave.Begin(player, ____cleaveWeapon, ____cleaveSnapshot, out var failure))
                        {
                            RollbackPayment?.Invoke(__instance, new object[] { AbilityId.VoidstepCleave });
                            Fail?.Invoke(__instance, new object[] { failure ?? "Cleave execution could not start." });
                            CancelCurrent?.Invoke(__instance, new object[] { CancelReason.Interrupted });
                            return false;
                        }
                    }
                    finally
                    {
                        FluidCleaveRuntime.ExitBeginSweep();
                    }
                    ____context.Logger.Debug(
                        $"Fluid Cleave started facing={Format(state.Facing)}, destination={Format(____destination)}, " +
                        $"target={state.TargetIndex}, directionalFallback={validation.UsedFallback}.");
                    ____state.Transition(____token, AbilityPhase.Active);
                    return false;

                case AbilityPhase.Active:
                    if (____cleave.Tick(dt))
                    {
                        ____context.Logger.Debug($"Fluid Cleave active phase finished; hits={____cleave.SuccessfulHits}.");
                        ____state.Transition(____token, AbilityPhase.Recovery);
                    }
                    return false;

                case AbilityPhase.Recovery:
                    if (____state.PhaseElapsed >= RecoverySeconds)
                    {
                        NativeCleavePresentation.End(player);
                        CompleteCurrent?.Invoke(__instance, null);
                        FluidCleaveRuntime.Clear(__instance);
                    }
                    return false;

                default:
                    return false;
            }
        }

        private static string Format(Vec3 value) =>
            $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";
    }

    [HarmonyPatch(typeof(CleaveExecutionSnapshot), "get_StartAngle")]
    internal static class FluidCleaveStartAnglePatch
    {
        private static void Postfix(ref double __result)
        {
            var state = FluidCleaveRuntime.BeginSweepState;
            if (state != null)
                __result = state.StartAngle;
        }
    }

    [HarmonyPatch(typeof(CleaveSweepController), nameof(CleaveSweepController.Begin))]
    internal static class FluidCleaveBeginPresentationPatch
    {
        private static void Prefix(
            Agent actor,
            CleaveExecutionSnapshot snapshot,
            VoidstepLogger ____logger)
        {
            FluidCleaveRuntime.EnterFacingSuppression();
            NativeCleavePresentation.Begin(actor, snapshot.Clockwise, ____logger);
        }

        private static void Postfix(
            ref float ____duration,
            ref float ____trailAccumulator,
            ref int ____trailBursts,
            bool __result)
        {
            if (__result)
            {
                ____duration = 0.46f;
                ____trailAccumulator = 0f;
                // Reserve 0-11 so the legacy straight-ahead trail path remains disabled.
                ____trailBursts = 12;
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            FluidCleaveRuntime.ExitFacingSuppression();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CleaveSweepController), nameof(CleaveSweepController.Tick))]
    internal static class FluidCleaveArcPresentationPatch
    {
        private const int FirstArcBurst = 12;
        private const int LastArcBurst = 30;

        private static void Prefix(
            float dt,
            Agent ____actor,
            float ____elapsed,
            float ____duration,
            double ____startAngle,
            double ____sweepRadians,
            SweepDirection ____direction,
            float ____radius,
            EffectController ____effects,
            ref int ____trailBursts,
            out Agent __state)
        {
            __state = ____actor;
            FluidCleaveRuntime.EnterFacingSuppression();
            if (____actor == null || !____actor.IsActive() || ____duration <= 0f)
                return;

            var progress = Math.Min(1f, (____elapsed + Math.Max(0f, dt)) / ____duration);
            NativeCleavePresentation.Tick(____actor, progress);
            var emitted = 0;
            while (____trailBursts < LastArcBurst && emitted < 3)
            {
                var sample = (____trailBursts - FirstArcBurst + 1f) / (LastArcBurst - FirstArcBurst);
                if (progress + 0.0001f < sample) break;
                var eased = sample * sample * (3f - 2f * sample);
                var angle = ____startAngle + (int)____direction * ____sweepRadians * eased;
                var direction = new Vec3((float)Math.Cos(angle), (float)Math.Sin(angle), 0f, 0f);
                var visualRadius = Math.Min(2.65f, Math.Max(1.15f, ____radius * 0.42f));
                var center = ____actor.GetChestGlobalPosition();
                ____effects.WeaponTrail(center + direction * visualRadius);
                if (((____trailBursts - FirstArcBurst) % 3) == 0)
                    ____effects.WeaponTrail(center + direction * (visualRadius * 0.66f));
                ____trailBursts++;
                emitted++;
            }
        }

        private static void Postfix(bool __result, Agent __state)
        {
            if (__result)
                NativeCleavePresentation.End(__state);
        }

        private static Exception Finalizer(Exception __exception)
        {
            FluidCleaveRuntime.ExitFacingSuppression();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(AnimationController), nameof(AnimationController.BeginCleave))]
    internal static class CleaveUnsafeActionSuppressionPatch
    {
        private static bool Prefix(AnimationController __instance, Agent actor)
        {
            // The fluid presentation uses Bannerlord's native attack input path instead of
            // an unrelated heavy-thrown/command action with manually forced progress.
            __instance?.ResetActionSpeed(actor);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(AnimationController),
        nameof(AnimationController.SetActorFacing),
        new Type[] { typeof(Agent), typeof(Vec3) })]
    internal static class FluidCleaveVectorFacingSuppressionPatch
    {
        private static bool Prefix() => !FluidCleaveRuntime.FacingWritesSuppressed;
    }

    [HarmonyPatch(
        typeof(AnimationController),
        nameof(AnimationController.SetActorFacing),
        new Type[] { typeof(Agent), typeof(double) })]
    internal static class FluidCleaveAngleFacingSuppressionPatch
    {
        private static bool Prefix() => !FluidCleaveRuntime.FacingWritesSuppressed;
    }

    [HarmonyPatch(typeof(AbilityManager), "BeginFovPulse")]
    internal static class FluidCleaveFovSuppressionPatch
    {
        private static bool Prefix(AbilityManager __instance) =>
            __instance == null || __instance.ActiveAbility != AbilityId.VoidstepCleave;
    }

    [HarmonyPatch(typeof(AbilityManager), "CancelCurrent")]
    internal static class FluidCleaveCancellationPatch
    {
        private static void Prefix(
            AbilityManager __instance,
            ref Vec3 ____castOriginalLook,
            AbilityContext ____context)
        {
            if (__instance == null || !__instance.IsBusy || __instance.ActiveAbility != AbilityId.VoidstepCleave)
                return;
            ____castOriginalLook = Vec3.Zero;
            NativeCleavePresentation.End(____context?.Player);
        }

        private static void Postfix(AbilityManager __instance)
        {
            FluidCleaveRuntime.Clear(__instance);
        }
    }

    [HarmonyPatch(
        typeof(AbilityManager),
        "TeleportActor",
        new Type[] { typeof(Agent), typeof(Vec3), typeof(bool) })]
    internal static class OrientationNeutralTeleportPatch
    {
        private static bool Prefix(
            AbilityManager __instance,
            Agent actor,
            Vec3 position,
            bool preserveMomentum)
        {
            if (actor == null || !actor.IsActive())
                return false;

            var before = actor.LookDirection;
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

            if (!preserveMomentum)
            {
                actor.MovementInputVector = Vec2.Zero;
                if (mount != null && mount.IsActive())
                    mount.MovementInputVector = Vec2.Zero;
            }

            __instance?.Logger.Debug(
                $"Orientation-neutral teleport position={Format(position)}, " +
                $"lookBefore={Format(before)}, lookAfter={Format(actor.LookDirection)}, " +
                $"movementDirectionWasNotOverwritten=true.");
            return false;
        }

        private static string Format(Vec3 value) =>
            $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";
    }

    [HarmonyPatch(typeof(VoidstepAbilityEffects), nameof(VoidstepAbilityEffects.VoidCleave))]
    internal static class FluidCleaveInitialEffectPatch
    {
        private static bool Prefix(EffectController effects, Vec3 center, float cleaveRadius)
        {
            if (effects == null || VoidstepSettings.Current.EffectIntensity <= 0f)
                return false;
            var state = FluidCleaveRuntime.BeginSweepState;
            var facing = state != null ? state.Facing : Vec3.Forward;
            facing = FluidCleaveRuntime.NormalizeFacing(facing);
            var right = new Vec3(-facing.y, facing.x, 0f, 0f);
            var radius = Math.Min(2.4f, Math.Max(1.1f, cleaveRadius * 0.36f));
            effects.Arrival(center + Vec3.Up * 0.35f);
            effects.WeaponTrail(center + Vec3.Up * 0.85f + facing * radius);
            effects.WeaponTrail(center + Vec3.Up * 0.82f + facing * (radius * 0.82f) + right * 0.42f);
            effects.WeaponTrail(center + Vec3.Up * 0.82f + facing * (radius * 0.82f) - right * 0.42f);
            return false;
        }
    }
}