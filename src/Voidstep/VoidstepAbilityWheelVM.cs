using TaleWorlds.Library;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class VoidstepAbilityWheelVM : ViewModel
    {
        private int _selectedIndex = -1;
        private string _cleaveText;
        private string _blinkText;
        private string _windblastText;
        private string _bendTimeText;
        private string _dominoText;
        private string _darkVisionText;
        private string _selectedName;
        private string _selectedDescription;
        private string _castHint;

        internal VoidstepAbilityWheelVM()
        {
            CastHint = "Release Q to select • Right Mouse Button to cast • Escape to cancel";
            SetSelected(0);
        }

        internal int SelectedIndex => _selectedIndex;

        internal bool SetSelected(int index)
        {
            if (index < 0 || index >= VoidstepInputBindings.Abilities.Length)
                return false;
            _selectedIndex = index;
            var selected = VoidstepInputBindings.Abilities[index];
            CleaveText = Decorate(AbilityId.VoidstepCleave, selected);
            BlinkText = Decorate(AbilityId.Blink, selected);
            WindblastText = Decorate(AbilityId.Windblast, selected);
            BendTimeText = Decorate(AbilityId.BendTime, selected);
            DominoText = Decorate(AbilityId.Domino, selected);
            DarkVisionText = Decorate(AbilityId.DarkVision, selected);
            SelectedName = AbilityPresentation.Name(selected);
            SelectedDescription = AbilityPresentation.Description(selected);
            return true;
        }

        private static string Decorate(AbilityId ability, AbilityId selected) =>
            ability == selected ? "◆ " + AbilityPresentation.Name(ability) + " ◆" : AbilityPresentation.Name(ability);

        [DataSourceProperty]
        public string CleaveText
        {
            get => _cleaveText;
            set { if (value == _cleaveText) return; _cleaveText = value; OnPropertyChangedWithValue(value, nameof(CleaveText)); }
        }

        [DataSourceProperty]
        public string BlinkText
        {
            get => _blinkText;
            set { if (value == _blinkText) return; _blinkText = value; OnPropertyChangedWithValue(value, nameof(BlinkText)); }
        }

        [DataSourceProperty]
        public string WindblastText
        {
            get => _windblastText;
            set { if (value == _windblastText) return; _windblastText = value; OnPropertyChangedWithValue(value, nameof(WindblastText)); }
        }

        [DataSourceProperty]
        public string BendTimeText
        {
            get => _bendTimeText;
            set { if (value == _bendTimeText) return; _bendTimeText = value; OnPropertyChangedWithValue(value, nameof(BendTimeText)); }
        }

        [DataSourceProperty]
        public string DominoText
        {
            get => _dominoText;
            set { if (value == _dominoText) return; _dominoText = value; OnPropertyChangedWithValue(value, nameof(DominoText)); }
        }

        [DataSourceProperty]
        public string DarkVisionText
        {
            get => _darkVisionText;
            set { if (value == _darkVisionText) return; _darkVisionText = value; OnPropertyChangedWithValue(value, nameof(DarkVisionText)); }
        }

        [DataSourceProperty]
        public string SelectedName
        {
            get => _selectedName;
            set { if (value == _selectedName) return; _selectedName = value; OnPropertyChangedWithValue(value, nameof(SelectedName)); }
        }

        [DataSourceProperty]
        public string SelectedDescription
        {
            get => _selectedDescription;
            set { if (value == _selectedDescription) return; _selectedDescription = value; OnPropertyChangedWithValue(value, nameof(SelectedDescription)); }
        }

        [DataSourceProperty]
        public string CastHint
        {
            get => _castHint;
            set { if (value == _castHint) return; _castHint = value; OnPropertyChangedWithValue(value, nameof(CastHint)); }
        }
    }
}
