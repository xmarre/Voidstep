using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Voidstep.Core;

namespace Voidstep
{
    internal enum VoidstepSkillId
    {
        VoidAffinity = 0,
        RiftStep = 1,
        PhaseRecovery = 2,
        MomentumWeave = 3,
        VoidDancer = 4,
        GaleForce = 5,
        CrushingWave = 6,
        BendTheHour = 7,
        Chronomancer = 8,
        FatefulLink = 9,
        SharedAgony = 10,
        UmbralSight = 11,
        SovereignGaze = 12,
        DeepReservoir = 13,
        EfficientChanneling = 14,
        RapidRecovery = 15,
        UnboundPower = 16,
        Singularity = 17,
        AvatarOfTheVoid = 18
    }

    internal sealed class VoidstepSkillRequirement
    {
        internal VoidstepSkillId Id;
        internal int Level;
    }

    internal sealed class VoidstepSkillDefinition
    {
        internal VoidstepSkillId Id;
        internal string Name;
        internal string Branch;
        internal string Glyph;
        internal string Description;
        internal int MaxLevel;
        internal int MasteryRank;
        internal int MeleeSkill;
        internal int TreeOrder;
        internal VoidstepSkillRequirement[] Prerequisites;
    }

    internal static class VoidstepProgressionBalance
    {
        internal const int MaximumMasteryRank = 99;

        internal static float MaximumEnergyCap(int affinity, int reservoir, int unbound, int avatar)
        {
            return 40f + 2f * Math.Max(0, affinity) + 4f * Math.Max(0, reservoir) +
                   6f * Math.Max(0, unbound) + 4f * Math.Max(0, avatar);
        }

        internal static float EnergyRegenerationCap(int affinity, int recovery, int unbound, int avatar)
        {
            return 1f + 0.1f * Math.Max(0, affinity) + 0.3f * Math.Max(0, recovery) +
                   0.5f * Math.Max(0, unbound) + 0.25f * Math.Max(0, avatar);
        }

        internal static float CostMultiplier(AbilityId ability, Func<VoidstepSkillId, int> level)
        {
            var multiplier = 1f;
            multiplier -= 0.0125f * level(VoidstepSkillId.EfficientChanneling);
            multiplier -= 0.01f * level(VoidstepSkillId.UnboundPower);
            multiplier -= 0.008f * level(VoidstepSkillId.Singularity);
            multiplier -= 0.006f * level(VoidstepSkillId.AvatarOfTheVoid);

            switch (ability)
            {
                case AbilityId.VoidstepCleave:
                    multiplier -= 0.008f * level(VoidstepSkillId.VoidAffinity);
                    multiplier -= 0.01f * level(VoidstepSkillId.VoidDancer);
                    break;
                case AbilityId.Blink:
                    multiplier -= 0.012f * level(VoidstepSkillId.PhaseRecovery);
                    multiplier -= 0.01f * level(VoidstepSkillId.VoidDancer);
                    break;
                case AbilityId.Windblast:
                    multiplier -= 0.012f * level(VoidstepSkillId.CrushingWave);
                    multiplier -= 0.006f * level(VoidstepSkillId.Chronomancer);
                    break;
                case AbilityId.BendTime:
                    multiplier -= 0.012f * level(VoidstepSkillId.BendTheHour);
                    multiplier -= 0.012f * level(VoidstepSkillId.Chronomancer);
                    break;
                case AbilityId.Domino:
                    multiplier -= 0.012f * level(VoidstepSkillId.SharedAgony);
                    multiplier -= 0.006f * level(VoidstepSkillId.SovereignGaze);
                    break;
                case AbilityId.DarkVision:
                    multiplier -= 0.012f * level(VoidstepSkillId.SovereignGaze);
                    multiplier -= 0.006f * level(VoidstepSkillId.SharedAgony);
                    break;
            }

            return Math.Max(0.35f, multiplier);
        }

        internal static float CooldownMultiplier(AbilityId ability, Func<VoidstepSkillId, int> level)
        {
            var multiplier = 1f;
            multiplier -= 0.0125f * level(VoidstepSkillId.RapidRecovery);
            multiplier -= 0.008f * level(VoidstepSkillId.Singularity);
            multiplier -= 0.006f * level(VoidstepSkillId.AvatarOfTheVoid);

            switch (ability)
            {
                case AbilityId.VoidstepCleave:
                case AbilityId.Blink:
                    multiplier -= 0.012f * level(VoidstepSkillId.PhaseRecovery);
                    multiplier -= 0.01f * level(VoidstepSkillId.VoidDancer);
                    break;
                case AbilityId.Windblast:
                    multiplier -= 0.01f * level(VoidstepSkillId.CrushingWave);
                    break;
                case AbilityId.BendTime:
                    multiplier -= 0.012f * level(VoidstepSkillId.Chronomancer);
                    break;
                case AbilityId.Domino:
                    multiplier -= 0.01f * level(VoidstepSkillId.SharedAgony);
                    break;
                case AbilityId.DarkVision:
                    multiplier -= 0.012f * level(VoidstepSkillId.SovereignGaze);
                    break;
            }

            return Math.Max(0.35f, multiplier);
        }
    }

