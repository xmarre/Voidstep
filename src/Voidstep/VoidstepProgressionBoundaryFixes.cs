using Voidstep.Core;

namespace Voidstep
{
    internal static class VoidstepProgressionBoundarySynchronizer
    {
        private static bool TryGetEnabledState(
            out VoidstepProgressionBehavior behavior,
            out VoidstepProgressionProfile profile)
        {
            behavior = VoidstepProgressionService.Current;
            profile = null;
            if (behavior == null)
                return false;

            profile = VoidstepProgressionService.Profile;
            if (profile.Enabled != behavior.Enabled)
            {
                VoidstepProgressionService.NotifyChanged();
                return false;
            }

            return behavior.Enabled;
        }

        internal static void SynchronizeAll()
        {
            VoidstepProgressionBehavior behavior;
            VoidstepProgressionProfile profile;
            if (!TryGetEnabledState(out behavior, out profile))
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
            VoidstepProgressionBehavior behavior;
            VoidstepProgressionProfile profile;
            if (!TryGetEnabledState(out behavior, out profile))
                return;

            var required = VoidstepSkillCatalog.RequiredSkill(ability);
            if (profile.Level(required) != behavior.GetSkillLevel(required))
                VoidstepProgressionService.NotifyChanged();
        }
    }
}
