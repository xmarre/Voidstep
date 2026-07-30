using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace Voidstep
{
    internal sealed class VoidstepProgressionSettings : AttributeGlobalSettings<VoidstepProgressionSettings>
    {
        private bool _enableProgression;
        private float _masteryXpMultiplier = 1f;

        public override string Id => "Voidstep_Progression_v1";
        public override string DisplayName => "Voidstep — Mastery Progression";
        public override string FolderName => "Voidstep";
        public override string FormatType => "json2";

        [SettingPropertyBool(
            "Enable Mastery Progression",
            Order = 0,
            RequireRestart = false,
            HintText = "Enables the level-99 Voidstep mastery tree. Disabled progression preserves the normal Voidstep MCM configuration without restrictions.")]
        [SettingPropertyGroup("Progression", GroupOrder = 0)]
        public bool EnableProgression
        {
            get => _enableProgression;
            set
            {
                if (_enableProgression == value) return;
                _enableProgression = value;
                OnPropertyChanged();
                VoidstepProgressionService.ApplyConfiguredEnabled(value);
            }
        }

        [SettingPropertyFloatingInteger(
            "Mastery XP Multiplier",
            0.25f,
            3f,
            "0.00",
            Order = 1,
            RequireRestart = false,
            HintText = "Scales Voidstep mastery XP. 1.00 is balanced for a long campaign.")]
        [SettingPropertyGroup("Progression", GroupOrder = 0)]
        public float MasteryXpMultiplier
        {
            get => _masteryXpMultiplier;
            set
            {
                var clamped = value < 0.25f ? 0.25f : (value > 3f ? 3f : value);
                if (_masteryXpMultiplier == clamped) return;
                _masteryXpMultiplier = clamped;
                OnPropertyChanged();
            }
        }
    }
}
