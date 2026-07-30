using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Library;

namespace Voidstep
{
    internal sealed class VoidstepSkillNodeVM : ViewModel
    {
        private readonly VoidstepMasteryVM _owner;
        internal readonly VoidstepSkillDefinition Definition;
        private bool _isSelected;
        private string _buttonText;

        internal VoidstepSkillNodeVM(VoidstepMasteryVM owner, VoidstepSkillDefinition definition)
        {
            _owner = owner;
            Definition = definition;
            Refresh();
        }

        [DataSourceProperty]
        public string ButtonText
        {
            get => _buttonText;
            set
            {
                if (value == _buttonText) return;
                _buttonText = value;
                OnPropertyChangedWithValue(value, nameof(ButtonText));
            }
        }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value == _isSelected) return;
                _isSelected = value;
                OnPropertyChangedWithValue(value, nameof(IsSelected));
            }
        }

        public void ExecuteSelect()
        {
            _owner.Select(Definition.Id);
        }

        internal void Refresh()
        {
            var progression = VoidstepProgressionService.Current;
            var level = progression != null ? progression.GetSkillLevel(Definition.Id) : 0;
            string reason;
            var ready = progression != null && progression.CanInvest(Definition.Id, out reason);
            var state = level >= Definition.MaxLevel ? "MAX" : (ready ? "READY" : (level > 0 ? "ACTIVE" : "LOCKED"));
            ButtonText = Definition.Glyph + "  " + Definition.Name + "\n" + level + " / " + Definition.MaxLevel + "  •  " + state;
        }
    }

    internal sealed class VoidstepMasteryVM : ViewModel
    {
        private readonly Action _close;
        private readonly List<VoidstepSkillNodeVM> _allNodes = new List<VoidstepSkillNodeVM>();
        private VoidstepSkillId _selectedId = VoidstepSkillId.VoidAffinity;

        private string _masteryText;
        private string _xpText;
        private string _pointsText;
        private string _meleeText;
        private string _selectedName;
        private string _selectedLevel;
        private string _selectedRequirements;
        private string _selectedDescription;
        private string _selectedCurrentEffect;
        private string _selectedNextEffect;
        private string _selectedStatus;
        private string _progressionText;
        private string _progressionActionText;
        private string _messageText;
        private int _xpProgress;

        internal VoidstepMasteryVM(Action close)
        {
            _close = close;
            MobilityNodes = Make("Mobility");
            ForceNodes = Make("Force");
            CoreNodes = Make("Core");
            DominionNodes = Make("Dominion");
            ReservoirNodes = Make("Reservoir");
            ConvergenceNodes = Make("Convergence");

            VoidstepProgressionService.Changed += RefreshAll;
            RefreshAll();
            Select(VoidstepSkillId.VoidAffinity);
        }

        [DataSourceProperty] public string Title => "Voidstep Mastery";
        [DataSourceProperty] public MBBindingList<VoidstepSkillNodeVM> MobilityNodes { get; }
        [DataSourceProperty] public MBBindingList<VoidstepSkillNodeVM> ForceNodes { get; }
        [DataSourceProperty] public MBBindingList<VoidstepSkillNodeVM> CoreNodes { get; }
        [DataSourceProperty] public MBBindingList<VoidstepSkillNodeVM> DominionNodes { get; }
        [DataSourceProperty] public MBBindingList<VoidstepSkillNodeVM> ReservoirNodes { get; }
        [DataSourceProperty] public MBBindingList<VoidstepSkillNodeVM> ConvergenceNodes { get; }

        [DataSourceProperty] public string MasteryText { get => _masteryText; set => Set(ref _masteryText, value, nameof(MasteryText)); }
        [DataSourceProperty] public string XpText { get => _xpText; set => Set(ref _xpText, value, nameof(XpText)); }
        [DataSourceProperty] public string PointsText { get => _pointsText; set => Set(ref _pointsText, value, nameof(PointsText)); }
        [DataSourceProperty] public string MeleeText { get => _meleeText; set => Set(ref _meleeText, value, nameof(MeleeText)); }
        [DataSourceProperty] public int XpProgress { get => _xpProgress; set { if (value != _xpProgress) { _xpProgress = value; OnPropertyChangedWithValue(value, nameof(XpProgress)); } } }
        [DataSourceProperty] public string SelectedName { get => _selectedName; set => Set(ref _selectedName, value, nameof(SelectedName)); }
        [DataSourceProperty] public string SelectedLevel { get => _selectedLevel; set => Set(ref _selectedLevel, value, nameof(SelectedLevel)); }
        [DataSourceProperty] public string SelectedRequirements { get => _selectedRequirements; set => Set(ref _selectedRequirements, value, nameof(SelectedRequirements)); }
        [DataSourceProperty] public string SelectedDescription { get => _selectedDescription; set => Set(ref _selectedDescription, value, nameof(SelectedDescription)); }
        [DataSourceProperty] public string SelectedCurrentEffect { get => _selectedCurrentEffect; set => Set(ref _selectedCurrentEffect, value, nameof(SelectedCurrentEffect)); }
        [DataSourceProperty] public string SelectedNextEffect { get => _selectedNextEffect; set => Set(ref _selectedNextEffect, value, nameof(SelectedNextEffect)); }
        [DataSourceProperty] public string SelectedStatus { get => _selectedStatus; set => Set(ref _selectedStatus, value, nameof(SelectedStatus)); }
        [DataSourceProperty] public string ProgressionText { get => _progressionText; set => Set(ref _progressionText, value, nameof(ProgressionText)); }
        [DataSourceProperty] public string ProgressionActionText { get => _progressionActionText; set => Set(ref _progressionActionText, value, nameof(ProgressionActionText)); }
        [DataSourceProperty] public string MessageText { get => _messageText; set => Set(ref _messageText, value, nameof(MessageText)); }

        public void ExecuteConfirm()
        {
            var progression = VoidstepProgressionService.Current;
            if (progression == null)
            {
                MessageText = "Campaign progression is unavailable.";
                return;
            }

            string reason;
            progression.Invest(_selectedId, out reason);
            MessageText = reason;
            RefreshAll();
        }

        public void ExecuteRespec()
        {
            var progression = VoidstepProgressionService.Current;
            if (progression == null) return;
            progression.Respec();
            MessageText = "All invested Voidstep mastery points were refunded.";
            RefreshAll();
        }

        public void ExecuteToggleProgression()
        {
            var progression = VoidstepProgressionService.Current;
            if (progression == null) return;
            var enable = !progression.Enabled;
            VoidstepProgressionService.SetEnabledFromUi(enable);
            MessageText = enable
                ? "Progression enabled. Ability unlocks and the invested mastery profile now apply in missions."
                : "Progression disabled. The normal Voidstep MCM configuration is unrestricted.";
            RefreshAll();
        }

        public void ExecuteClose()
        {
            _close?.Invoke();
        }

        internal void Select(VoidstepSkillId id)
        {
            _selectedId = id;
            foreach (var node in _allNodes) node.IsSelected = node.Definition.Id == id;
            RefreshSelected();
        }

        internal void RefreshAll()
        {
            var progression = VoidstepProgressionService.Current;
            if (progression == null)
            {
                MasteryText = "Mastery Rank 1 / 99";
                XpText = "XP 0 / " + VoidstepSkillCatalog.GetNextThreshold(1);
                PointsText = "Points Available: 0";
                MeleeText = "Melee Skill: 0";
                XpProgress = 0;
                ProgressionText = "Progression: Unavailable";
                ProgressionActionText = "Enable Progression";
            }
            else
            {
                var rank = progression.Rank;
                var currentThreshold = VoidstepSkillCatalog.GetThreshold(rank);
                var nextThreshold = VoidstepSkillCatalog.GetNextThreshold(rank);
                var width = Math.Max(1, nextThreshold - currentThreshold);

                MasteryText = "Mastery Rank " + rank + " / 99";
                XpText = rank >= VoidstepProgressionBalance.MaximumMasteryRank
                    ? "XP " + progression.Xp + " • MAXIMUM RANK"
                    : "XP " + progression.Xp + " / " + nextThreshold;
                PointsText = "Points Available: " + progression.AvailablePoints + "  •  Invested: " + progression.InvestedPoints;
                MeleeText = "Melee Skill: " + progression.MeleeSkill;
                XpProgress = rank >= VoidstepProgressionBalance.MaximumMasteryRank
                    ? 100
                    : Math.Max(0, Math.Min(100, (progression.Xp - currentThreshold) * 100 / width));
                ProgressionText = progression.Enabled ? "Progression: ENABLED" : "Progression: DISABLED";
                ProgressionActionText = progression.Enabled ? "Disable Progression" : "Enable Progression";
            }

            foreach (var node in _allNodes) node.Refresh();
            RefreshSelected();
        }

        public override void OnFinalize()
        {
            VoidstepProgressionService.Changed -= RefreshAll;
            base.OnFinalize();
        }

        private MBBindingList<VoidstepSkillNodeVM> Make(string branch)
        {
            var list = new MBBindingList<VoidstepSkillNodeVM>();
            foreach (var definition in VoidstepSkillCatalog.All.Where(skill => skill.Branch == branch).OrderBy(skill => skill.TreeOrder))
            {
                var node = new VoidstepSkillNodeVM(this, definition);
                list.Add(node);
                _allNodes.Add(node);
            }
            return list;
        }

        private void RefreshSelected()
        {
            var skill = VoidstepSkillCatalog.ById[_selectedId];
            var progression = VoidstepProgressionService.Current;
            var level = progression != null ? progression.GetSkillLevel(skill.Id) : 0;

            SelectedName = skill.Glyph + "  " + skill.Name;
            SelectedLevel = "Level " + level + " / " + skill.MaxLevel + "  •  1 point per level";
            SelectedRequirements = VoidstepSkillCatalog.GetRequirementText(skill);
            SelectedDescription = skill.Description;
            SelectedCurrentEffect = "CURRENT\n" + VoidstepSkillCatalog.GetEffectText(skill.Id, level);
            SelectedNextEffect = "NEXT\n" + VoidstepSkillCatalog.GetNextLevelText(skill, level);

            if (progression == null)
            {
                SelectedStatus = "Campaign progression is unavailable.";
            }
            else if (!progression.Enabled)
            {
                SelectedStatus = "Progression disabled — the normal MCM configuration is unrestricted.";
            }
            else if (level >= skill.MaxLevel)
            {
                SelectedStatus = "Maximum level reached • " + skill.Branch;
            }
            else
            {
                string reason;
                progression.CanInvest(skill.Id, out reason);
                SelectedStatus = reason + " • " + skill.Branch;
            }
        }

        private void Set(ref string field, string value, string name)
        {
            if (field == value) return;
            field = value;
            OnPropertyChangedWithValue(value, name);
        }
    }
}
