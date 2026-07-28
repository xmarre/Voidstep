using System;
using System.Collections.Generic;

namespace Voidstep.Core
{
    public readonly struct SweepTarget<T>
    {
        public SweepTarget(T value, double angle, double distanceSquared)
        {
            Value = value;
            Angle = angle;
            DistanceSquared = distanceSquared;
        }

        public T Value { get; }
        public double Angle { get; }
        public double DistanceSquared { get; }
    }

    public readonly struct ScheduledSweepTarget<T>
    {
        public ScheduledSweepTarget(T value, double progress, double distanceSquared)
        {
            Value = value;
            Progress = progress;
            DistanceSquared = distanceSquared;
        }

        public T Value { get; }
        public double Progress { get; }
        public double DistanceSquared { get; }
    }

    public static class SweepPlanner
    {
        public static void BuildSchedule<T>(
            IReadOnlyList<SweepTarget<T>> candidates,
            double startAngle,
            double sweepRadians,
            SweepDirection direction,
            double radius,
            int maximumTargets,
            List<ScheduledSweepTarget<T>> destination)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (radius < 0.0) throw new ArgumentOutOfRangeException(nameof(radius));
            if (maximumTargets < 0) throw new ArgumentOutOfRangeException(nameof(maximumTargets));

            destination.Clear();
            var radiusSquared = radius * radius;
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate.DistanceSquared > radiusSquared)
                    continue;
                if (!AngleMath.IsInsideSweep(startAngle, candidate.Angle, sweepRadians, direction))
                    continue;
                destination.Add(new ScheduledSweepTarget<T>(
                    candidate.Value,
                    AngleMath.ProgressForTarget(startAngle, candidate.Angle, sweepRadians, direction),
                    candidate.DistanceSquared));
            }

            destination.Sort(Compare);
            if (maximumTargets > 0 && destination.Count > maximumTargets)
                destination.RemoveRange(maximumTargets, destination.Count - maximumTargets);
        }

        private static int Compare<T>(ScheduledSweepTarget<T> left, ScheduledSweepTarget<T> right)
        {
            var progress = left.Progress.CompareTo(right.Progress);
            return progress != 0 ? progress : left.DistanceSquared.CompareTo(right.DistanceSquared);
        }
    }
}
