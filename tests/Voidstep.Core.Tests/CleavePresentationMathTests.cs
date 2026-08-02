using Voidstep.Core;
using Xunit;

namespace Voidstep.Core.Tests
{
    public sealed class CleavePresentationMathTests
    {
        [Fact]
        public void BroaderCleaveUsesMoreOfWeaponActionWithoutSlowingDamageSweep()
        {
            var halfStart = CleavePresentationMath.CalculateActionStartProgress(180f);
            var fullStart = CleavePresentationMath.CalculateActionStartProgress(360f);
            var halfEnd = CleavePresentationMath.CalculateActionEndProgress(180f);
            var fullEnd = CleavePresentationMath.CalculateActionEndProgress(360f);

            Assert.True(fullStart < halfStart);
            Assert.True(fullEnd > halfEnd);
            Assert.Equal(0.22f, CleavePresentationMath.CalculateDuration(180f), 3);
            Assert.Equal(0.22f, CleavePresentationMath.CalculateDuration(360f), 3);
        }

        [Fact]
        public void LateAcquiredTargetBehindCurrentSweepIsDueImmediately()
        {
            Assert.True(CleavePresentationMath.IsLiveTargetDue(0.25, 0.70));
            Assert.True(CleavePresentationMath.IsLiveTargetDue(0.70, 0.70));
            Assert.False(CleavePresentationMath.IsLiveTargetDue(0.80, 0.70));
        }

        [Fact]
        public void MountedSweepHeightTargetsInfantryTorsoPlane()
        {
            Assert.Equal(11.25f, CleavePresentationMath.CalculateMountedSweepHeight(10f, 12.4f), 3);
            Assert.Equal(10.75f, CleavePresentationMath.CalculateMountedSweepHeight(10f, 10.8f), 3);
        }

        [Fact]
        public void VisualReachReflectsWeaponLengthWithoutExceedingRadius()
        {
            var shortWeapon = CleavePresentationMath.CalculateVisualReach(0.9f, 4.8f);
            var longWeapon = CleavePresentationMath.CalculateVisualReach(2.1f, 4.8f);

            Assert.True(longWeapon > shortWeapon);
            Assert.InRange(longWeapon, 0f, 4.8f);
        }
    }
}
