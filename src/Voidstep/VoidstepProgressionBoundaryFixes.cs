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
}
