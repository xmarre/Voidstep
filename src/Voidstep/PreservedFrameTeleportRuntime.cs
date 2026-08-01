using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Translates only the current mission's controlled actor through Bannerlord's native
    /// IMBAgent.SetPosition operation. Mounted actors are moved through the existing mount
    /// attachment only; the rider is never independently repositioned or given orientation input.
    /// </summary>
    internal static class PreservedFrameTeleportRuntime
    {
        private static readonly VoidstepLogger FallbackLogger = new VoidstepLogger();
        private static readonly FieldInfo NativeAgentApiField =
            AccessTools.Field(typeof(MBAPI), "IMBAgent");
        private static readonly MethodInfo NativeGetPtrMethod =
            AccessTools.Method(typeof(Agent), "GetPtr", Type.EmptyTypes);
        private static readonly MethodInfo NativeSetPositionMethod =
            NativeAgentApiField == null
                ? null
                : AccessTools.Method(
                    NativeAgentApiField.FieldType,
                    "SetPosition",
                    new[] { typeof(UIntPtr), typeof(Vec3).MakeByRefType() });

        internal static bool Teleport(
            Mission mission,
            Agent actor,
            Vec3 destination,
            bool preserveMomentum,
            string source,
            VoidstepLogger logger)
        {
            if (!OwnsLiveMainAgent(mission, actor) || !destination.IsValid)
                return false;

            logger = logger ?? FallbackLogger;
            var actorBodyBefore = BodyAlignedCleaveRuntime.GetBodyFacing(actor);
            var actorLookBefore = CaptureLookDirection(actor);
            var mount = actor.MountAgent;
            var mounted = mount != null && mount.IsActive();
            var riderOffset = Vec3.Zero;
            var mountBodyBefore = Vec3.Forward;
            var mountTarget = Vec3.Invalid;

            try
            {
                if (!NativePositionApiAvailable())
                {
                    logger.Debug(source + " native position-only teleport API was unavailable.");
                    return false;
                }

                if (mounted)
                {
                    var actorPosition = actor.Position;
                    var mountPosition = mount.Position;
                    riderOffset = actorPosition - mountPosition;
                    mountBodyBefore = BodyAlignedCleaveRuntime.GetBodyFacing(mount);

                    // The requested destination is the rider destination. Move only the mount to
                    // the corresponding attachment origin and let Bannerlord carry the rider.
                    // Moving mount and rider independently forces attachment reconciliation and
                    // was the remaining source of intermittent 180-degree turns.
                    mountTarget = destination - riderOffset;
                    if (!SetNativePosition(mount, mountTarget))
                    {
                        logger.Debug(source + " native mounted attachment translation failed safely.");
                        return false;
                    }

                    var mountBodyAfterNativeMove = BodyAlignedCleaveRuntime.GetBodyFacing(mount);
                    NotifyTeleported(mount);
                    NotifyTeleported(actor);
                    LogResult(
                        source,
                        logger,
                        actor,
                        mount,
                        true,
                        actorBodyBefore,
                        actorLookBefore,
                        mountBodyBefore,
                        mountBodyAfterNativeMove,
                        riderOffset,
                        mountTarget,
                        destination);
                    return true;
                }

                if (!SetNativePosition(actor, destination))
                {
                    logger.Debug(source + " native actor position translation failed safely.");
                    return false;
                }

                NotifyTeleported(actor);
                LogResult(
                    source,
                    logger,
                    actor,
                    null,
                    false,
                    actorBodyBefore,
                    actorLookBefore,
                    Vec3.Forward,
                    Vec3.Forward,
                    Vec3.Zero,
                    Vec3.Invalid,
                    destination);
                return true;
            }
            catch (Exception ex)
            {
                logger.Debug(
                    source + " native position-only teleport failed safely for actor=" +
                    actor.Index + ": " + Unwrap(ex).Message);
                return false;
            }
        }

        private static bool NativePositionApiAvailable()
        {
            return NativeAgentApiField != null && NativeGetPtrMethod != null &&
                   NativeSetPositionMethod != null && NativeAgentApiField.GetValue(null) != null;
        }

        private static bool SetNativePosition(Agent agent, Vec3 position)
        {
            if (agent == null || !agent.IsActive() || !position.IsValid)
                return false;

            var api = NativeAgentApiField.GetValue(null);
            if (api == null)
                return false;

            var pointerValue = NativeGetPtrMethod.Invoke(agent, null);
            if (!(pointerValue is UIntPtr pointer) || pointer.Equals(UIntPtr.Zero))
                return false;

            var arguments = new object[] { pointer, position };
            NativeSetPositionMethod.Invoke(api, arguments);
            return true;
        }

        private static void NotifyTeleported(Agent agent)
        {
            if (agent == null || !agent.IsActive())
                return;

            var components = agent.Components;
            if (components == null)
                return;

            for (var i = 0; i < components.Count; i++)
            {
                try { components[i]?.OnAgentTeleported(); }
                catch { }
            }
        }

        private static void LogResult(
            string source,
            VoidstepLogger logger,
            Agent actor,
            Agent mount,
            bool mounted,
            Vec3 actorBodyBefore,
            Vec3 actorLookBefore,
            Vec3 mountBodyBefore,
            Vec3 mountBodyAfterNativeMove,
            Vec3 riderOffset,
            Vec3 mountTarget,
            Vec3 riderTarget)
        {
            var actorBodyAfter = BodyAlignedCleaveRuntime.GetBodyFacing(actor);
            var actorLookAfter = CaptureLookDirection(actor);
            var message = source + " applied attachment-safe native teleport; actor=" + actor.Index +
                          ", bodyDelta=" + AngleDegrees(actorBodyBefore, actorBodyAfter).ToString("0.0") +
                          "deg, lookDelta=" + AngleDegrees(actorLookBefore, actorLookAfter).ToString("0.0") +
                          "deg, positionError=" + Distance(actor.Position, riderTarget).ToString("0.000") +
                          ", mounted=" + mounted;

            if (mounted && mount != null && mount.IsActive())
            {
                var mountBodyAfterCallbacks = BodyAlignedCleaveRuntime.GetBodyFacing(mount);
                var liveOffset = actor.Position - mount.Position;
                message += ", mountNativeBodyDelta=" +
                           AngleDegrees(mountBodyBefore, mountBodyAfterNativeMove).ToString("0.0") +
                           "deg, mountCallbackBodyDelta=" +
                           AngleDegrees(mountBodyAfterNativeMove, mountBodyAfterCallbacks).ToString("0.0") +
                           "deg, mountPositionError=" +
                           Distance(mount.Position, mountTarget).ToString("0.000") +
                           ", riderOffsetError=" +
                           Distance(liveOffset, riderOffset).ToString("0.000");
            }

            logger.Debug(message + ".");
        }

        private static Vec3 CaptureLookDirection(Agent agent)
        {
            var look = agent != null ? agent.LookDirection : Vec3.Forward;
            look.z = 0f;
            if (look.Normalize() < 0.001f)
                look = Vec3.Forward;
            return look;
        }

        private static bool OwnsLiveMainAgent(Mission mission, Agent actor)
        {
            return mission != null && actor != null && actor.IsActive() &&
                   ReferenceEquals(Mission.Current, mission) &&
                   ReferenceEquals(mission.MainAgent, actor);
        }

        private static float Distance(Vec3 left, Vec3 right)
        {
            return (left - right).Length;
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

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }
    }

    [HarmonyPatch(
        typeof(BodyAlignedCleaveRuntime),
        nameof(BodyAlignedCleaveRuntime.TeleportPositionOnly))]
    internal static class PreservedFrameCleaveTeleportPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Agent actor, Vec3 position)
        {
            PreservedFrameTeleportRuntime.Teleport(
                Mission.Current,
                actor,
                position,
                false,
                "Voidstep Cleave",
                null);
            return false;
        }
    }
}