using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Replaces the old destination-occupancy bypass with mounted-aware clearance. A teleport
    /// into another live collision capsule can make Bannerlord's native position operation solve
    /// the overlap by rotating the mount, so occupied destinations must be rejected before the
    /// native move is attempted.
    /// </summary>
    [HarmonyPatch(typeof(TeleportValidator), "IsOccupied")]
    internal static class ScopedTeleportOccupancyPatch
    {
        private const float RiderRadius = 0.45f;
        private const float HumanRadius = 0.45f;
        private const float MountRadius = 1.20f;
        private const float ClearanceMargin = 0.15f;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Mission ____mission,
            MBList<Agent> ____nearby,
            Agent actor,
            Vec3 candidate,
            ref bool __result)
        {
            __result = IsOccupied(____mission, ____nearby, actor, candidate);
            return false;
        }

        private static bool IsOccupied(
            Mission mission,
            MBList<Agent> nearby,
            Agent actor,
            Vec3 riderCandidate)
        {
            if (mission == null || nearby == null || actor == null)
                return true;

            var mount = actor.MountAgent;
            var mounted = mount != null && mount.IsActive();
            var mountCandidate = riderCandidate;
            if (mounted)
            {
                var riderOffset = actor.Position - mount.Position;
                mountCandidate = riderCandidate - riderOffset;
            }

            nearby.Clear();
            mission.GetNearbyAgents(riderCandidate.AsVec2, mounted ? 4.0f : 1.75f, nearby);
            for (var i = 0; i < nearby.Count; i++)
            {
                var other = nearby[i];
                if (other == null || !other.IsActive() ||
                    ReferenceEquals(other, actor) || ReferenceEquals(other, mount) ||
                    ReferenceEquals(other, actor.RiderAgent))
                {
                    continue;
                }

                var otherRadius = other.IsMount ? MountRadius : HumanRadius;
                if (Overlaps(riderCandidate, RiderRadius, other, otherRadius))
                    return true;
                if (mounted && Overlaps(mountCandidate, MountRadius, other, otherRadius))
                    return true;
            }

            return false;
        }

        private static bool Overlaps(
            Vec3 candidate,
            float candidateRadius,
            Agent other,
            float otherRadius)
        {
            var maximumVerticalDelta = other.IsMount ? 2.6f : 2.1f;
            if (Math.Abs(other.Position.z - candidate.z) > maximumVerticalDelta)
                return false;

            var dx = other.Position.x - candidate.x;
            var dy = other.Position.y - candidate.y;
            var minimumDistance = candidateRadius + otherRadius + ClearanceMargin;
            return dx * dx + dy * dy < minimumDistance * minimumDistance;
        }
    }

    /// <summary>
    /// Bannerlord 1.3.15 can rotate a mount inside IMBAgent.SetPosition before any managed
    /// teleport callback runs. This guard captures only the current mission main agent and its
    /// current mount, restores their pre-cast native movement/look directions immediately, and
    /// repeats that restoration for two owned mission ticks while native collision attachment
    /// state settles. It never patches an Agent method and never resolves an actor globally.
    /// </summary>
    internal static class ScopedTeleportOrientationGuard
    {
        private const int SettlementTicks = 2;
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
            internal NativeDirection ActorDirection;
            internal NativeDirection MountDirection;
            internal Vec3 ActorBody;
            internal Vec3 MountBody;
            internal string Source;
            internal VoidstepLogger Logger;
        }

        private sealed class PendingState
        {
            internal Snapshot Snapshot;
            internal int RemainingTicks;
        }

        internal readonly struct NativeDirection
        {
            internal NativeDirection(Vec2 movement, Vec3 look)
            {
                Movement = movement;
                Look = look;
            }

            internal Vec2 Movement { get; }
            internal Vec3 Look { get; }
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
                ActorDirection = CaptureDirection(actor),
                MountDirection = mount == null ? default(NativeDirection) : CaptureDirection(mount),
                ActorBody = BodyAlignedCleaveRuntime.GetBodyFacing(actor),
                MountBody = mount == null ? Vec3.Forward : BodyAlignedCleaveRuntime.GetBodyFacing(mount),
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
            RestoreDirection(actor, snapshot.ActorDirection);
            if (snapshot.Mount != null && snapshot.Mount.TryGetTarget(out var mount) && mount.IsActive() &&
                ReferenceEquals(actor.MountAgent, mount))
            {
                RestoreDirection(mount, snapshot.MountDirection);
            }
        }

        private static NativeDirection CaptureDirection(Agent agent)
        {
            var angle = agent != null ? agent.MovementDirectionAsAngle : 0f;
            var movement = new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle));
            if (movement.Normalize() < 0.001f)
                movement = Vec2.Forward;

            var look = agent != null ? agent.LookDirection : Vec3.Forward;
            look.z = 0f;
            if (look.Normalize() < 0.001f)
                look = Vec3.Forward;
            return new NativeDirection(movement, look);
        }

        private static void RestoreDirection(Agent agent, NativeDirection direction)
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

                var movement = direction.Movement;
                NativeSetMovementDirectionMethod.Invoke(api, new object[] { pointer, movement });
                NativeSetLookDirectionMethod.Invoke(api, new object[] { pointer, direction.Look });
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

        private static double AngleDegrees(Vec3 left, Vec3 right)
        {
            left.z = 0f;
            right.z = 0f;
            if (left.Normalize() < 0.001f)
                left = Vec3.Forward;
            if (right.Normalize() < 0.001f)
                right = Vec3.Forward;
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
