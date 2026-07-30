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
            if (!Enabled) return configured;
            return configured && Level(VoidstepSkillId.MomentumWeave) > 0;
        }

        internal bool AllowCompleteSuspension(bool configured)
        {
            if (!Enabled) return configured;
            return configured && Level(VoidstepSkillId.Chronomancer) >= 10;
        }

        internal bool AllowCooldownOnlyMode(bool configured)
        {
            if (!Enabled) return configured;
            return configured && Level(VoidstepSkillId.UnboundPower) >= 5;
        }

        internal bool AllowUnlimitedEnergy(bool configured)
        {
            if (!Enabled) return configured;
            return configured && Level(VoidstepSkillId.UnboundPower) >= 10;
        }

        internal bool AllowWallTraversal(bool configured)
        {
            return Enabled
                ? configured && (Level(VoidstepSkillId.MomentumWeave) >= 10 || Level(VoidstepSkillId.AvatarOfTheVoid) >= 5)
                : configured;
        }

        internal float EffectiveCleaveRadius(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.CleaveRadius(configured, Level) : configured;
        }

        internal float EffectiveCleaveSweepDegrees(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.CleaveSweepDegrees(configured, Level) : configured;
        }

        internal float EffectiveCleaveDamageMultiplier(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.CleaveDamageMultiplier(configured, Level) : configured;
        }

        internal float EffectiveCleaveKnockback(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.CleaveKnockback(configured, Level) : configured;
        }

        internal float EffectiveCleaveKnockdownThreshold(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.CleaveKnockdownThreshold(configured, Level) : configured;
        }

        internal int EffectiveMaximumCleaveTargets(int configured)
        {
            return Enabled ? VoidstepProgressionBalance.MaximumCleaveTargets(configured, Level) : configured;
        }

        internal float EffectiveVoidstepRange(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.VoidstepRange(configured, Level) : configured;
        }

        internal float EffectiveBlinkRange(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.BlinkRange(configured, Level) : configured;
        }

        internal float EffectiveWindblastAngle(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.WindblastAngle(configured, Level) : configured;
        }

        internal float EffectiveWindblastRange(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.WindblastRange(configured, Level) : configured;
        }

        internal float EffectiveWindblastForce(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.WindblastForce(configured, Level) : configured;
        }

        internal float EffectiveWindblastDamage(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.WindblastDamage(configured, Level) : configured;
        }

        internal float EffectiveBendTimeFactor(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.BendTimeFactor(configured, Level) : configured;
        }

        internal float EffectiveBendTimeDuration(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.BendTimeDuration(configured, Level) : configured;
        }

        internal int EffectiveDominoMaximumLinks(int configured)
        {
            return Enabled ? VoidstepProgressionBalance.DominoMaximumLinks(configured, Level) : configured;
        }

        internal float EffectiveDominoDamageFactor(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.DominoDamageFactor(configured, Level) : configured;
        }

        internal float EffectiveDominoRange(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.DominoRange(configured, Level) : configured;
        }

        internal float EffectiveDarkVisionRange(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.DarkVisionRange(configured, Level) : configured;
        }

        internal float EffectiveDarkVisionRefreshInterval(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.DarkVisionRefreshInterval(configured, Level) : configured;
        }
    }
}
