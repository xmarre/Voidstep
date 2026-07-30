using System;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class VoidstepProgressionProfile
    {
        private readonly int[] _levels;
        private readonly Func<VoidstepSkillId, int> _levelResolver;

        private VoidstepProgressionProfile(bool enabled, int[] levels)
        {
            Enabled = enabled;
            _levels = levels ?? new int[VoidstepSkillCatalog.All.Count];
            _levelResolver = Level;
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
            return Math.Max(0f, configured * VoidstepProgressionBalance.CostMultiplier(ability, _levelResolver));
        }

        internal float EffectiveCooldown(AbilityId ability, float configured)
        {
            if (!Enabled) return configured;
            return Math.Max(0f, configured * VoidstepProgressionBalance.CooldownMultiplier(ability, _levelResolver));
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
                ? configured && Level(VoidstepSkillId.MomentumWeave) >= 10
                : configured;
        }

        internal float EffectiveCleaveRadius(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.CleaveRadius(configured, _levelResolver) : configured;
        }

        internal float EffectiveCleaveSweepDegrees(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.CleaveSweepDegrees(configured, _levelResolver) : configured;
        }

        internal float EffectiveCleaveDamageMultiplier(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.CleaveDamageMultiplier(configured, _levelResolver) : configured;
        }

        internal float EffectiveCleaveKnockback(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.CleaveKnockback(configured, _levelResolver) : configured;
        }

        internal float EffectiveCleaveKnockdownThreshold(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.CleaveKnockdownThreshold(configured, _levelResolver) : configured;
        }

        internal int EffectiveMaximumCleaveTargets(int configured)
        {
            return Enabled ? VoidstepProgressionBalance.MaximumCleaveTargets(configured, _levelResolver) : configured;
        }

        internal float EffectiveVoidstepRange(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.VoidstepRange(configured, _levelResolver) : configured;
        }

        internal float EffectiveBlinkRange(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.BlinkRange(configured, _levelResolver) : configured;
        }

        internal float EffectiveWindblastAngle(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.WindblastAngle(configured, _levelResolver) : configured;
        }

        internal float EffectiveWindblastRange(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.WindblastRange(configured, _levelResolver) : configured;
        }

        internal float EffectiveWindblastForce(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.WindblastForce(configured, _levelResolver) : configured;
        }

        internal float EffectiveWindblastDamage(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.WindblastDamage(configured, _levelResolver) : configured;
        }

        internal float EffectiveBendTimeFactor(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.BendTimeFactor(configured, _levelResolver) : configured;
        }

        internal float EffectiveBendTimeDuration(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.BendTimeDuration(configured, _levelResolver) : configured;
        }

        internal int EffectiveDominoMaximumLinks(int configured)
        {
            return Enabled ? VoidstepProgressionBalance.DominoMaximumLinks(configured, _levelResolver) : configured;
        }

        internal float EffectiveDominoDamageFactor(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.DominoDamageFactor(configured, _levelResolver) : configured;
        }

        internal float EffectiveDominoRange(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.DominoRange(configured, _levelResolver) : configured;
        }

        internal float EffectiveDarkVisionRange(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.DarkVisionRange(configured, _levelResolver) : configured;
        }

        internal float EffectiveDarkVisionRefreshInterval(float configured)
        {
            return Enabled ? VoidstepProgressionBalance.DarkVisionRefreshInterval(configured, _levelResolver) : configured;
        }
    }
}
