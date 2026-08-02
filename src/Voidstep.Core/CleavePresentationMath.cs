using System;

namespace Voidstep.Core
{
    public static class CleavePresentationMath
    {
        public static float CalculateDuration(float sweepDegrees)
        {
            var normalized = NormalizeSweep(sweepDegrees);
            return 0.18f + 0.42f * normalized;
        }

        public static float CalculateActionStartProgress(float sweepDegrees)
        {
            var normalized = NormalizeSweep(sweepDegrees);
            return 0.18f - 0.12f * normalized;
        }

        public static float CalculateActionEndProgress(float sweepDegrees)
        {
            var normalized = NormalizeSweep(sweepDegrees);
            return 0.82f + 0.12f * normalized;
        }

        public static float CalculateMountedSweepHeight(float groundHeight, float riderChestHeight)
        {
            var lower = groundHeight + 0.75f;
            var upper = Math.Max(lower, riderChestHeight - 0.20f);
            return Clamp(groundHeight + 1.25f, lower, upper);
        }

        public static float CalculateVisualReach(float weaponLength, float cleaveRadius)
        {
            var radius = Math.Max(0.10f, cleaveRadius);
            var physicalReach = Clamp(weaponLength, 0.65f, radius);
            var magicalExtension = Math.Max(0f, radius - physicalReach) * 0.45f;
            return Math.Min(radius, physicalReach + magicalExtension);
        }

        private static float NormalizeSweep(float sweepDegrees) =>
            Clamp(sweepDegrees, 0f, 360f) / 360f;

        private static float Clamp(float value, float minimum, float maximum) =>
            Math.Max(minimum, Math.Min(maximum, value));
    }
}
