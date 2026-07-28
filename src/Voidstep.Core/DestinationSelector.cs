using System;
using System.Collections.Generic;

namespace Voidstep.Core
{
    public readonly struct CandidateScore<T>
    {
        public CandidateScore(T value, double distanceSquared, double verticalDelta, int ordinal)
        {
            Value = value;
            DistanceSquared = distanceSquared;
            VerticalDelta = Math.Abs(verticalDelta);
            Ordinal = ordinal;
        }
        public T Value { get; }
        public double DistanceSquared { get; }
        public double VerticalDelta { get; }
        public int Ordinal { get; }
    }

    public static class DestinationSelector
    {
        public static bool TrySelectBest<T>(IReadOnlyList<CandidateScore<T>> candidates, Predicate<T> isValid, out T result)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (isValid == null) throw new ArgumentNullException(nameof(isValid));

            var found = false;
            var best = default(CandidateScore<T>);
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!isValid(candidate.Value)) continue;
                if (!found || Compare(candidate, best) < 0)
                {
                    best = candidate;
                    found = true;
                }
            }
            result = found ? best.Value : default(T);
            return found;
        }

        private static int Compare<T>(CandidateScore<T> left, CandidateScore<T> right)
        {
            var distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
            if (distance != 0) return distance;
            var vertical = left.VerticalDelta.CompareTo(right.VerticalDelta);
            if (vertical != 0) return vertical;
            return left.Ordinal.CompareTo(right.Ordinal);
        }
    }
}
