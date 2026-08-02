using System;
using System.Collections.Generic;
using HarmonyLib;
using Voidstep.Core;

namespace Voidstep
{
    internal static class VoidstepMasteryGraphRuntime
    {
        private static readonly object Sync = new object();
        private static volatile bool _applied;

        internal static void EnsureApplied()
        {
            if (_applied) return;

            lock (Sync)
            {
                if (_applied) return;
                ValidateStableSkillIds();

                foreach (var skill in VoidstepSkillCatalog.All)
                {
                    var requirements = MasteryGraphPolicy.GetRequirements((int)skill.Id);
                    if (requirements.Count == 0)
                    {
                        skill.Prerequisites = Array.Empty<VoidstepSkillRequirement>();
                        continue;
                    }

                    var translated = new VoidstepSkillRequirement[requirements.Count];
                    for (var index = 0; index < requirements.Count; index++)
                    {
                        var requirement = requirements[index];
                        translated[index] = new VoidstepSkillRequirement
                        {
                            Id = (VoidstepSkillId)requirement.SkillId,
                            Level = requirement.Level
                        };
                    }
                    skill.Prerequisites = translated;
                }

                _applied = true;
            }
        }

        private static void ValidateStableSkillIds()
        {
            var stableIds = new Dictionary<VoidstepSkillId, int>
            {
                { VoidstepSkillId.VoidAffinity, MasteryGraphPolicy.VoidAffinity },
                { VoidstepSkillId.RiftStep, MasteryGraphPolicy.RiftStep },
                { VoidstepSkillId.PhaseRecovery, MasteryGraphPolicy.PhaseRecovery },
                { VoidstepSkillId.MomentumWeave, MasteryGraphPolicy.MomentumWeave },
                { VoidstepSkillId.VoidDancer, MasteryGraphPolicy.VoidDancer },
                { VoidstepSkillId.GaleForce, MasteryGraphPolicy.GaleForce },
                { VoidstepSkillId.CrushingWave, MasteryGraphPolicy.CrushingWave },
                { VoidstepSkillId.BendTheHour, MasteryGraphPolicy.BendTheHour },
                { VoidstepSkillId.Chronomancer, MasteryGraphPolicy.Chronomancer },
                { VoidstepSkillId.FatefulLink, MasteryGraphPolicy.FatefulLink },
                { VoidstepSkillId.SharedAgony, MasteryGraphPolicy.SharedAgony },
                { VoidstepSkillId.UmbralSight, MasteryGraphPolicy.UmbralSight },
                { VoidstepSkillId.SovereignGaze, MasteryGraphPolicy.SovereignGaze },
                { VoidstepSkillId.DeepReservoir, MasteryGraphPolicy.DeepReservoir },
                { VoidstepSkillId.EfficientChanneling, MasteryGraphPolicy.EfficientChanneling },
                { VoidstepSkillId.RapidRecovery, MasteryGraphPolicy.RapidRecovery },
                { VoidstepSkillId.UnboundPower, MasteryGraphPolicy.UnboundPower },
                { VoidstepSkillId.Singularity, MasteryGraphPolicy.Singularity },
                { VoidstepSkillId.AvatarOfTheVoid, MasteryGraphPolicy.AvatarOfTheVoid }
            };

            if (stableIds.Count != MasteryGraphPolicy.SkillCount)
                throw new InvalidOperationException("Voidstep mastery skill count changed without a save migration.");

            foreach (var entry in stableIds)
            {
                if ((int)entry.Key != entry.Value)
                    throw new InvalidOperationException(
                        "Voidstep mastery skill IDs changed without a save migration: " + entry.Key + ".");
            }
        }
    }

    [HarmonyPatch(typeof(VoidstepProgressionBehavior), "CanInvest")]
    internal static class IndependentMasteryCanInvestPatch
    {
        private static bool Prepare()
        {
            VoidstepMasteryGraphRuntime.EnsureApplied();
            return true;
        }

        private static void Prefix() => VoidstepMasteryGraphRuntime.EnsureApplied();
    }

    [HarmonyPatch(typeof(VoidstepSkillCatalog), "GetRequirementText")]
    internal static class IndependentMasteryRequirementTextPatch
    {
        private static void Prefix() => VoidstepMasteryGraphRuntime.EnsureApplied();
    }
}