    internal static class VoidstepSkillCatalog
    {
        internal static readonly IReadOnlyList<VoidstepSkillDefinition> All = new[]
        {
            D(VoidstepSkillId.VoidAffinity, "Void Affinity", "Core", "◆", 20, 1, 25, 0,
                "The centre of the tree. Rank 1 unlocks Voidstep Cleave; further ranks deepen the connection that supports every branch."),

            D(VoidstepSkillId.RiftStep, "Rift Step", "Mobility", "↯", 20, 2, 50, 1,
                "Rank 1 unlocks Blink. Further ranks establish the mobility branch and prepare more efficient recovery.", R(VoidstepSkillId.VoidAffinity, 3)),
            D(VoidstepSkillId.PhaseRecovery, "Phase Recovery", "Mobility", "⌁", 20, 8, 75, 2,
                "Reduces Blink and Voidstep Cleave energy costs and cooldowns without changing the configured targeting rules.", R(VoidstepSkillId.RiftStep, 5)),
            D(VoidstepSkillId.MomentumWeave, "Momentum Weave", "Mobility", "➤", 10, 20, 100, 3,
                "Rank 1 permits the configured Blink momentum-preservation option. Higher ranks strengthen the route to the mobility capstone.", R(VoidstepSkillId.PhaseRecovery, 5)),
            D(VoidstepSkillId.VoidDancer, "Void Dancer", "Mobility", "✦", 10, 40, 150, 4,
                "A mobility capstone that further reduces Cleave and Blink costs and cooldowns.", R(VoidstepSkillId.MomentumWeave, 5)),

            D(VoidstepSkillId.GaleForce, "Gale Force", "Force", "≋", 20, 5, 75, 1,
                "Rank 1 unlocks Windblast and opens the force branch.", R(VoidstepSkillId.VoidAffinity, 5)),
            D(VoidstepSkillId.CrushingWave, "Crushing Wave", "Force", "◈", 20, 15, 100, 2,
                "Reduces Windblast energy cost and cooldown while preserving the configured cone, force and damage ceilings.", R(VoidstepSkillId.GaleForce, 5)),
            D(VoidstepSkillId.BendTheHour, "Bend the Hour", "Force", "⌛", 20, 25, 125, 3,
                "Rank 1 unlocks Bend Time. Further ranks reduce its energy cost.", R(VoidstepSkillId.CrushingWave, 5)),
            D(VoidstepSkillId.Chronomancer, "Chronomancer", "Force", "◷", 10, 45, 175, 4,
                "Reduces Bend Time cost and cooldown. Rank 10 permits the configured complete-suspension option.", R(VoidstepSkillId.BendTheHour, 10)),

            D(VoidstepSkillId.FatefulLink, "Fateful Link", "Dominion", "∞", 20, 5, 75, 1,
                "Rank 1 unlocks Domino and opens the dominion branch.", R(VoidstepSkillId.VoidAffinity, 5)),
            D(VoidstepSkillId.SharedAgony, "Shared Agony", "Dominion", "⛓", 20, 18, 100, 2,
                "Reduces Domino energy cost and cooldown while leaving propagation strength under the normal MCM limits.", R(VoidstepSkillId.FatefulLink, 5)),
            D(VoidstepSkillId.UmbralSight, "Umbral Sight", "Dominion", "◉", 20, 10, 75, 3,
                "Rank 1 unlocks Dark Vision and establishes the perception side of dominion.", R(VoidstepSkillId.VoidAffinity, 5)),
            D(VoidstepSkillId.SovereignGaze, "Sovereign Gaze", "Dominion", "☉", 10, 35, 150, 4,
                "Reduces Dark Vision energy cost and cooldown and contributes to advanced dominion efficiency.", R(VoidstepSkillId.UmbralSight, 10)),

            D(VoidstepSkillId.DeepReservoir, "Deep Reservoir", "Reservoir", "◇", 20, 2, 50, 1,
                "Raises the maximum Void Energy available while progression is enabled.", R(VoidstepSkillId.VoidAffinity, 3)),
            D(VoidstepSkillId.EfficientChanneling, "Efficient Channeling", "Reservoir", "△", 20, 10, 75, 2,
                "Reduces the energy cost of every Voidstep ability.", R(VoidstepSkillId.DeepReservoir, 5)),
            D(VoidstepSkillId.RapidRecovery, "Rapid Recovery", "Reservoir", "↻", 20, 20, 100, 3,
                "Raises Void Energy regeneration and reduces every ability cooldown.", R(VoidstepSkillId.EfficientChanneling, 5)),
            D(VoidstepSkillId.UnboundPower, "Unbound Power", "Reservoir", "✹", 10, 45, 175, 4,
                "Raises energy capacity and regeneration. Rank 5 permits cooldown-only mode; rank 10 permits unlimited energy.", R(VoidstepSkillId.RapidRecovery, 10)),

            D(VoidstepSkillId.Singularity, "Singularity", "Convergence", "⊙", 10, 60, 200, 1,
                "Combines mobility, force and dominion mastery to reduce all energy costs and cooldowns.",
                R(VoidstepSkillId.VoidDancer, 5), R(VoidstepSkillId.Chronomancer, 5),
                R(VoidstepSkillId.SharedAgony, 10), R(VoidstepSkillId.SovereignGaze, 5)),
            D(VoidstepSkillId.AvatarOfTheVoid, "Avatar of the Void", "Convergence", "✺", 10, 75, 225, 2,
                "The final convergence. Rank 10 releases progression caps on configured maximum energy and regeneration.",
                R(VoidstepSkillId.Singularity, 5), R(VoidstepSkillId.UnboundPower, 5))
        };

