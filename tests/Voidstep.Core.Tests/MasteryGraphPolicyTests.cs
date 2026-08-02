using System.Collections.Generic;
using System.Linq;
using Voidstep.Core;
using Xunit;

namespace Voidstep.Core.Tests
{
    public sealed class MasteryGraphPolicyTests
    {
        [Theory]
        [InlineData(MasteryGraphPolicy.VoidAffinity)]
        [InlineData(MasteryGraphPolicy.RiftStep)]
        [InlineData(MasteryGraphPolicy.GaleForce)]
        [InlineData(MasteryGraphPolicy.BendTheHour)]
        [InlineData(MasteryGraphPolicy.FatefulLink)]
        [InlineData(MasteryGraphPolicy.UmbralSight)]
        [InlineData(MasteryGraphPolicy.DeepReservoir)]
        public void FoundationSkillsHaveNoCrossBranchPrerequisites(int skillId)
        {
            Assert.Empty(MasteryGraphPolicy.GetRequirements(skillId));
        }

        [Theory]
        [MemberData(nameof(SamePathRequirements))]
        public void AdvancedSkillsRequireOnlyTheirOwnPreviousNode(
            int skillId,
            int prerequisiteId,
            int prerequisiteLevel)
        {
            var requirement = Assert.Single(MasteryGraphPolicy.GetRequirements(skillId));
            Assert.Equal(prerequisiteId, requirement.SkillId);
            Assert.Equal(prerequisiteLevel, requirement.Level);
        }

        [Fact]
        public void SingularityRequiresOnlyOneRankInEachAbilityFoundation()
        {
            var requirements = MasteryGraphPolicy.GetRequirements(MasteryGraphPolicy.Singularity);
            Assert.Collection(
                requirements,
                requirement => AssertRequirement(requirement, MasteryGraphPolicy.VoidAffinity, 1),
                requirement => AssertRequirement(requirement, MasteryGraphPolicy.RiftStep, 1),
                requirement => AssertRequirement(requirement, MasteryGraphPolicy.GaleForce, 1),
                requirement => AssertRequirement(requirement, MasteryGraphPolicy.BendTheHour, 1),
                requirement => AssertRequirement(requirement, MasteryGraphPolicy.FatefulLink, 1),
                requirement => AssertRequirement(requirement, MasteryGraphPolicy.UmbralSight, 1));
        }

        [Fact]
        public void AvatarRetainsOnlyIntentionalConvergenceRequirements()
        {
            var requirements = MasteryGraphPolicy.GetRequirements(MasteryGraphPolicy.AvatarOfTheVoid);
            Assert.Collection(
                requirements,
                requirement => AssertRequirement(requirement, MasteryGraphPolicy.Singularity, 5),
                requirement => AssertRequirement(requirement, MasteryGraphPolicy.UnboundPower, 5));
        }

        [Fact]
        public void StableSkillIdsRemainContiguousForExistingSaveKeys()
        {
            var ids = new[]
            {
                MasteryGraphPolicy.VoidAffinity,
                MasteryGraphPolicy.RiftStep,
                MasteryGraphPolicy.PhaseRecovery,
                MasteryGraphPolicy.MomentumWeave,
                MasteryGraphPolicy.VoidDancer,
                MasteryGraphPolicy.GaleForce,
                MasteryGraphPolicy.CrushingWave,
                MasteryGraphPolicy.BendTheHour,
                MasteryGraphPolicy.Chronomancer,
                MasteryGraphPolicy.FatefulLink,
                MasteryGraphPolicy.SharedAgony,
                MasteryGraphPolicy.UmbralSight,
                MasteryGraphPolicy.SovereignGaze,
                MasteryGraphPolicy.DeepReservoir,
                MasteryGraphPolicy.EfficientChanneling,
                MasteryGraphPolicy.RapidRecovery,
                MasteryGraphPolicy.UnboundPower,
                MasteryGraphPolicy.Singularity,
                MasteryGraphPolicy.AvatarOfTheVoid
            };

            Assert.Equal(MasteryGraphPolicy.SkillCount, ids.Length);
            Assert.Equal(Enumerable.Range(0, MasteryGraphPolicy.SkillCount), ids);
        }

        public static IEnumerable<object[]> SamePathRequirements()
        {
            yield return Row(MasteryGraphPolicy.PhaseRecovery, MasteryGraphPolicy.RiftStep, 5);
            yield return Row(MasteryGraphPolicy.MomentumWeave, MasteryGraphPolicy.PhaseRecovery, 5);
            yield return Row(MasteryGraphPolicy.VoidDancer, MasteryGraphPolicy.MomentumWeave, 5);
            yield return Row(MasteryGraphPolicy.CrushingWave, MasteryGraphPolicy.GaleForce, 5);
            yield return Row(MasteryGraphPolicy.Chronomancer, MasteryGraphPolicy.BendTheHour, 5);
            yield return Row(MasteryGraphPolicy.SharedAgony, MasteryGraphPolicy.FatefulLink, 5);
            yield return Row(MasteryGraphPolicy.SovereignGaze, MasteryGraphPolicy.UmbralSight, 5);
            yield return Row(MasteryGraphPolicy.EfficientChanneling, MasteryGraphPolicy.DeepReservoir, 5);
            yield return Row(MasteryGraphPolicy.RapidRecovery, MasteryGraphPolicy.EfficientChanneling, 5);
            yield return Row(MasteryGraphPolicy.UnboundPower, MasteryGraphPolicy.RapidRecovery, 5);
        }

        private static object[] Row(int skillId, int prerequisiteId, int prerequisiteLevel) =>
            new object[] { skillId, prerequisiteId, prerequisiteLevel };

        private static void AssertRequirement(
            MasteryRequirementSpec requirement,
            int skillId,
            int level)
        {
            Assert.Equal(skillId, requirement.SkillId);
            Assert.Equal(level, requirement.Level);
        }
    }
}
