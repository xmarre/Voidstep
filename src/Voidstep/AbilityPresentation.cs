using Voidstep.Core;

namespace Voidstep
{
    internal static class AbilityPresentation
    {
        internal static string Name(AbilityId ability)
        {
            switch (ability)
            {
                case AbilityId.VoidstepCleave: return "Voidstep Cleave";
                case AbilityId.Blink: return "Blink";
                case AbilityId.Windblast: return "Windblast";
                case AbilityId.BendTime: return "Bend Time";
                case AbilityId.Domino: return "Domino";
                case AbilityId.DarkVision: return "Dark Vision";
                default: return ability.ToString();
            }
        }

        internal static string Description(AbilityId ability)
        {
            switch (ability)
            {
                case AbilityId.VoidstepCleave: return "Teleport into the selected area and carve through every enemy in the sweep.";
                case AbilityId.Blink: return "Freeze the outside world while choosing a safe teleport destination.";
                case AbilityId.Windblast: return "Release a camera-aimed cone that throws enemies away.";
                case AbilityId.BendTime: return "Slow the outside world while preserving the controlled actor's speed.";
                case AbilityId.Domino: return "Link nearby enemies so a strike against one propagates to the others.";
                case AbilityId.DarkVision: return "Reveal nearby hostile agents through terrain and darkness.";
                default: return string.Empty;
            }
        }

        internal static uint MarkerColor(AbilityId ability)
        {
            switch (ability)
            {
                case AbilityId.VoidstepCleave: return 0x60E080FFu;
                case AbilityId.Blink: return 0x40E0A0FFu;
                case AbilityId.Windblast: return 0x70C8FFFFu;
                case AbilityId.BendTime: return 0x9070FFFFu;
                case AbilityId.Domino: return 0xC060FFFFu;
                case AbilityId.DarkVision: return 0xE0B050FFu;
                default: return 0xFFFFFFFFu;
            }
        }

        internal static string TorStringId(AbilityId ability) => "voidstep_" + ability.ToString().ToLowerInvariant();
    }
}
