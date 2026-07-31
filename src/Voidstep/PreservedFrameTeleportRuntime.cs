using System;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Translates the current main agent without allowing Bannerlord's convenience teleport
    /// routine to derive a new body yaw from movement, camera or terrain state. Rider and mount
    /// are moved as one rigid pair using their existing native frame directions and spatial offset.
    /// </summary>
    internal static class PreservedFrameTeleportRuntime
    {
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

            var actorPosition = actor.Position;
            var actorDirection = CaptureNativeFrameDirection(actor);
            var actorLook = CaptureLookDirection(actor);
            var actorBodyBefore = BodyAlignedCleaveRuntime.GetBodyFacing(actor);
            var mount = actor.MountAgent;
            var mounted = mount != null && mount.IsActive();
            var riderOffset = Vec3.Zero;
            var mountBodyBefore = Vec3.Forward;
            var mountTarget = Vec3.Invalid;
            var riderTarget = destination;

            try
            {
                if (mounted)
                {
                    var mountPosition = mount.Position;
                    var mountDirection = CaptureNativeFrameDirection(mount);
                    var mountLook = CaptureLookDirection(mount);
                    mountBodyBefore = BodyAlignedCleaveRuntime.GetBodyFacing(mount);
                    riderOffset = actorPosition - mountPosition;
                    mountTarget = destination;
                    riderTarget = destination + riderOffset;

                    mount.SetInitialFrame(in mountTarget, in mountDirection, true);
                    actor.SetInitialFrame(in riderTarget, in actorDirection, true);

                    // SetInitialFrame owns the native body frame. Restore independent look state
                    // exactly as captured so aiming cannot become an implicit facing command.
                    mount.LookDirection = mountLook;
                    actor.LookDirection = actorLook;
                }
                else
                {
                    actor.SetInitialFrame(in riderTarget, in actorDirection, true);
                    actor.LookDirection = actorLook;
                }

                if (!preserveMomentum)
                {
                    actor.MovementInputVector = Vec2.Zero;
                    if (mounted)
                        mount.MovementInputVector = Vec2.Zero;
                }

                LogResult(
                    source,
                    logger,
                    actor,
                    mount,
                    mounted,
                    actorBodyBefore,
                    actorLook,
                    mountBodyBefore,
                    riderOffset,
                    mountTarget,
                    riderTarget);
                return true;
            }
            catch (Exception ex)
            {
                logger?.Debug(
                    source + " preserved-frame teleport failed safely for actor=" +
                    actor.Index + ": " + ex.Message);
                return false;
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
            Vec3 riderOffset,
            Vec3 mountTarget,
            Vec3 riderTarget)
        {
            if (logger == null)
                return;

            var actorBodyAfter = BodyAlignedCleaveRuntime.GetBodyFacing(actor);
            var actorLookAfter = CaptureLookDirection(actor);
            var actorPositionError = Distance(actor.Position, riderTarget);
            var message = source + " applied preserved-frame teleport; actor=" + actor.Index +
                          ", bodyDelta=" + AngleDegrees(actorBodyBefore, actorBodyAfter).ToString("0.0") +
                          "deg, lookDelta=" + AngleDegrees(actorLookBefore, actorLookAfter).ToString("0.0") +
                          "deg, positionError=" + actorPositionError.ToString("0.000") +
                          ", mounted=" + mounted;

            if (mounted && mount != null && mount.IsActive())
            {
                var mountBodyAfter = BodyAlignedCleaveRuntime.GetBodyFacing(mount);
                var liveOffset = actor.Position - mount.Position;
                message += ", mountBodyDelta=" +
                           AngleDegrees(mountBodyBefore, mountBodyAfter).ToString("0.0") +
                           "deg, mountPositionError=" +
                           Distance(mount.Position, mountTarget).ToString("0.000") +
                           ", riderOffsetError=" +
                           Distance(liveOffset, riderOffset).ToString("0.000");
            }

            logger.Debug(message + ".");
        }

        private static Vec3 CaptureNativeFrameDirection(Agent agent)
        {
            if (agent != null)
            {
                try
                {
                    var direction = agent.Frame.rotation.f;
                    direction.z = 0f;
                    if (direction.Normalize() >= 0.001f)
                        return direction;
                }
                catch
                {
                }

                var body = BodyAlignedCleaveRuntime.GetBodyFacing(agent);
                body.z = 0f;
                if (body.Normalize() >= 0.001f)
                    return body;
            }
            return Vec3.Forward;
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
