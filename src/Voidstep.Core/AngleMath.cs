using System;

namespace Voidstep.Core
{
    public static class AngleMath
    {
        public const double Tau = Math.PI * 2.0;

        public static double NormalizeRadians(double angle)
        {
            angle %= Tau;
            if (angle < 0.0)
                angle += Tau;
            return angle;
        }

        public static double SignedShortestDelta(double from, double to)
        {
            var delta = NormalizeRadians(to) - NormalizeRadians(from);
            if (delta > Math.PI)
                delta -= Tau;
            if (delta < -Math.PI)
                delta += Tau;
            return delta;
        }

        public static double TravelFromStart(double start, double target, SweepDirection direction)
        {
            return direction == SweepDirection.CounterClockwise
                ? NormalizeRadians(target - start)
                : NormalizeRadians(start - target);
        }

        public static bool IsInsideSweep(double start, double target, double sweepRadians, SweepDirection direction, double epsilon = 1e-6)
        {
            if (sweepRadians < 0.0 || sweepRadians > Tau + epsilon)
                throw new ArgumentOutOfRangeException(nameof(sweepRadians));
            return TravelFromStart(start, target, direction) <= sweepRadians + epsilon;
        }

        public static double ProgressForTarget(double start, double target, double sweepRadians, SweepDirection direction)
        {
            if (sweepRadians <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(sweepRadians));
            var travel = TravelFromStart(start, target, direction);
            return Clamp01(travel / sweepRadians);
        }

        public static double LerpSweepAngle(double start, double sweepRadians, SweepDirection direction, double progress)
        {
            var sign = (int)direction;
            return NormalizeRadians(start + sign * sweepRadians * Clamp01(progress));
        }

        public static bool HasSweepPassed(double start, double target, double currentProgress, double sweepRadians, SweepDirection direction, double toleranceRadians = 0.025)
        {
            var targetTravel = TravelFromStart(start, target, direction);
            var currentTravel = sweepRadians * Clamp01(currentProgress);
            return targetTravel + toleranceRadians < currentTravel;
        }

        private static double Clamp01(double value) => value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
    }
}
