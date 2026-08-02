using Voidstep.Core;
using Xunit;

namespace Voidstep.Core.Tests
{
    public sealed class CleavePresentationMathTests
    {
        [Fact]
        public void BroaderCleaveUsesLongerPresentationAndMoreOfWeaponAction()
        {
            var halfDuration = CleavePresentationMath.CalculateDuration(180f);
            var fullDuration = CleavePresentationMath.CalculateDuration(360f);
            var halfStart = CleavePresentationMath.CalculateActionStartProgress(180f);
            var fullStart = CleavePresentationMath.CalculateActionStartProgress(360f);
            var halfEnd = CleavePresentationMath.CalculateActionEndProgress(180f);
            var fullEnd = CleavePresentationMath.CalculateActionEndProgress(360f);

            Assert.Equal(0.39f, halfDuration, 3);
            Assert.Equal(0.60f, fullDuration, 3);
            Assert.True(fullStart < halfStart);
            Assert.True(fullEnd > halfEnd);
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