        internal static readonly IReadOnlyDictionary<VoidstepSkillId, VoidstepSkillDefinition> ById =
            All.ToDictionary(skill => skill.Id);

        internal static int GetThreshold(int rank)
        {
            rank = Math.Max(1, Math.Min(VoidstepProgressionBalance.MaximumMasteryRank, rank));
            long n = rank - 1;
            long late = Math.Max(0, n - 50);
            long value = 6L * n * n + 20L * n + (long)Math.Round(0.08d * late * late * late);
            return (int)Math.Min(int.MaxValue, value);
        }

        internal static int GetRank(int xp)
        {
            xp = Math.Max(0, xp);
            var low = 1;
            var high = VoidstepProgressionBalance.MaximumMasteryRank;
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (xp >= GetThreshold(middle)) low = middle;
                else high = middle - 1;
            }
            return low;
        }

        internal static int GetNextThreshold(int rank)
        {
            return rank >= VoidstepProgressionBalance.MaximumMasteryRank
                ? GetThreshold(VoidstepProgressionBalance.MaximumMasteryRank)
                : GetThreshold(rank + 1);
        }

        internal static int GetInvestedPoints(Func<VoidstepSkillId, int> levelResolver)
        {
            if (levelResolver == null) return 0;
            var total = 0;
            foreach (var skill in All)
                total += Math.Max(0, Math.Min(skill.MaxLevel, levelResolver(skill.Id)));
            return total;
        }

        internal static string GetRequirementText(VoidstepSkillDefinition skill)
        {
            if (skill == null) return string.Empty;
            var parts = new List<string>
            {
                "Mastery " + skill.MasteryRank,
                "Melee Skill " + skill.MeleeSkill
            };
            foreach (var requirement in skill.Prerequisites)
                parts.Add(ById[requirement.Id].Name + " " + requirement.Level);
            return string.Join(" • ", parts);
        }

        internal static VoidstepSkillId RequiredSkill(AbilityId ability)
        {
            switch (ability)
            {
                case AbilityId.VoidstepCleave: return VoidstepSkillId.VoidAffinity;
                case AbilityId.Blink: return VoidstepSkillId.RiftStep;
                case AbilityId.Windblast: return VoidstepSkillId.GaleForce;
                case AbilityId.BendTime: return VoidstepSkillId.BendTheHour;
                case AbilityId.Domino: return VoidstepSkillId.FatefulLink;
                case AbilityId.DarkVision: return VoidstepSkillId.UmbralSight;
                default: return VoidstepSkillId.VoidAffinity;
            }
        }

