using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    [HarmonyPatch(typeof(CleaveSweepController), "BuildSchedule")]
    internal static class MovingCleaveTargetSchedulePatch
    {
        private static bool Prefix(
            Mission ____mission,
            Agent ____actor,
            float ____radius,
            bool ____friendlyFire,
            bool ____targetMounts,
            double ____startAngle,
            double ____sweepRadians,
            SweepDirection ____direction,
            MBList<Agent> ____nearby,
            List<SweepTarget<Agent>> ____candidates,
            List<ScheduledSweepTarget<Agent>> ____schedule,
            HitRegistry<int> ____hits,
            ref int ____largestCandidateSet)
        {
            if (____mission == null || ____actor == null)
            {
                ____nearby.Clear();
                ____candidates.Clear();
                ____schedule.Clear();
                return false;
            }

            ____nearby.Clear();
            if (____friendlyFire)
                ____mission.GetNearbyAgents(____actor.Position.AsVec2, ____radius, ____nearby);
            else
                ____mission.GetNearbyEnemyAgents(____actor.Position.AsVec2, ____radius, ____actor.Team, ____nearby);

            ____candidates.Clear();
            for (var i = 0; i < ____nearby.Count; i++)
            {
                var target = ____nearby[i];
                if (!IsEligible(
                        ____actor,
                        target,
                        ____friendlyFire,
                        ____targetMounts,
                        ____hits))
                {
                    continue;
                }

                var delta = target.Position - ____actor.Position;
                var distanceSquared = delta.x * delta.x + delta.y * delta.y;
                var angle = AngleMath.NormalizeRadians(Math.Atan2(delta.y, delta.x));

                // Do not discard a live target merely because its current angle is now behind
                // the moving sweep boundary. Mounted movement can shift an enemy across that
                // boundary between ticks. ProcessLive will hit it immediately when its current
                // angular progress is already due, while HitRegistry still guarantees one hit.
                ____candidates.Add(new SweepTarget<Agent>(
                    target,
                    angle,
                    distanceSquared,
                    target.Index));
            }

            ____largestCandidateSet = Math.Max(____largestCandidateSet, ____candidates.Count);
            SweepPlanner.BuildSchedule(
                ____candidates,
                ____startAngle,
                ____sweepRadians,
                ____direction,
                ____radius,
                0,
                ____schedule);
            return false;
        }

        private static bool IsEligible(
            Agent actor,
            Agent target,
            bool friendlyFire,
            bool targetMounts,
            HitRegistry<int> hits)
        {
            if (target == null || target == actor || !target.IsActive() || target.Health <= 0f)
                return false;
            if (hits != null && hits.Contains(target.Index))
                return false;
            if (!targetMounts && target.IsMount)
                return false;
            if (!friendlyFire &&
                (actor.Team == null || target.Team == null || !actor.Team.IsEnemyOf(target.Team)))
            {
                return false;
            }
            return target.IsHuman || target.IsMount;
        }
    }
}
