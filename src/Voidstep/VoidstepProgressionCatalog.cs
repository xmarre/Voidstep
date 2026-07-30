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
            multiplier -= 0.0075f * level(VoidstepSkillId.EfficientChanneling);
            multiplier -= 0.005f * level(VoidstepSkillId.UnboundPower);
            multiplier -= 0.004f * level(VoidstepSkillId.Singularity);
            multiplier -= 0.003f * level(VoidstepSkillId.AvatarOfTheVoid);

            switch (ability)
            {
                case AbilityId.VoidstepCleave:
                    multiplier -= 0.004f * level(VoidstepSkillId.VoidAffinity);
                    multiplier -= 0.006f * level(VoidstepSkillId.VoidDancer);
                    break;
                case AbilityId.Blink:
                    multiplier -= 0.004f * level(VoidstepSkillId.PhaseRecovery);
                    multiplier -= 0.006f * level(VoidstepSkillId.VoidDancer);
                    break;
                case AbilityId.Windblast:
                    multiplier -= 0.005f * level(VoidstepSkillId.CrushingWave);
                    break;
                case AbilityId.BendTime:
                    multiplier -= 0.004f * level(VoidstepSkillId.BendTheHour);
                    multiplier -= 0.006f * level(VoidstepSkillId.Chronomancer);
                    break;
                case AbilityId.Domino:
                    multiplier -= 0.005f * level(VoidstepSkillId.SharedAgony);
                    break;
                case AbilityId.DarkVision:
                    multiplier -= 0.006f * level(VoidstepSkillId.SovereignGaze);
                    break;
            }

            return Math.Max(0.65f, multiplier);
        }

        internal static float CooldownMultiplier(AbilityId ability, Func<VoidstepSkillId, int> level)
        {
            var multiplier = 1f;
            multiplier -= 0.006f * level(VoidstepSkillId.RapidRecovery);
            multiplier -= 0.004f * level(VoidstepSkillId.Singularity);
            multiplier -= 0.003f * level(VoidstepSkillId.AvatarOfTheVoid);

            switch (ability)
            {
                case AbilityId.VoidstepCleave:
                case AbilityId.Blink:
                    multiplier -= 0.004f * level(VoidstepSkillId.PhaseRecovery);
                    multiplier -= 0.006f * level(VoidstepSkillId.VoidDancer);
                    break;
                case AbilityId.Windblast:
                    multiplier -= 0.004f * level(VoidstepSkillId.CrushingWave);
                    break;
                case AbilityId.BendTime:
                    multiplier -= 0.006f * level(VoidstepSkillId.Chronomancer);
                    break;
                case AbilityId.Domino:
                    multiplier -= 0.005f * level(VoidstepSkillId.SharedAgony);
                    break;
                case AbilityId.DarkVision:
                    multiplier -= 0.006f * level(VoidstepSkillId.SovereignGaze);
                    break;
            }

            return Math.Max(0.65f, multiplier);
        }

        internal static float CleaveRadius(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.65f + 0.018f * level(VoidstepSkillId.VoidAffinity) +
                        0.025f * level(VoidstepSkillId.PhaseRecovery) +
                        0.03f * level(VoidstepSkillId.VoidDancer) +
                        0.01f * level(VoidstepSkillId.UnboundPower) +
                        0.018f * level(VoidstepSkillId.Singularity) +
                        0.02f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Min(1.75f, scale), 1f, 14f);
        }

        internal static float CleaveSweepDegrees(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.68f + 0.015f * level(VoidstepSkillId.VoidAffinity) +
                        0.025f * level(VoidstepSkillId.VoidDancer) +
                        0.015f * level(VoidstepSkillId.Singularity) +
                        0.02f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * scale, 180f, 360f);
        }

        internal static float CleaveDamageMultiplier(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.7f + 0.02f * level(VoidstepSkillId.VoidAffinity) +
                        0.025f * level(VoidstepSkillId.VoidDancer) +
                        0.015f * level(VoidstepSkillId.CrushingWave) +
                        0.01f * level(VoidstepSkillId.UnboundPower) +
                        0.02f * level(VoidstepSkillId.Singularity) +
                        0.025f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Min(1.8f, scale), 0.1f, 7.5f);
        }

        internal static float CleaveKnockback(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.65f + 0.025f * level(VoidstepSkillId.VoidAffinity) +
                        0.03f * level(VoidstepSkillId.VoidDancer) +
                        0.02f * level(VoidstepSkillId.CrushingWave) +
                        0.02f * level(VoidstepSkillId.Singularity) +
                        0.025f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Min(2f, scale), 0f, 30f);
        }

        internal static float CleaveKnockdownThreshold(float configured, Func<VoidstepSkillId, int> level)
        {
            if (configured <= 0f) return configured;
            var scale = 1.25f - 0.015f * level(VoidstepSkillId.VoidAffinity) -
                        0.02f * level(VoidstepSkillId.VoidDancer) -
                        0.015f * level(VoidstepSkillId.CrushingWave) -
                        0.015f * level(VoidstepSkillId.Singularity) -
                        0.02f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Max(0.35f, scale), 0f, 100f);
        }

        internal static int MaximumCleaveTargets(int configured, Func<VoidstepSkillId, int> level)
        {
            if (configured == 0 && level(VoidstepSkillId.AvatarOfTheVoid) >= 10) return 0;
            var progressionCap = 2 + (int)Math.Ceiling(level(VoidstepSkillId.VoidAffinity) / 3f) +
                                 level(VoidstepSkillId.PhaseRecovery) / 4 +
                                 2 * level(VoidstepSkillId.VoidDancer) +
                                 2 * level(VoidstepSkillId.Singularity) +
                                 3 * level(VoidstepSkillId.AvatarOfTheVoid);
            progressionCap = Math.Max(1, Math.Min(200, progressionCap));
            return configured <= 0 ? progressionCap : Math.Min(configured, progressionCap);
        }

        internal static float VoidstepRange(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.55f + 0.02f * level(VoidstepSkillId.VoidAffinity) +
                        0.03f * level(VoidstepSkillId.PhaseRecovery) +
                        0.035f * level(VoidstepSkillId.VoidDancer) +
                        0.01f * level(VoidstepSkillId.UnboundPower) +
                        0.02f * level(VoidstepSkillId.Singularity) +
                        0.02f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Min(1.9f, scale), 1f, 45f);
        }

        internal static float BlinkRange(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.55f + 0.025f * level(VoidstepSkillId.RiftStep) +
                        0.03f * level(VoidstepSkillId.PhaseRecovery) +
                        0.04f * level(VoidstepSkillId.VoidDancer) +
                        0.01f * level(VoidstepSkillId.UnboundPower) +
                        0.02f * level(VoidstepSkillId.Singularity) +
                        0.02f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Min(2f, scale), 1f, 45f);
        }

        internal static float WindblastAngle(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.65f + 0.02f * level(VoidstepSkillId.GaleForce) +
                        0.025f * level(VoidstepSkillId.CrushingWave) +
                        0.01f * level(VoidstepSkillId.UnboundPower) +
                        0.015f * level(VoidstepSkillId.Singularity) +
                        0.02f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Min(1.6f, scale), 10f, 160f);
        }

        internal static float WindblastRange(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.55f + 0.025f * level(VoidstepSkillId.GaleForce) +
                        0.03f * level(VoidstepSkillId.CrushingWave) +
                        0.01f * level(VoidstepSkillId.UnboundPower) +
                        0.02f * level(VoidstepSkillId.Singularity) +
                        0.02f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Min(1.9f, scale), 1f, 45f);
        }

        internal static float WindblastForce(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.55f + 0.025f * level(VoidstepSkillId.GaleForce) +
                        0.035f * level(VoidstepSkillId.CrushingWave) +
                        0.015f * level(VoidstepSkillId.UnboundPower) +
                        0.02f * level(VoidstepSkillId.Singularity) +
                        0.025f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Min(2f, scale), 0f, 45f);
        }

        internal static float WindblastDamage(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.5f + 0.02f * level(VoidstepSkillId.GaleForce) +
                        0.04f * level(VoidstepSkillId.CrushingWave) +
                        0.02f * level(VoidstepSkillId.UnboundPower) +
                        0.02f * level(VoidstepSkillId.Singularity) +
                        0.03f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Min(2f, scale), 0f, 350f);
        }

        internal static float BendTimeDuration(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.55f + 0.02f * level(VoidstepSkillId.BendTheHour) +
                        0.04f * level(VoidstepSkillId.Chronomancer) +
                        0.01f * level(VoidstepSkillId.UnboundPower) +
                        0.02f * level(VoidstepSkillId.Singularity) +
                        0.025f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Min(1.8f, scale), 0.25f, 45f);
        }

        internal static float BendTimeFactor(float configured, Func<VoidstepSkillId, int> level)
        {
            var power = 0.2f + 0.025f * level(VoidstepSkillId.BendTheHour) +
                        0.03f * level(VoidstepSkillId.Chronomancer) +
                        0.01f * level(VoidstepSkillId.UnboundPower) +
                        0.015f * level(VoidstepSkillId.Singularity) +
                        0.02f * level(VoidstepSkillId.AvatarOfTheVoid);
            power = Math.Min(1.2f, power);
            return Clamp(1f - (1f - configured) * power, 0.02f, 1f);
        }

        internal static int DominoMaximumLinks(int configured, Func<VoidstepSkillId, int> level)
        {
            var progressionCap = 2 + (int)Math.Ceiling(level(VoidstepSkillId.FatefulLink) / 3f) +
                                 level(VoidstepSkillId.SharedAgony) / 2 +
                                 level(VoidstepSkillId.SovereignGaze) / 2 +
                                 level(VoidstepSkillId.Singularity) / 2 +
                                 level(VoidstepSkillId.AvatarOfTheVoid);
            progressionCap = Math.Max(2, Math.Min(30, progressionCap));
            return Math.Max(2, Math.Min(configured, progressionCap));
        }

        internal static float DominoDamageFactor(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.45f + 0.025f * level(VoidstepSkillId.FatefulLink) +
                        0.03f * level(VoidstepSkillId.SharedAgony) +
                        0.02f * level(VoidstepSkillId.SovereignGaze) +
                        0.01f * level(VoidstepSkillId.UnboundPower) +
                        0.02f * level(VoidstepSkillId.Singularity) +
                        0.025f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Min(1.5f, scale), 0f, 1f);
        }

        internal static float DominoRange(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.55f + 0.02f * level(VoidstepSkillId.FatefulLink) +
                        0.03f * level(VoidstepSkillId.SharedAgony) +
                        0.025f * level(VoidstepSkillId.SovereignGaze) +
                        0.01f * level(VoidstepSkillId.UnboundPower) +
                        0.02f * level(VoidstepSkillId.Singularity) +
                        0.02f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Min(1.8f, scale), 1f, 45f);
        }

        internal static float DarkVisionRange(float configured, Func<VoidstepSkillId, int> level)
        {
            var scale = 0.5f + 0.025f * level(VoidstepSkillId.UmbralSight) +
                        0.035f * level(VoidstepSkillId.SovereignGaze) +
                        0.01f * level(VoidstepSkillId.UnboundPower) +
                        0.02f * level(VoidstepSkillId.Singularity) +
                        0.02f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured * Math.Min(1.8f, scale), 5f, 150f);
        }

        internal static float DarkVisionRefreshInterval(float configured, Func<VoidstepSkillId, int> level)
        {
            var speed = 0.75f + 0.03f * level(VoidstepSkillId.UmbralSight) +
                        0.06f * level(VoidstepSkillId.SovereignGaze) +
                        0.02f * level(VoidstepSkillId.UnboundPower) +
                        0.03f * level(VoidstepSkillId.Singularity) +
                        0.04f * level(VoidstepSkillId.AvatarOfTheVoid);
            return Clamp(configured / Math.Max(0.25f, speed), 0.1f, 3f);
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }

    internal static class VoidstepSkillCatalog
    {
        internal static readonly IReadOnlyList<VoidstepSkillDefinition> All = new[]
        {
            D(VoidstepSkillId.VoidAffinity, "Void Affinity", "Core", "◆", 20, 1, 25, 0,
                "Unlocks Voidstep Cleave and directly grows its radius, teleport range, sweep, damage, knockback and target capacity."),

            D(VoidstepSkillId.RiftStep, "Rift Step", "Mobility", "↯", 20, 2, 50, 1,
                "Unlocks Blink. Every rank increases Blink distance instead of merely making the cast cheaper.", R(VoidstepSkillId.VoidAffinity, 3)),
            D(VoidstepSkillId.PhaseRecovery, "Rift Reach", "Mobility", "⌁", 20, 8, 75, 2,
                "Expands both teleport abilities: Blink range, Voidstep teleport range, Cleave radius and Cleave target capacity.", R(VoidstepSkillId.RiftStep, 5)),
            D(VoidstepSkillId.MomentumWeave, "Momentum Weave", "Mobility", "➤", 10, 20, 100, 3,
                "Rank 1 permits configured momentum preservation. Rank 10 permits configured sealed-wall traversal.", R(VoidstepSkillId.PhaseRecovery, 5)),
            D(VoidstepSkillId.VoidDancer, "Void Dancer", "Mobility", "✦", 10, 40, 150, 4,
                "Mobility capstone: greatly expands Blink and Voidstep range while widening, strengthening and raising the target cap of Cleave.", R(VoidstepSkillId.MomentumWeave, 5)),

            D(VoidstepSkillId.GaleForce, "Gale Force", "Force", "≋", 20, 5, 75, 1,
                "Unlocks Windblast and grows its cone angle, range, force and damage with every rank.", R(VoidstepSkillId.VoidAffinity, 5)),
            D(VoidstepSkillId.CrushingWave, "Crushing Wave", "Force", "◈", 20, 15, 100, 2,
                "Turns Windblast into a heavy battlefield tool by sharply increasing force and damage, while also reinforcing Cleave impact.", R(VoidstepSkillId.GaleForce, 5)),
            D(VoidstepSkillId.BendTheHour, "Bend the Hour", "Force", "⌛", 20, 25, 125, 3,
                "Unlocks Bend Time. Every rank increases duration and deepens the world slowdown.", R(VoidstepSkillId.CrushingWave, 5)),
            D(VoidstepSkillId.Chronomancer, "Chronomancer", "Force", "◷", 10, 45, 175, 4,
                "Force capstone: strongly increases Bend Time duration and slowdown. Rank 10 permits configured complete suspension.", R(VoidstepSkillId.BendTheHour, 5)),

            D(VoidstepSkillId.FatefulLink, "Fateful Link", "Dominion", "∞", 20, 5, 75, 1,
                "Unlocks Domino and increases marking range, link capacity and propagated damage.", R(VoidstepSkillId.VoidAffinity, 5)),
            D(VoidstepSkillId.SharedAgony, "Shared Agony", "Dominion", "⛓", 20, 18, 100, 2,
                "Greatly increases Domino link count, marking range and damage propagation strength.", R(VoidstepSkillId.FatefulLink, 5)),
            D(VoidstepSkillId.UmbralSight, "Umbral Sight", "Dominion", "◉", 20, 10, 75, 3,
                "Unlocks Dark Vision and expands its detection radius while accelerating hostile refreshes.", R(VoidstepSkillId.VoidAffinity, 5)),
            D(VoidstepSkillId.SovereignGaze, "Sovereign Gaze", "Dominion", "☉", 10, 35, 150, 4,
                "Dominion capstone: massively improves Dark Vision range and refresh speed and also extends Domino reach and capacity.", R(VoidstepSkillId.UmbralSight, 5)),

            D(VoidstepSkillId.DeepReservoir, "Deep Reservoir", "Reservoir", "◇", 20, 2, 50, 1,
                "Raises maximum Void Energy while progression is enabled.", R(VoidstepSkillId.VoidAffinity, 3)),
            D(VoidstepSkillId.EfficientChanneling, "Efficient Channeling", "Reservoir", "△", 20, 10, 75, 2,
                "Provides a moderate global energy-cost reduction; it supports ability growth rather than replacing it.", R(VoidstepSkillId.DeepReservoir, 5)),
            D(VoidstepSkillId.RapidRecovery, "Rapid Recovery", "Reservoir", "↻", 20, 20, 100, 3,
                "Raises Void Energy regeneration and moderately reduces global cooldowns.", R(VoidstepSkillId.EfficientChanneling, 5)),
            D(VoidstepSkillId.UnboundPower, "Unbound Power", "Reservoir", "✹", 10, 45, 175, 4,
                "Raises energy capacity and regeneration and adds raw range, radius, force, damage and duration to every branch. Rank 5 permits cooldown-only mode; rank 10 permits unlimited energy.", R(VoidstepSkillId.RapidRecovery, 5)),

            D(VoidstepSkillId.Singularity, "Singularity", "Convergence", "⊙", 10, 60, 200, 1,
                "Convergence mastery that increases every ability's range, radius, force, damage, duration, target capacity and refresh power.",
                R(VoidstepSkillId.VoidDancer, 1), R(VoidstepSkillId.Chronomancer, 1),
                R(VoidstepSkillId.SharedAgony, 5), R(VoidstepSkillId.SovereignGaze, 1)),
            D(VoidstepSkillId.AvatarOfTheVoid, "Avatar of the Void", "Convergence", "✺", 10, 80, 225, 2,
                "Final transformation: further amplifies every mechanical effect. Rank 10 releases progression energy caps and configured unlimited Cleave targets.",
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
                    return level <= 0 ? "Rank 1 unlocks Voidstep Cleave." :
                        "Cleave radius +" + F(1.8f * level, 1) + "%, teleport range +" + F(2f * level, 1) +
                        "%, damage +" + F(2f * level, 1) + "%, sweep +" + F(1.5f * level, 1) +
                        "%, knockback +" + F(2.5f * level, 1) + "% and target cap growth.";
                case VoidstepSkillId.RiftStep:
                    return level <= 0 ? "Rank 1 unlocks Blink." : "Blink range contribution: +" + F(2.5f * level, 1) + "%.";
                case VoidstepSkillId.PhaseRecovery:
                    return "Blink and Voidstep range +" + F(3f * level, 1) + "%; Cleave radius +" + F(2.5f * level, 1) + "% and target cap growth.";
                case VoidstepSkillId.MomentumWeave:
                    return (level >= 1 ? "Configured momentum preservation permitted. " : "Rank 1 permits configured momentum preservation. ") +
                           (level >= 10 ? "Configured sealed-wall traversal permitted." : "Rank 10 permits configured sealed-wall traversal.");
                case VoidstepSkillId.VoidDancer:
                    return "Blink range +" + F(4f * level, 1) + "%, Voidstep range +" + F(3.5f * level, 1) +
                           "%, Cleave radius +" + F(3f * level, 1) + "%, damage +" + F(2.5f * level, 1) +
                           "%, knockback +" + F(3f * level, 1) + "% and +" + (2 * level) + " target capacity.";
                case VoidstepSkillId.GaleForce:
                    return level <= 0 ? "Rank 1 unlocks Windblast." : "Windblast angle +" + F(2f * level, 1) +
                        "%, range/force +" + F(2.5f * level, 1) + "% and damage +" + F(2f * level, 1) + "%.";
                case VoidstepSkillId.CrushingWave:
                    return "Windblast angle +" + F(2.5f * level, 1) + "%, range +" + F(3f * level, 1) +
                           "%, force +" + F(3.5f * level, 1) + "%, damage +" + F(4f * level, 1) +
                           "%; Cleave damage/knockback also increase.";
                case VoidstepSkillId.BendTheHour:
                    return level <= 0 ? "Rank 1 unlocks Bend Time." : "Bend Time duration +" + F(2f * level, 1) +
                        "% and slowdown power +" + F(2.5f * level, 1) + "%.";
                case VoidstepSkillId.Chronomancer:
                    return "Bend Time duration +" + F(4f * level, 1) + "% and slowdown power +" + F(3f * level, 1) + "%." +
                           (level >= 10 ? " Configured complete suspension permitted." : " Rank 10 permits configured complete suspension.");
                case VoidstepSkillId.FatefulLink:
                    return level <= 0 ? "Rank 1 unlocks Domino." : "Domino range +" + F(2f * level, 1) +
                        "%, propagation +" + F(2.5f * level, 1) + "% and link-cap growth.";
                case VoidstepSkillId.SharedAgony:
                    return "Domino range/propagation +" + F(3f * level, 1) + "% and approximately +" + (level / 2) + " link capacity.";
                case VoidstepSkillId.UmbralSight:
                    return level <= 0 ? "Rank 1 unlocks Dark Vision." : "Dark Vision range +" + F(2.5f * level, 1) +
                        "% and refresh speed +" + F(3f * level, 1) + "%.";
                case VoidstepSkillId.SovereignGaze:
                    return "Dark Vision range +" + F(3.5f * level, 1) + "% and refresh speed +" + F(6f * level, 1) +
                           "%; Domino range +" + F(2.5f * level, 1) + "% and link cap also increase.";
                case VoidstepSkillId.DeepReservoir:
                    return "Maximum energy contribution: +" + (4 * level) + ".";
                case VoidstepSkillId.EfficientChanneling:
                    return "Global energy-cost reduction: " + F(0.75f * level, 2) + "%.";
                case VoidstepSkillId.RapidRecovery:
                    return "Regeneration contribution: +" + F(0.3f * level, 1) + "/s. Global cooldown reduction: " + F(0.6f * level, 1) + "%.";
                case VoidstepSkillId.UnboundPower:
                    return "All range/radius +" + F(level, 1) + "%; raw force/damage/duration +at least " + F(1f * level, 1) +
                           "% (some effects scale faster). Maximum energy +" + (6 * level) + "; regeneration +" + F(0.5f * level, 1) + "/s." +
                           (level >= 5 ? " Cooldown-only mode permitted." : string.Empty) +
                           (level >= 10 ? " Unlimited energy permitted." : string.Empty);
                case VoidstepSkillId.Singularity:
                    return "All ability range/radius/force/damage/duration +roughly " + F(2f * level, 1) +
                           "%; Cleave targets, Domino links and Dark Vision refresh also grow.";
                case VoidstepSkillId.AvatarOfTheVoid:
                    return level >= 10
                        ? "All mechanical effects amplified; configured energy caps and unlimited Cleave targets released."
                        : "All mechanical effects receive a final +" + F(2f * level, 1) + " to " + F(4f * level, 1) + "% contribution.";
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
