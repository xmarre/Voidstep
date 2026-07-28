using System;
using System.Collections.Generic;
using Voidstep.Core;
using Xunit;

namespace Voidstep.Core.Tests
{
    public sealed class CoreLogicTests
    {
        private const double Deg = Math.PI / 180.0;

        [Theory]
        [InlineData(0, 0)]
        [InlineData(360, 0)]
        [InlineData(-90, 270)]
        [InlineData(810, 90)]
        public void AngleNormalisation(double inputDegrees, double expectedDegrees)
        {
            Assert.Equal(expectedDegrees, AngleMath.NormalizeRadians(inputDegrees * Deg) / Deg, 8);
        }

        [Fact]
        public void ClockwiseSweepOrdering()
        {
            var candidates = new List<SweepTarget<string>>
            {
                new SweepTarget<string>("late", 270 * Deg, 1),
                new SweepTarget<string>("early", 350 * Deg, 1),
                new SweepTarget<string>("middle", 315 * Deg, 1)
            };
            var result = new List<ScheduledSweepTarget<string>>();
            SweepPlanner.BuildSchedule(candidates, 0, 100 * Deg, SweepDirection.Clockwise, 2, 0, result);
            Assert.Collection(result,
                x => Assert.Equal("early", x.Value),
                x => Assert.Equal("middle", x.Value),
                x => Assert.Equal("late", x.Value));
        }

        [Fact]
        public void CounterClockwiseSweepOrdering()
        {
            var candidates = new List<SweepTarget<string>>
            {
                new SweepTarget<string>("late", 100 * Deg, 1),
                new SweepTarget<string>("early", 10 * Deg, 1),
                new SweepTarget<string>("middle", 55 * Deg, 1)
            };
            var result = new List<ScheduledSweepTarget<string>>();
            SweepPlanner.BuildSchedule(candidates, 0, 100 * Deg, SweepDirection.CounterClockwise, 2, 0, result);
            Assert.Collection(result,
                x => Assert.Equal("early", x.Value),
                x => Assert.Equal("middle", x.Value),
                x => Assert.Equal("late", x.Value));
        }

        [Fact]
        public void EqualProgressAndDistanceUseStableOrdinalBeforeTruncation()
        {
            var candidates = new List<SweepTarget<string>>
            {
                new SweepTarget<string>("second", 0, 1, 20),
                new SweepTarget<string>("first", 0, 1, 10)
            };
            var result = new List<ScheduledSweepTarget<string>>();
            SweepPlanner.BuildSchedule(candidates, 0, Math.PI, SweepDirection.CounterClockwise, 2, 1, result);
            Assert.Single(result);
            Assert.Equal("first", result[0].Value);
        }

        [Fact]
        public void SweepGapExcludesTargetOutsideThreeHundredFortyDegrees()
        {
            var start = 10 * Deg;
            Assert.True(AngleMath.IsInsideSweep(start, 340 * Deg, 340 * Deg, SweepDirection.CounterClockwise));
            Assert.False(AngleMath.IsInsideSweep(start, 355 * Deg, 340 * Deg, SweepDirection.CounterClockwise));
        }

        [Fact]
        public void RadiusFilteringIsInclusiveAtBoundary()
        {
            var candidates = new List<SweepTarget<int>>
            {
                new SweepTarget<int>(1, 0, 25),
                new SweepTarget<int>(2, 0, 25.0001)
            };
            var result = new List<ScheduledSweepTarget<int>>();
            SweepPlanner.BuildSchedule(candidates, 0, Math.PI, SweepDirection.CounterClockwise, 5, 0, result);
            Assert.Single(result);
            Assert.Equal(1, result[0].Value);
        }

        [Fact]
        public void OneHitPerAgentInvariant()
        {
            var registry = new HitRegistry<int>();
            Assert.True(registry.TryRegister(42));
            Assert.False(registry.TryRegister(42));
            Assert.Equal(1, registry.Count);
            registry.Clear();
            Assert.True(registry.TryRegister(42));
        }


        [Fact]
        public void HitRegistryEnforcesMaximumAcrossRepeatedSchedules()
        {
            var registry = new HitRegistry<int>();
            Assert.True(registry.TryRegister(1, 2));
            Assert.True(registry.TryRegister(2, 2));
            Assert.False(registry.TryRegister(3, 2));
            Assert.False(registry.TryRegister(2, 2));
            Assert.Equal(2, registry.Count);
        }

