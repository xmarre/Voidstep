using System;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Bannerlord may report a null or indirect affector for missile and synthetic hits while
    /// Blow.OwnerId still contains the authoritative player or controlled-mount owner. Normalize
    /// that source before Domino evaluates the hit so valid player damage is propagated.
    /// </summary>
    [HarmonyPatch(typeof(VoidstepMissionBehavior), nameof(VoidstepMissionBehavior.OnAgentHit))]
    internal static class DominoPlayerSourceRepairPatch
    {
        private static void Prefix(ref Agent affectorAgent, in Blow blow)
        {
            var mission = Mission.Current;
            var player = mission?.MainAgent;
            if (mission == null || player == null || blow.OwnerId < 0)
                return;

            var owner = mission.FindAgentWithIndex(blow.OwnerId);
            if (owner == null)
                return;

            var mount = player.MountAgent;
            if (ReferenceEquals(owner, player) ||
                (mount != null && ReferenceEquals(owner, mount)))
            {
                affectorAgent = owner;
            }
        }
    }
}
