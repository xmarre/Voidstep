using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Bannerlord 1.3.15 can rotate a mount inside IMBAgent.SetPosition before any managed
    /// teleport callback runs. This guard is restricted to the exact current mission main agent
    /// and its current mount. It restores their pre-cast native body/look directions immediately
    /// and for a small bounded number of owned mission ticks while attachment state settles.
    ///
    /// Occupied destinations remain valid. This class does not patch TeleportValidator.IsOccupied
    /// and does not displace the requested destination to a fallback location.
    /// </summary>
    internal static class ScopedTeleportOrientationGuard
    {
        private const int SettlementTicks = 3;
        private static readonly ConditionalWeakTable<Mission, PendingState> PendingByMission =
            new ConditionalWeakTable<Mission, PendingState>();
        private static readonly FieldInfo NativeAgentApiField =
            AccessTools.Field(typeof(MBAPI), "IMBAgent");
        private static readonly MethodInfo NativeGetPtrMethod =
            AccessTools.Method(typeof(Agent), "GetPtr", Type.EmptyTypes);
        private static readonly MethodInfo NativeSetMovementDirectionMethod =
            NativeAgentApiField == null
                ? null
                : AccessTools.Method(
                    NativeAgentApiField.FieldType,
                    "SetMovementDirection",
                    new[] { typeof(UIntPtr), typeof(Vec2).MakeByRefType() });
        private static readonly MethodInfo NativeSetLookDirectionMethod =
            NativeAgentApiField == null
                ? null
                : AccessTools.Method(
                    NativeAgentApiField.FieldType,
                    "SetLookDirection",
                    new[] { typeof(UIntPtr), typeof(Vec3) });

        internal sealed class Snapshot
        {
            internal WeakReference<Agent> Actor;
            internal WeakReference<Agent> Mount;
            internal Vec3 ActorBody;
            internal Vec3 ActorLook;
            internal Vec3 MountBody;
            internal Vec3 MountLook;
            internal string Source;
            internal VoidstepLogger Logger;
        }

        private sealed class PendingState
        {
            internal Snapshot Snapshot;
            internal int RemainingTicks;
        }

        internal static Snapshot Capture(
            Mission mission,
            Agent actor,
            string source,
            VoidstepLogger logger)
        {
            if (!OwnsLiveMainAgent(mission, actor))
                return null;

            var mount = actor.MountAgent;
            if (mount != null && !mount.IsActive())
                mount = null;

            return new Snapshot
            {
                Actor = new WeakReference<Agent>(actor),
                Mount = mount == null ? null : new WeakReference<Agent>(mount),
                ActorBody = NormalizeBody(BodyAlignedCleaveRuntime.GetBodyFacing(actor)),
                ActorLook = NormalizeLook(actor.LookDirection),
                MountBody = mount == null
                    ? Vec3.Forward
                    : NormalizeBody(BodyAlignedCleaveRuntime.GetBodyFacing(mount)),
                MountLook = mount == null
                    ? Vec3.Forward
                    : NormalizeLook(mount.LookDirection),
                Source = source ?? "Voidstep",
                Logger = logger
            };
        }

        internal static void RestoreAndArm(
            Mission mission,
            Snapshot snapshot)
        {
            if (mission == null || snapshot == null ||
                !snapshot.Actor.TryGetTarget(out var actor) ||
                !OwnsLiveMainAgent(mission, actor))
            {
                return;
            }

            Restore(snapshot, actor);
            PendingByMission.Remove(mission);
            PendingByMission.Add(
                mission,
                new PendingState
                {
                    Snapshot = snapshot,
                    RemainingTicks = SettlementTicks
                });

            LogRestoration(snapshot, actor, "immediate");
        }

        internal static void Tick(Mission mission)
        {
            if (mission == null || !ReferenceEquals(Mission.Current, mission) ||
                !PendingByMission.TryGetValue(mission, out var pending) ||
                pending?.Snapshot == null)
            {
                return;
            }

            var snapshot = pending.Snapshot;
            if (!snapshot.Actor.TryGetTarget(out var actor) ||
                !OwnsLiveMainAgent(mission, actor))
            {
                PendingByMission.Remove(mission);
                return;
            }

            Restore(snapshot, actor);
            pending.RemainingTicks--;
            if (pending.RemainingTicks > 0)
                return;

            LogRestoration(snapshot, actor, "settled");
            PendingByMission.Remove(mission);
        }

        internal static void Clear(Mission mission)
        {
            if (mission != null)
                PendingByMission.Remove(mission);
        }

        private static void Restore(Snapshot snapshot, Agent actor)
        {
            var mount = actor.MountAgent;
            Agent capturedMount = null;
            var mounted = snapshot.Mount != null &&
                          snapshot.Mount.TryGetTarget(out capturedMount) &&
                          capturedMount != null && capturedMount.IsActive() &&
                          ReferenceEquals(mount, capturedMount);

            // A mounted rider's body is attachment-owned. Restoring a separate rider movement
            // direction fights that attachment and was the source of the deterministic 90-degree
            // right turn. Preserve only the rider look while the mount owns body direction.
            RestoreDirection(
                actor,
                snapshot.ActorBody,
                snapshot.ActorLook,
                restoreMovementDirection: !mounted);

            if (mounted)
            {
                RestoreDirection(
                    capturedMount,
                    snapshot.MountBody,
                    snapshot.MountLook,
                    restoreMovementDirection: true);
            }
        }

        private static void RestoreDirection(
            Agent agent,
            Vec3 body,
            Vec3 look,
            bool restoreMovementDirection)
        {
            if (agent == null || !agent.IsActive() ||
                NativeAgentApiField == null || NativeGetPtrMethod == null ||
                NativeSetMovementDirectionMethod == null || NativeSetLookDirectionMethod == null)
            {
                return;
            }

            try
            {
                var api = NativeAgentApiField.GetValue(null);
                var pointerValue = NativeGetPtrMethod.Invoke(agent, null);
                if (api == null || !(pointerValue is UIntPtr pointer) || pointer.Equals(UIntPtr.Zero))
                    return;

                if (restoreMovementDirection)
                {
                    // Use Bannerlord's native XY direction vector directly. Reconstructing this
                    // from the angular property applies a 90-degree axis offset in 1.3.15.
                    var movement = new Vec2(body.x, body.y);
                    if (movement.Normalize() < 0.001f)
                        movement = Vec2.Forward;
                    NativeSetMovementDirectionMethod.Invoke(api, new object[] { pointer, movement });
                }

                NativeSetLookDirectionMethod.Invoke(api, new object[] { pointer, look });
            }
            catch
            {
            }
        }

        private static void LogRestoration(Snapshot snapshot, Agent actor, string stage)
        {
            var actorDelta = AngleDegrees(snapshot.ActorBody, BodyAlignedCleaveRuntime.GetBodyFacing(actor));
            var message = snapshot.Source + " scoped orientation restore " + stage +
                          "; actor=" + actor.Index +
                          ", actorBodyDelta=" + actorDelta.ToString("0.0") + "deg";

            if (snapshot.Mount != null && snapshot.Mount.TryGetTarget(out var mount) && mount.IsActive())
            {
                var mountDelta = AngleDegrees(snapshot.MountBody, BodyAlignedCleaveRuntime.GetBodyFacing(mount));
                message += ", mountBodyDelta=" + mountDelta.ToString("0.0") + "deg";
            }

            snapshot.Logger?.Debug(message + ".");
        }

        private static bool OwnsLiveMainAgent(Mission mission, Agent actor)
        {
            return mission != null && actor != null && actor.IsActive() &&
                   ReferenceEquals(Mission.Current, mission) &&
                   ReferenceEquals(mission.MainAgent, actor);
        }

        private static Vec3 NormalizeBody(Vec3 direction)
        {
            direction.z = 0f;
            if (direction.Normalize() < 0.001f)
                direction = Vec3.Forward;
            return direction;
        }

        private static Vec3 NormalizeLook(Vec3 direction)
        {
            if (direction.Normalize() < 0.001f)
                direction = Vec3.Forward;
            return direction;
        }

        private static double AngleDegrees(Vec3 left, Vec3 right)
        {
            left = NormalizeBody(left);
            right = NormalizeBody(right);
            var dot = Math.Max(-1f, Math.Min(1f, Vec3.DotProduct(left, right)));
            return Math.Acos(dot) * 180.0 / Math.PI;
        }
    }

    [HarmonyPatch(
        typeof(PreservedFrameTeleportRuntime),
        nameof(PreservedFrameTeleportRuntime.Teleport))]
    internal static class ScopedTeleportOrientationCapturePatch
    {
        private static void Prefix(
            Mission mission,
            Agent actor,
            string source,
            VoidstepLogger logger,
            out ScopedTeleportOrientationGuard.Snapshot __state)
        {
            __state = ScopedTeleportOrientationGuard.Capture(
                mission,
                actor,
                source,
                logger);
        }

        private static void Postfix(
            Mission mission,
            bool __result,
            ScopedTeleportOrientationGuard.Snapshot __state)
        {
            if (__result)
                ScopedTeleportOrientationGuard.RestoreAndArm(mission, __state);
        }
    }

    [HarmonyPatch(
        typeof(CameraFacingTeleportOwnership),
        nameof(CameraFacingTeleportOwnership.Tick))]
    internal static class ScopedTeleportOrientationTickPatch
    {
        private static void Postfix(Mission mission)
        {
            ScopedTeleportOrientationGuard.Tick(mission);
        }
    }

    [HarmonyPatch(
        typeof(CameraFacingTeleportOwnership),
        nameof(CameraFacingTeleportOwnership.Clear))]
    internal static class ScopedTeleportOrientationCleanupPatch
    {
        private static void Postfix(Mission mission)
        {
            ScopedTeleportOrientationGuard.Clear(mission);
        }
    }
}
