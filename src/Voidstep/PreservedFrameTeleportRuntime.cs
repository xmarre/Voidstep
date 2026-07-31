using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Safe fallback while Bannerlord's mounted displacement APIs are unsuitable.
    /// Runtime testing proved that every attempted runtime movement boundary could rotate the
    /// rider or mount. This boundary therefore consumes the cast without mutating Agent position,
    /// frame, body direction, look direction, movement input or action state.
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

            logger?.Debug(
                source + " displacement suppressed to protect Agent orientation; actor=" +
                actor.Index + ", requestedDestination=" + Format(destination) +
                ", livePosition=" + Format(actor.Position) + ".");
            return true;
        }

        private static bool OwnsLiveMainAgent(Mission mission, Agent actor)
        {
            return mission != null && actor != null && actor.IsActive() &&
                   ReferenceEquals(Mission.Current, mission) &&
                   ReferenceEquals(mission.MainAgent, actor);
        }

        private static string Format(Vec3 value)
        {
            return "(" + value.x.ToString("0.00") + ", " +
                   value.y.ToString("0.00") + ", " +
                   value.z.ToString("0.00") + ")";
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