        internal static string GetEffectText(VoidstepSkillId id, int level)
        {
            level = Math.Max(0, level);
            switch (id)
            {
                case VoidstepSkillId.VoidAffinity:
                    return level <= 0 ? "Rank 1 unlocks Voidstep Cleave." : "Voidstep Cleave unlocked. Energy-cap contribution: +" + (2 * level) + ".";
                case VoidstepSkillId.RiftStep:
                    return level <= 0 ? "Rank 1 unlocks Blink." : "Blink unlocked. Mobility foundation level " + level + ".";
                case VoidstepSkillId.PhaseRecovery:
                    return "Cleave/Blink cost and cooldown reduction: up to " + F(1.2f * level, 1) + "% each before capstones.";
                case VoidstepSkillId.MomentumWeave:
                    return level <= 0 ? "Rank 1 permits configured momentum preservation." : "Momentum preservation permitted. Capstone path level " + level + ".";
                case VoidstepSkillId.VoidDancer:
                    return "Additional Cleave/Blink cost and cooldown reduction: " + level + "% each.";
                case VoidstepSkillId.GaleForce:
                    return level <= 0 ? "Rank 1 unlocks Windblast." : "Windblast unlocked. Force foundation level " + level + ".";
                case VoidstepSkillId.CrushingWave:
                    return "Windblast cost reduction: " + F(1.2f * level, 1) + "%. Cooldown reduction: " + F(level, 1) + "%.";
                case VoidstepSkillId.BendTheHour:
                    return level <= 0 ? "Rank 1 unlocks Bend Time." : "Bend Time unlocked. Cost reduction: " + F(1.2f * level, 1) + "%.";
                case VoidstepSkillId.Chronomancer:
                    return (level >= 10 ? "Configured complete suspension permitted. " : string.Empty) +
                           "Bend Time cost/cooldown reduction: " + F(1.2f * level, 1) + "% each.";
                case VoidstepSkillId.FatefulLink:
                    return level <= 0 ? "Rank 1 unlocks Domino." : "Domino unlocked. Dominion foundation level " + level + ".";
                case VoidstepSkillId.SharedAgony:
                    return "Domino cost reduction: " + F(1.2f * level, 1) + "%. Cooldown reduction: " + F(level, 1) + "%.";
                case VoidstepSkillId.UmbralSight:
                    return level <= 0 ? "Rank 1 unlocks Dark Vision." : "Dark Vision unlocked. Perception foundation level " + level + ".";
                case VoidstepSkillId.SovereignGaze:
                    return "Dark Vision cost/cooldown reduction: " + F(1.2f * level, 1) + "% each.";
                case VoidstepSkillId.DeepReservoir:
                    return "Maximum energy contribution: +" + (4 * level) + ".";
                case VoidstepSkillId.EfficientChanneling:
                    return "Global energy-cost reduction: " + F(1.25f * level, 2) + "%.";
                case VoidstepSkillId.RapidRecovery:
                    return "Regeneration contribution: +" + F(0.3f * level, 1) + "/s. Global cooldown reduction: " + F(1.25f * level, 2) + "%.";
                case VoidstepSkillId.UnboundPower:
                    return "Maximum energy: +" + (6 * level) + ". Regeneration: +" + F(0.5f * level, 1) + "/s." +
                           (level >= 5 ? " Cooldown-only mode permitted." : string.Empty) +
                           (level >= 10 ? " Unlimited energy permitted." : string.Empty);
                case VoidstepSkillId.Singularity:
                    return "Global energy-cost and cooldown reduction: " + F(0.8f * level, 1) + "% each.";
                case VoidstepSkillId.AvatarOfTheVoid:
                    return level >= 10
                        ? "Configured maximum energy and regeneration are unrestricted by progression."
                        : "Energy cap: +" + (4 * level) + ". Regeneration: +" + F(0.25f * level, 2) + "/s; global cost/cooldown reduction: " + F(0.6f * level, 1) + "%.";
                default:
                    return string.Empty;
            }
        }

        internal static string GetNextLevelText(VoidstepSkillDefinition skill, int currentLevel)
        {
            if (skill == null) return string.Empty;
            if (currentLevel >= skill.MaxLevel) return "Maximum level reached.";
            return "Next level: " + GetEffectText(skill.Id, currentLevel + 1);
        }

        private static VoidstepSkillDefinition D(
            VoidstepSkillId id,
            string name,
            string branch,
            string glyph,
            int maxLevel,
            int masteryRank,
            int meleeSkill,
            int treeOrder,
            string description,
            params VoidstepSkillRequirement[] prerequisites)
        {
            return new VoidstepSkillDefinition
            {
                Id = id,
                Name = name,
                Branch = branch,
                Glyph = glyph,
                MaxLevel = maxLevel,
                MasteryRank = masteryRank,
                MeleeSkill = meleeSkill,
                TreeOrder = treeOrder,
                Description = description,
                Prerequisites = prerequisites ?? Array.Empty<VoidstepSkillRequirement>()
            };
        }

        private static VoidstepSkillRequirement R(VoidstepSkillId id, int level)
        {
            return new VoidstepSkillRequirement { Id = id, Level = level };
        }

        private static string F(float value, int decimals)
        {
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }
    }
}
