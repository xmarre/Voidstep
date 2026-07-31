using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
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

        internal static void Arm(
            AbilityManager manager,
            Agent actor,
            Snapshot snapshot,
            string source,
            VoidstepLogger logger)
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
}
