using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class BodyAlignedCleaveState
    {
        internal readonly MBList<Agent> Nearby = new MBList<Agent>();
        internal Vec3 Facing = Vec3.Forward;
        internal Vec3 TargetPosition = Vec3.Invalid;
        internal int TargetIndex = -1;
        internal int ActorIndex = -1;
        internal double StartAngle;
        internal int VisualBurstIndex;
        internal bool ForwardBurstPlayed;
        internal VoidstepLogger Logger;
    }

    internal static class BodyAlignedCleaveRuntime
    {
        private const float TargetStandOff = 1.30f;
        private const float DefaultTravelDistance = 4.25f;
        private const float MinimumForwardTargetDistance = 0.55f;
        private const float MaximumTargetLateralOffset = 1.10f;
        private const float ValidationStep = 0.35f;
        private const float MinimumTeleportDistance = 0.10f;

        private static readonly ConditionalWeakTable<AbilityManager, BodyAlignedCleaveState> States =
            new ConditionalWeakTable<AbilityManager, BodyAlignedCleaveState>();
        private static readonly Dictionary<int, BodyAlignedCleaveState> ActiveActors =
            new Dictionary<int, BodyAlignedCleaveState>();

        [ThreadStatic]
        private static AbilityManager _validationOwner;
        [ThreadStatic]
        private static bool _validationBypass;
        [ThreadStatic]
        private static BodyAlignedCleaveState _beginSweepState;
        [ThreadStatic]
        private static int _facingSuppressionDepth;

        internal static bool FacingWritesSuppressed => _facingSuppressionDepth > 0;
        internal static bool ValidationBypass => _validationBypass;
        internal static AbilityManager ValidationOwner => _validationOwner;
        internal static BodyAlignedCleaveState BeginSweepState => _beginSweepState;

        internal static BodyAlignedCleaveState Get(AbilityManager manager)
        {
            if (manager == null) return null;
            return States.GetValue(manager, _ => new BodyAlignedCleaveState());
        }

        internal static BodyAlignedCleaveState GetForActor(Agent actor)
        {
            if (actor == null) return null;
            ActiveActors.TryGetValue(actor.Index, out var state);
            return state;
        }

        internal static void Clear(AbilityManager manager)
        {
            if (manager == null) return;
            if (States.TryGetValue(manager, out var state) && state.ActorIndex >= 0)
                ActiveActors.Remove(state.ActorIndex);
            States.Remove(manager);
        }

        internal static void BindActor(BodyAlignedCleaveState state, Agent actor)
        {
            if (state == null || actor == null) return;
            if (state.ActorIndex >= 0 && state.ActorIndex != actor.Index)
                ActiveActors.Remove(state.ActorIndex);
            state.ActorIndex = actor.Index;
            ActiveActors[actor.Index] = state;
        }

        internal static void EnterValidation(AbilityManager manager) => _validationOwner = manager;
        internal static void ExitValidation(AbilityManager manager)
        {
            if (ReferenceEquals(_validationOwner, manager))
                _validationOwner = null;
        }

        internal static void EnterValidationBypass() => _validationBypass = true;
        internal static void ExitValidationBypass() => _validationBypass = false;
        internal static void EnterBeginSweep(BodyAlignedCleaveState state) => _beginSweepState = state;
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

        // LookDirection follows aim/camera state and is not guaranteed to match the visible
        // body yaw. Cleave is grounded in the rendered/native body frame instead.
        internal static Vec3 GetBodyFacing(Agent actor)
        {
            if (actor == null) return Vec3.Forward;

            try
            {
                var visuals = actor.AgentVisuals;
                if (visuals != null && visuals.IsValid())
                {
                    var visualForward = visuals.GetGlobalFrame().rotation.f;
                    visualForward.z = 0f;
                    if (visualForward.Normalize() >= 0.001f)
                        return visualForward;
                }
            }
            catch
            {
            }

            try
            {
                var frameForward = actor.Frame.rotation.f;
                frameForward.z = 0f;
                if (frameForward.Normalize() >= 0.001f)
                    return frameForward;
            }
            catch
            {
            }

            return NormalizeFacing(actor.LookDirection);
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
            state.Facing = GetBodyFacing(player);
            var target = FindBodyAlignedTarget(context.Mission, player, state.Facing, range, state.Nearby);
            state.TargetIndex = target != null ? target.Index : -1;
            state.TargetPosition = target != null ? target.Position : Vec3.Invalid;

            var travelDistance = Math.Min(Math.Max(0f, range), DefaultTravelDistance);
            if (target != null)
            {
                var toTarget = target.Position - player.Position;
                toTarget.z = 0f;
                var forwardDistance = Vec3.DotProduct(toTarget, state.Facing);
                travelDistance = Math.Max(0f, Math.Min(range, forwardDistance - TargetStandOff));
            }

            return player.Position + state.Facing * travelDistance;
        }

        internal static TeleportValidationResult ValidateOnFacingAxis(
            TeleportValidator validator,
            Agent actor,
            Vec3 requested,
            float maximumRange,
            bool allowThroughWalls,
            BodyAlignedCleaveState state)
        {
            if (validator == null || actor == null || state == null)
                return new TeleportValidationResult(false, Vec3.Invalid, "Cleave validation state was unavailable.", false);

            var facing = NormalizeFacing(state.Facing);
            var requestedDelta = requested - actor.Position;
            requestedDelta.z = 0f;
            var requestedDistance = Math.Max(0f, Math.Min(maximumRange, Vec3.DotProduct(requestedDelta, facing)));
            string lastReason = null;

            for (var distance = requestedDistance; distance >= MinimumTeleportDistance; distance -= ValidationStep)
            {
                var candidate = actor.Position + facing * distance;
                TeleportValidationResult result;
                try
                {
                    EnterValidationBypass();
                    // A budget of one tests the exact candidate without accepting the validator's
                    // unrestricted radial fallback ring.
                    result = validator.Validate(actor, candidate, maximumRange, allowThroughWalls, 1);
                }
                finally
                {
                    ExitValidationBypass();
                }

                if (result.Success)
                    return new TeleportValidationResult(true, result.Position, null, distance + 0.01f < requestedDistance);
                lastReason = result.Reason;
            }

            // A blocked dash degrades to an in-place Cleave rather than side-stepping,
            // crossing the target, cancelling the cast, or changing the alignment axis.
            return new TeleportValidationResult(true, actor.Position, lastReason, requestedDistance > MinimumTeleportDistance);
        }

        internal static void PrepareSweep(BodyAlignedCleaveState state, CleaveExecutionSnapshot snapshot, Agent actor)
        {
            if (state == null) return;
            state.Facing = GetBodyFacing(actor);
            var facingAngle = AngleMath.NormalizeRadians(Math.Atan2(state.Facing.y, state.Facing.x));
            state.StartAngle = AngleMath.NormalizeRadians(
                facingAngle - (int)snapshot.Direction * snapshot.SweepRadians * 0.5);
            state.VisualBurstIndex = 0;
            state.ForwardBurstPlayed = false;
            BindActor(state, actor);
        }

        internal static void TeleportPositionOnly(Agent actor, Vec3 position)
        {
            if (actor == null || !actor.IsActive()) return;
            var delta = position - actor.Position;
            delta.z = 0f;
            if (delta.Length <= MinimumTeleportDistance) return;

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

        internal static void EmitArc(
            BodyAlignedCleaveState state,
            Agent actor,
            EffectController effects,
            float radius,
            double sweepRadians,
            SweepDirection direction,
            float progress)
        {
            if (state == null || actor == null || effects == null || !actor.IsActive()) return;
            progress = Math.Max(0f, Math.Min(1f, progress));

            const int totalSamples = 30;
            var emittedThisTick = 0;
            while (state.VisualBurstIndex < totalSamples && emittedThisTick < 5)
            {
                var sample = (state.VisualBurstIndex + 1f) / totalSamples;
                if (sample > progress + 0.0001f) break;

                var eased = sample * sample * (3f - 2f * sample);
                var angle = state.StartAngle + (int)direction * sweepRadians * eased;
                var arcDirection = new Vec3((float)Math.Cos(angle), (float)Math.Sin(angle), 0f, 0f);
                var reach = Math.Min(3.15f, Math.Max(1.25f, radius * 0.46f));
                var center = actor.GetChestGlobalPosition() + Vec3.Up * 0.05f;
                effects.WeaponTrail(center + arcDirection * reach);
                if ((state.VisualBurstIndex & 1) == 0)
                    effects.WeaponTrail(center + arcDirection * (reach * 0.68f));
                state.VisualBurstIndex++;
                emittedThisTick++;
            }

            // The exact forward crossing occurs halfway through a centred sweep. Reinforce it
            // so the visual strike and the frontal target hit read on the body's facing axis.
            if (!state.ForwardBurstPlayed && progress >= 0.5f)
            {
                state.ForwardBurstPlayed = true;
                var forward = NormalizeFacing(state.Facing);
                var right = new Vec3(-forward.y, forward.x, 0f, 0f);
                var reach = Math.Min(3.25f, Math.Max(1.35f, radius * 0.48f));
                var center = actor.GetChestGlobalPosition() + Vec3.Up * 0.12f;
                effects.WeaponTrail(center + forward * reach);
                effects.WeaponTrail(center + forward * (reach * 0.84f) + right * 0.32f);
                effects.WeaponTrail(center + forward * (reach * 0.84f) - right * 0.32f);
            }
        }

        internal static string DescribeFacing(Agent actor, Vec3 bodyFacing)
        {
            var look = actor != null ? NormalizeFacing(actor.LookDirection) : Vec3.Forward;
            var dot = Math.Max(-1f, Math.Min(1f, Vec3.DotProduct(bodyFacing, look)));
            var difference = Math.Acos(dot) * 180.0 / Math.PI;
            return $"body={Format(bodyFacing)}, look={Format(look)}, bodyLookDifference={difference:0.0}deg";
        }

        private static Agent FindBodyAlignedTarget(
            Mission mission,
            Agent player,
            Vec3 facing,
            float range,
            MBList<Agent> nearby)
        {
            if (mission == null || player?.Team == null) return null;
            nearby.Clear();
            mission.GetNearbyEnemyAgents(player.Position.AsVec2, range, player.Team, nearby);
            var right = new Vec3(-facing.y, facing.x, 0f, 0f);
            Agent best = null;
            var bestScore = float.MaxValue;

            for (var i = 0; i < nearby.Count; i++)
            {
                var candidate = nearby[i];
                if (!TargetingService.IsUsableTarget(player, candidate, true)) continue;
                var delta = candidate.GetChestGlobalPosition() - player.GetChestGlobalPosition();
                delta.z = 0f;
                var forwardDistance = Vec3.DotProduct(delta, facing);
                if (forwardDistance < MinimumForwardTargetDistance || forwardDistance > range) continue;
                var lateral = Math.Abs(Vec3.DotProduct(delta, right));
                if (lateral > MaximumTargetLateralOffset) continue;
                var score = forwardDistance + lateral * 2.5f;
                if (score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        private static string Format(Vec3 value) =>
            $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";
    }

    [HarmonyPatch(typeof(AbilityManager), "BeginVoidstep")]
    internal static class BodyAlignedCleaveBeginScopePatch
    {
        private static void Prefix(AbilityManager __instance, AbilityContext ____context, Agent player)
        {
            var state = BodyAlignedCleaveRuntime.Get(__instance);
            if (state != null)
            {
                state.Logger = ____context?.Logger;
                state.Facing = BodyAlignedCleaveRuntime.GetBodyFacing(player);
            }
            BodyAlignedCleaveRuntime.EnterValidation(__instance);
        }

        private static Exception Finalizer(AbilityManager __instance, Exception __exception)
        {
            BodyAlignedCleaveRuntime.ExitValidation(__instance);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "ResolveVoidstepDestination")]
    internal static class BodyAlignedCleaveDestinationPatch
    {
        private static bool Prefix(
            AbilityManager __instance,
            AbilityContext ____context,
            Agent player,
            float range,
            ref Vec3 __result)
        {
            __result = BodyAlignedCleaveRuntime.ResolveRequested(__instance, ____context, player, range);
            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportValidator), nameof(TeleportValidator.Validate))]
    internal static class BodyAlignedCleaveValidationPatch
    {
        private static bool Prefix(
            TeleportValidator __instance,
            Agent actor,
            Vec3 requested,
            float maximumRange,
            bool allowThroughWalls,
            ref TeleportValidationResult __result)
        {
            if (BodyAlignedCleaveRuntime.ValidationBypass) return true;
            var owner = BodyAlignedCleaveRuntime.ValidationOwner;
            if (owner == null) return true;
            __result = BodyAlignedCleaveRuntime.ValidateOnFacingAxis(
                __instance,
                actor,
                requested,
                maximumRange,
                allowThroughWalls,
                BodyAlignedCleaveRuntime.Get(owner));
            return false;
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "TickVoidstep")]
    internal static class BodyAlignedCleaveTickPatch
    {
        private const float WindUpSeconds = 0.045f;
        private const float RecoverySeconds = 0.035f;
        private static readonly System.Reflection.MethodInfo RollbackPayment = AccessTools.Method(typeof(AbilityManager), "RollbackPayment");
        private static readonly System.Reflection.MethodInfo Fail = AccessTools.Method(typeof(AbilityManager), "Fail");
        private static readonly System.Reflection.MethodInfo CancelCurrent = AccessTools.Method(typeof(AbilityManager), "CancelCurrent");
        private static readonly System.Reflection.MethodInfo CompleteCurrent = AccessTools.Method(typeof(AbilityManager), "CompleteCurrent");
        private static readonly System.Reflection.MethodInfo RemoveCleaveMarker = AccessTools.Method(typeof(AbilityManager), "RemoveCleaveMarker");

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

                    var state = BodyAlignedCleaveRuntime.Get(__instance);
                    state.Facing = BodyAlignedCleaveRuntime.GetBodyFacing(player);
                    var requested = BodyAlignedCleaveRuntime.ResolveRequested(
                        __instance,
                        ____context,
                        player,
                        ____cleaveSnapshot.TeleportRange);
                    var validation = BodyAlignedCleaveRuntime.ValidateOnFacingAxis(
                        ____teleportValidator,
                        player,
                        requested,
                        ____cleaveSnapshot.TeleportRange,
                        false,
                        state);
                    if (!validation.Success)
                    {
                        RollbackPayment?.Invoke(__instance, new object[] { AbilityId.VoidstepCleave });
                        Fail?.Invoke(__instance, new object[] { validation.Reason ?? "No body-aligned Cleave destination was found." });
                        CancelCurrent?.Invoke(__instance, new object[] { CancelReason.InvalidDestination });
                        return false;
                    }

                    ____destination = validation.Position;
                    ____effects.Departure(player.Position);
                    ____state.Transition(____token, AbilityPhase.Departing);
                    BodyAlignedCleaveRuntime.TeleportPositionOnly(player, ____destination);
                    RemoveCleaveMarker?.Invoke(__instance, null);
                    ____effects.Arrival(player.Position);
                    ____effects.PlaySound("event:/mission/combat/swing/weapon_swing", player.Position);
                    ____state.Transition(____token, AbilityPhase.Teleporting);
                    ____state.Transition(____token, AbilityPhase.Arriving);

                    BodyAlignedCleaveRuntime.PrepareSweep(state, ____cleaveSnapshot, player);
                    BodyAlignedCleaveRuntime.EnterBeginSweep(state);
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
                        BodyAlignedCleaveRuntime.ExitBeginSweep();
                    }

                    ____context.Logger.Debug(
                        $"Body-aligned Cleave started {BodyAlignedCleaveRuntime.DescribeFacing(player, state.Facing)}, " +
                        $"destination=({____destination.x:0.00}, {____destination.y:0.00}, {____destination.z:0.00}), " +
                        $"target={state.TargetIndex}, axialBacktrack={validation.UsedFallback}.");
                    ____state.Transition(____token, AbilityPhase.Active);
                    return false;

                case AbilityPhase.Active:
                    if (____cleave.Tick(dt))
                    {
                        ____context.Logger.Debug($"Body-aligned Cleave active phase finished; hits={____cleave.SuccessfulHits}.");
                        ____state.Transition(____token, AbilityPhase.Recovery);
                    }
                    return false;

                case AbilityPhase.Recovery:
                    if (____state.PhaseElapsed >= RecoverySeconds)
                    {
                        CompleteCurrent?.Invoke(__instance, null);
                        BodyAlignedCleaveRuntime.Clear(__instance);
                    }
                    return false;

                default:
                    return false;
            }
        }
    }

    [HarmonyPatch(typeof(CleaveExecutionSnapshot), "get_StartAngle")]
    internal static class BodyAlignedCleaveStartAnglePatch
    {
        private static void Postfix(ref double __result)
        {
            var state = BodyAlignedCleaveRuntime.BeginSweepState;
            if (state != null)
                __result = state.StartAngle;
        }
    }

    [HarmonyPatch(typeof(CleaveSweepController), nameof(CleaveSweepController.Begin))]
    internal static class BodyAlignedCleaveBeginPatch
    {
        private static void Prefix() => BodyAlignedCleaveRuntime.EnterFacingSuppression();

        private static void Postfix(
            ref float ____duration,
            ref float ____trailAccumulator,
            ref int ____trailBursts,
            bool __result)
        {
            if (__result)
            {
                ____duration = 0.22f;
                ____trailAccumulator = 0f;
                ____trailBursts = 12;
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            BodyAlignedCleaveRuntime.ExitFacingSuppression();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CleaveSweepController), nameof(CleaveSweepController.Tick))]
    internal static class BodyAlignedCleaveArcPatch
    {
        private static void Prefix(
            float dt,
            Agent ____actor,
            float ____elapsed,
            float ____duration,
            double ____sweepRadians,
            SweepDirection ____direction,
            float ____radius,
            EffectController ____effects)
        {
            BodyAlignedCleaveRuntime.EnterFacingSuppression();
            if (____actor == null || ____duration <= 0f) return;
            var progress = Math.Min(1f, (____elapsed + Math.Max(0f, dt)) / ____duration);
            BodyAlignedCleaveRuntime.EmitArc(
                BodyAlignedCleaveRuntime.GetForActor(____actor),
                ____actor,
                ____effects,
                ____radius,
                ____sweepRadians,
                ____direction,
                progress);
        }

        private static Exception Finalizer(Exception __exception)
        {
            BodyAlignedCleaveRuntime.ExitFacingSuppression();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(AnimationController), nameof(AnimationController.BeginCleave))]
    internal static class BodyAlignedCleaveActionSuppressionPatch
    {
        private static bool Prefix(AnimationController __instance, Agent actor)
        {
            // A single native left/right attack cannot represent or synchronize with a 340-degree
            // radial sweep. The aligned arc and hit schedule are the presentation source of truth.
            __instance?.ResetActionSpeed(actor);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(AnimationController),
        nameof(AnimationController.SetActorFacing),
        new Type[] { typeof(Agent), typeof(Vec3) })]
    internal static class BodyAlignedCleaveVectorFacingSuppressionPatch
    {
        private static bool Prefix() => !BodyAlignedCleaveRuntime.FacingWritesSuppressed;
    }

    [HarmonyPatch(
        typeof(AnimationController),
        nameof(AnimationController.SetActorFacing),
        new Type[] { typeof(Agent), typeof(double) })]
    internal static class BodyAlignedCleaveAngleFacingSuppressionPatch
    {
        private static bool Prefix() => !BodyAlignedCleaveRuntime.FacingWritesSuppressed;
    }

    [HarmonyPatch(typeof(AbilityManager), "BeginFovPulse")]
    internal static class BodyAlignedCleaveFovSuppressionPatch
    {
        private static bool Prefix(AbilityManager __instance) =>
            __instance == null || __instance.ActiveAbility != AbilityId.VoidstepCleave;
    }

    [HarmonyPatch(typeof(AbilityManager), "CancelCurrent")]
    internal static class BodyAlignedCleaveCancellationPatch
    {
        private static void Prefix(AbilityManager __instance, ref Vec3 ____castOriginalLook)
        {
            if (__instance == null || !__instance.IsBusy || __instance.ActiveAbility != AbilityId.VoidstepCleave)
                return;
            ____castOriginalLook = Vec3.Zero;
        }

        private static void Postfix(AbilityManager __instance) => BodyAlignedCleaveRuntime.Clear(__instance);
    }

    [HarmonyPatch(typeof(VoidstepAbilityEffects), nameof(VoidstepAbilityEffects.VoidCleave))]
    internal static class BodyAlignedCleaveInitialEffectPatch
    {
        private static bool Prefix(EffectController effects, Vec3 center, float cleaveRadius)
        {
            if (effects == null || VoidstepSettings.Current.EffectIntensity <= 0f)
                return false;
            var state = BodyAlignedCleaveRuntime.BeginSweepState;
            var facing = state != null ? state.Facing : Vec3.Forward;
            facing = BodyAlignedCleaveRuntime.NormalizeFacing(facing);
            var right = new Vec3(-facing.y, facing.x, 0f, 0f);
            var radius = Math.Min(2.5f, Math.Max(1.15f, cleaveRadius * 0.38f));
            effects.Arrival(center + Vec3.Up * 0.30f);
            effects.WeaponTrail(center + Vec3.Up * 0.82f + facing * radius);
            effects.WeaponTrail(center + Vec3.Up * 0.80f + facing * (radius * 0.82f) + right * 0.38f);
            effects.WeaponTrail(center + Vec3.Up * 0.80f + facing * (radius * 0.82f) - right * 0.38f);
            return false;
        }
    }
}
