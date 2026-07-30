using System;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Bannerlord may report a missing or inactive affector for missile and synthetic hits while
    /// Blow.OwnerId still contains the authoritative player or controlled-mount owner. Preserve
    /// every valid existing affector and repair only the missing or stale case before Domino
    /// evaluates ownership.
    /// </summary>
    [HarmonyPatch(typeof(VoidstepMissionBehavior), nameof(VoidstepMissionBehavior.OnAgentHit))]
    internal static class DominoPlayerSourceRepairPatch
    {
        private static void Prefix(ref Agent __1, ref Blow __2)
        {
            if (__1 != null && __1.IsActive())
                return;

            var mission = Mission.Current;
            var player = mission?.MainAgent;
            if (mission == null || player == null || __2.OwnerId < 0)
                return;

            var owner = mission.FindAgentWithIndex(__2.OwnerId);
            if (owner == null || !owner.IsActive())
                return;

            var mount = player.MountAgent;
            if (ReferenceEquals(owner, player) ||
                (mount != null && ReferenceEquals(owner, mount)))
            {
                __1 = owner;
            }
        }
    }
}