        [Fact]
        public void TargetAtSweepStartIsNotAlreadyPassedOnFirstTick()
        {
            var start = 0d;
            var sweep = 340 * Deg;
            Assert.False(AngleMath.HasSweepPassed(start, start, 0d, sweep, SweepDirection.Clockwise));
            Assert.Equal(0d, AngleMath.ProgressForTarget(start, start, sweep, SweepDirection.Clockwise), 8);
        }

        [Fact]
        public void DamageTimingMapsAngleToAnimationProgress()
        {
            var progress = AngleMath.ProgressForTarget(10 * Deg, 180 * Deg, 340 * Deg, SweepDirection.CounterClockwise);
            Assert.Equal(0.5, progress, 8);
            Assert.False(AngleMath.HasSweepPassed(10 * Deg, 180 * Deg, 0.49, 340 * Deg, SweepDirection.CounterClockwise));
            Assert.True(AngleMath.HasSweepPassed(10 * Deg, 180 * Deg, 0.51, 340 * Deg, SweepDirection.CounterClockwise, 0));
        }

        [Fact]
        public void DestinationFallbackSelectsNearestThenLowestVerticalDeltaThenOrdinal()
        {
            var candidates = new List<CandidateScore<string>>
            {
                new CandidateScore<string>("far", 4, 0, 0),
                new CandidateScore<string>("high", 1, 2, 1),
                new CandidateScore<string>("best", 1, 0.5, 2),
                new CandidateScore<string>("invalid", 0, 0, 3)
            };
            Assert.True(DestinationSelector.TrySelectBest(candidates, x => x != "invalid", out var selected));
            Assert.Equal("best", selected);
        }

        [Fact]
        public void ResourceCostsRespectUnlimitedAndDisabledModes()
        {
            var pool = new VoidEnergyPool(100);
            Assert.True(pool.TrySpend(35, false, false));
            Assert.Equal(65, pool.Current);
            Assert.False(pool.TrySpend(70, false, false));
            Assert.True(pool.TrySpend(70, true, false));
            Assert.Equal(65, pool.Current);
            Assert.True(pool.TrySpend(70, false, true));
            Assert.Equal(65, pool.Current);
        }

        [Fact]
        public void CooldownTransitionsToReadyAtZero()
        {
            var book = new CooldownBook();
            book.Start(AbilityId.Blink, 1);
            book.Tick(0.4f);
            Assert.Equal(0.6f, book.GetRemaining(AbilityId.Blink), 3);
            book.Tick(0.6f);
            Assert.True(book.IsReady(AbilityId.Blink));
        }

        [Fact]
        public void DominoRecursionGuardRejectsNestedPropagation()
        {
            var guard = new RecursionGuard<int>();
            using (var outer = guard.Enter(1))
            {
                Assert.NotNull(outer);
                Assert.Null(guard.Enter(1));
            }
            Assert.NotNull(guard.Enter(1));
        }

        [Fact]
        public void TimeStateOwnershipRestoresOnlyOwnedToken()
        {
            var ledger = new OwnershipLedger<float>();
            var first = ledger.Acquire(0.25f);
            var second = ledger.Acquire(0.5f);
            Assert.True(ledger.Release(first, out var firstValue));
            Assert.Equal(0.25f, firstValue);
            Assert.False(ledger.Owns(first));
            Assert.True(ledger.Owns(second));
            Assert.False(ledger.Release(999, out _));
        }

        [Fact]
        public void CancellationCleanupResetsCastAndPerCastRegistry()
        {
            var state = new CastStateMachine();
            var hits = new HitRegistry<int>();
            var token = state.Begin(AbilityId.VoidstepCleave);
            state.Transition(token, AbilityPhase.Validating);
            state.Transition(token, AbilityPhase.WindUp);
            hits.TryRegister(7);
            state.ForceReset(CancelReason.MissionEnded);
            hits.Clear();
            Assert.Equal(AbilityPhase.Idle, state.Phase);
            Assert.False(state.IsCasting);
            Assert.Equal(0, hits.Count);
            Assert.Equal(CancelReason.MissionEnded, state.LastCancelReason);
        }
    }
}
