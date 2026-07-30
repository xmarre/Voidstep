using HarmonyLib;
using Voidstep.Core;

namespace Voidstep
{
    internal static class VoidstepProgressionBoundarySynchronizer
    {
        internal static void SynchronizeAll()
        {
            var behavior = VoidstepProgressionService.Current;
            if (behavior == null)
                return;

            var profile = VoidstepProgressionService.Profile;
            if (profile.Enabled != behavior.Enabled)
            {
                VoidstepProgressionService.NotifyChanged();
                return;
            }

            if (!behavior.Enabled)
                return;

            foreach (var skill in VoidstepSkillCatalog.All)
            {
                if (profile.Level(skill.Id) == behavior.GetSkillLevel(skill.Id))
                    continue;

                VoidstepProgressionService.NotifyChanged();
                return;
            }
        }

        internal static void SynchronizeUnlock(AbilityId ability)
        {
            var behavior = VoidstepProgressionService.Current;
            if (behavior == null)
                return;

            var profile = VoidstepProgressionService.Profile;
            if (profile.Enabled != behavior.Enabled)
            {
                VoidstepProgressionService.NotifyChanged();
                return;
            }

            if (!behavior.Enabled)
                return;

            var required = VoidstepSkillCatalog.RequiredSkill(ability);
            if (profile.Level(required) != behavior.GetSkillLevel(required))
                VoidstepProgressionService.NotifyChanged();
        }
    }

    [HarmonyPatch(typeof(AbilityContext), MethodType.Constructor)]
    internal static class ProgressionMissionBoundarySynchronizationPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            VoidstepProgressionBoundarySynchronizer.SynchronizeAll();
        }
    }

    [HarmonyPatch(typeof(AbilityManager), nameof(AbilityManager.TryActivate))]
    internal static class ProgressionActivationBoundarySynchronizationPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(AbilityId ability)
        {
            VoidstepProgressionBoundarySynchronizer.SynchronizeUnlock(ability);
        }
    }

    internal static class VoidstepMasteryDescriptions
    {
        private static bool _applied;

        internal static void Apply()
        {
            if (_applied)
                return;

            _applied = true;
            Set(VoidstepSkillId.VoidAffinity,
                "Unlocks Voidstep Cleave. Increases teleport range, cleave radius, sweep, damage, knockback and target capacity.");
            Set(VoidstepSkillId.RiftStep,
                "Unlocks Blink. Increases Blink teleport range.");
            Set(VoidstepSkillId.PhaseRecovery,
                "Extends Blink and Voidstep Cleave teleport range and increases Cleave radius and target capacity.");
            Set(VoidstepSkillId.MomentumWeave,
                "Rank 1 preserves momentum after Blink. Rank 10 allows passage through sealed obstacles when enabled.");
            Set(VoidstepSkillId.VoidDancer,
                "Greatly extends both teleports and increases Cleave radius, damage, knockback and target capacity.");
            Set(VoidstepSkillId.GaleForce,
                "Unlocks Windblast. Increases its cone angle, range, force and damage.");
            Set(VoidstepSkillId.CrushingWave,
                "Greatly increases Windblast force and damage and strengthens Voidstep Cleave impacts.");
            Set(VoidstepSkillId.BendTheHour,
                "Unlocks Bend Time. Increases its duration and slowdown strength.");
            Set(VoidstepSkillId.Chronomancer,
                "Greatly increases Bend Time duration and slowdown strength. Rank 10 allows complete suspension when enabled.");
            Set(VoidstepSkillId.FatefulLink,
                "Unlocks Domino. Increases marking range, linked targets and propagated damage.");
            Set(VoidstepSkillId.SharedAgony,
                "Greatly increases Domino range, linked targets and propagated damage.");
            Set(VoidstepSkillId.UmbralSight,
                "Unlocks Dark Vision. Increases detection range and refresh speed.");
            Set(VoidstepSkillId.SovereignGaze,
                "Greatly increases Dark Vision range and refresh speed and extends Domino range and link capacity.");
            Set(VoidstepSkillId.DeepReservoir,
                "Increases maximum Void Energy.");
            Set(VoidstepSkillId.EfficientChanneling,
                "Reduces the Void Energy cost of every ability.");
            Set(VoidstepSkillId.RapidRecovery,
                "Increases Void Energy regeneration and reduces ability cooldowns.");
            Set(VoidstepSkillId.UnboundPower,
                "Strengthens every ability and increases Void Energy capacity and regeneration. Rank 5 unlocks cooldown-only mode; rank 10 unlocks unlimited energy.");
            Set(VoidstepSkillId.Singularity,
                "Increases every ability's range, radius, force, damage, duration, target capacity and refresh speed.");
            Set(VoidstepSkillId.AvatarOfTheVoid,
                "Further strengthens every ability. Rank 10 removes progression energy limits and unlocks unlimited Cleave targets.");
        }

        private static void Set(VoidstepSkillId id, string description)
        {
            VoidstepSkillCatalog.ById[id].Description = description;
        }
    }

    [HarmonyPatch(typeof(VoidstepMasteryVM), MethodType.Constructor)]
    internal static class ProgressionMasteryDescriptionPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            VoidstepMasteryDescriptions.Apply();
        }
    }
}
