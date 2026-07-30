using System;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class VoidstepProgressionProfile
    {
        private readonly int[] _levels;

        private VoidstepProgressionProfile(bool enabled, int[] levels)
        {
            Enabled = enabled;
            _levels = levels ?? new int[VoidstepSkillCatalog.All.Count];
        }

        internal static readonly VoidstepProgressionProfile Disabled =
            new VoidstepProgressionProfile(false, new int[VoidstepSkillCatalog.All.Count]);

        internal bool Enabled { get; }

        internal static VoidstepProgressionProfile Build(VoidstepProgressionBehavior behavior)
        {
            if (behavior == null || !behavior.Enabled) return Disabled;
            var levels = new int[VoidstepSkillCatalog.All.Count];
            foreach (var skill in VoidstepSkillCatalog.All)
                levels[(int)skill.Id] = behavior.GetSkillLevel(skill.Id);
            return new VoidstepProgressionProfile(true, levels);
        }

        internal int Level(VoidstepSkillId id)
        {
            var index = (int)id;
            return index >= 0 && index < _levels.Length ? _levels[index] : 0;
        }

        internal bool CanUse(AbilityId ability, out string reason)
        {
            if (!Enabled)
            {
                reason = null;
                return true;
            }

            var required = VoidstepSkillCatalog.RequiredSkill(ability);
            if (Level(required) > 0)
            {
                reason = null;
                return true;
            }

            reason = AbilityPresentation.Name(ability) + " requires " + VoidstepSkillCatalog.ById[required].Name + " rank 1 in Voidstep Mastery.";
            return false;
        }

        internal float EffectiveMaximumEnergy(float configured)
        {
            if (!Enabled || Level(VoidstepSkillId.AvatarOfTheVoid) >= 10) return configured;
            var cap = VoidstepProgressionBalance.MaximumEnergyCap(
                Level(VoidstepSkillId.VoidAffinity),
                Level(VoidstepSkillId.DeepReservoir),
                Level(VoidstepSkillId.UnboundPower),
                Level(VoidstepSkillId.AvatarOfTheVoid));
            return Math.Max(1f, Math.Min(configured, cap));
        }

        internal float EffectiveEnergyRegeneration(float configured)
        {
            if (!Enabled || Level(VoidstepSkillId.AvatarOfTheVoid) >= 10) return configured;
            var cap = VoidstepProgressionBalance.EnergyRegenerationCap(
                Level(VoidstepSkillId.VoidAffinity),
                Level(VoidstepSkillId.RapidRecovery),
                Level(VoidstepSkillId.UnboundPower),
                Level(VoidstepSkillId.AvatarOfTheVoid));
            return Math.Max(0f, Math.Min(configured, cap));
        }

        internal float EffectiveCost(AbilityId ability, float configured)
        {
            if (!Enabled) return configured;
            return Math.Max(0f, configured * VoidstepProgressionBalance.CostMultiplier(ability, Level));
        }

        internal float EffectiveCooldown(AbilityId ability, float configured)
        {
            if (!Enabled) return configured;
            return Math.Max(0f, configured * VoidstepProgressionBalance.CooldownMultiplier(ability, Level));
        }

        internal bool AllowMomentumPreservation(bool configured)
        {
            return !Enabled || (configured && Level(VoidstepSkillId.MomentumWeave) > 0);
        }

        internal bool AllowCompleteSuspension(bool configured)
        {
            return !Enabled || (configured && Level(VoidstepSkillId.Chronomancer) >= 10);
        }

        internal bool AllowCooldownOnlyMode(bool configured)
        {
            return !Enabled || (configured && Level(VoidstepSkillId.UnboundPower) >= 5);
        }

        internal bool AllowUnlimitedEnergy(bool configured)
        {
            return !Enabled || (configured && Level(VoidstepSkillId.UnboundPower) >= 10);
        }
    }
}
