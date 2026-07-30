using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace Voidstep
{
    internal sealed class VoidstepProgressionBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, int> _masteryXp = new Dictionary<string, int>();
        private Dictionary<string, int> _skillLevels = new Dictionary<string, int>();
        private bool _progressionEnabled;
        private int _dataVersion = 1;

        internal VoidstepProgressionBehavior()
        {
            VoidstepProgressionService.Attach(this);
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => AttachAndApplySettings());
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, _ => AttachAndApplySettings());
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, _ => AttachAndApplySettings());
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_voidstepMasteryXp_v1", ref _masteryXp);
            dataStore.SyncData("_voidstepSkillLevels_v1", ref _skillLevels);
            dataStore.SyncData("_voidstepProgressionEnabled_v1", ref _progressionEnabled);
            dataStore.SyncData("_voidstepProgressionDataVersion", ref _dataVersion);

            if (_masteryXp == null) _masteryXp = new Dictionary<string, int>();
            if (_skillLevels == null) _skillLevels = new Dictionary<string, int>();
            if (_dataVersion < 1) _dataVersion = 1;

            VoidstepProgressionService.Attach(this);
            if (dataStore.IsLoading) VoidstepProgressionService.ApplyCurrentSetting();
        }

        internal bool Enabled => _progressionEnabled;
        internal string HeroKey => Hero.MainHero != null ? Hero.MainHero.StringId : "main_hero";

        internal int Xp
        {
            get
            {
                int value;
                return _masteryXp.TryGetValue(HeroKey, out value) ? Math.Max(0, value) : 0;
            }
        }

        internal int Rank => VoidstepSkillCatalog.GetRank(Xp);

        internal int MeleeSkill
        {
            get
            {
                var hero = Hero.MainHero;
                if (hero == null) return 0;
                return Math.Max(
                    hero.GetSkillValue(DefaultSkills.OneHanded),
                    Math.Max(hero.GetSkillValue(DefaultSkills.TwoHanded), hero.GetSkillValue(DefaultSkills.Polearm)));
            }
        }

        internal int InvestedPoints => VoidstepSkillCatalog.GetInvestedPoints(GetSkillLevel);
        internal int AvailablePoints => Math.Max(0, Rank - InvestedPoints);

        internal void SetEnabled(bool enabled)
        {
            if (_progressionEnabled == enabled) return;
            _progressionEnabled = enabled;
            NotifyChanged();
        }

        internal int GetSkillLevel(VoidstepSkillId id)
        {
            var definition = VoidstepSkillCatalog.ById[id];
            int value;
            if (!_skillLevels.TryGetValue(SkillKey(HeroKey, id), out value)) return 0;
            return Math.Max(0, Math.Min(definition.MaxLevel, value));
        }

        internal bool CanInvest(VoidstepSkillId id, out string reason)
        {
            var skill = VoidstepSkillCatalog.ById[id];
            var current = GetSkillLevel(id);
            if (!_progressionEnabled) { reason = "Enable progression first."; return false; }
            if (current >= skill.MaxLevel) { reason = "Maximum level reached."; return false; }
            if (Rank < skill.MasteryRank) { reason = "Requires Mastery Rank " + skill.MasteryRank + "."; return false; }
            if (MeleeSkill < skill.MeleeSkill) { reason = "Requires Melee Skill " + skill.MeleeSkill + "."; return false; }
            if (AvailablePoints < 1) { reason = "Requires 1 mastery point."; return false; }

            foreach (var prerequisite in skill.Prerequisites)
            {
                if (GetSkillLevel(prerequisite.Id) >= prerequisite.Level) continue;
                reason = "Requires " + VoidstepSkillCatalog.ById[prerequisite.Id].Name + " level " + prerequisite.Level + ".";
                return false;
            }

            reason = "Ready to invest.";
            return true;
        }

        internal bool Invest(VoidstepSkillId id, out string reason)
        {
            if (!CanInvest(id, out reason)) return false;

            var skill = VoidstepSkillCatalog.ById[id];
            var newLevel = GetSkillLevel(id) + 1;
            _skillLevels[SkillKey(HeroKey, id)] = newLevel;
            reason = skill.Name + " reached level " + newLevel + "/" + skill.MaxLevel + ".";
            InformationManager.DisplayMessage(new InformationMessage(reason, Colors.Green));
            NotifyChanged();
            return true;
        }

        internal void Respec()
        {
            var prefix = HeroKey + "|";
            var remove = new List<string>();
            foreach (var key in _skillLevels.Keys)
                if (key != null && key.StartsWith(prefix, StringComparison.Ordinal)) remove.Add(key);
            foreach (var key in remove) _skillLevels.Remove(key);
            NotifyChanged();
        }

        internal void AddXp(int amount)
        {
            if (!_progressionEnabled || amount <= 0) return;

            var multiplier = VoidstepProgressionService.XpMultiplier;
            var scaled = Math.Max(1, (int)Math.Round(amount * multiplier));
            var beforeRank = Rank;
            var maximumXp = VoidstepSkillCatalog.GetThreshold(VoidstepProgressionBalance.MaximumMasteryRank);
            _masteryXp[HeroKey] = Math.Min(maximumXp, Xp + scaled);
            var afterRank = Rank;

            if (afterRank > beforeRank)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "Voidstep Mastery reached rank " + afterRank + ". Mastery points available: " + AvailablePoints + ".",
                    Colors.Green));
            }
            NotifyChanged();
        }

        private void AttachAndApplySettings()
        {
            VoidstepProgressionService.Attach(this);
            VoidstepProgressionService.ApplyCurrentSetting();
        }

        private static string SkillKey(string heroKey, VoidstepSkillId id)
        {
            return (heroKey ?? "main_hero") + "|" + (int)id;
        }

        private void NotifyChanged()
        {
            VoidstepProgressionService.NotifyChanged();
        }
    }

    internal static class VoidstepProgressionService
    {
        private static readonly object Sync = new object();
        private static VoidstepProgressionBehavior _behavior;
        private static volatile VoidstepProgressionProfile _profile = VoidstepProgressionProfile.Disabled;

        internal static event Action Changed;

        internal static VoidstepProgressionBehavior Current
        {
            get
            {
                lock (Sync) return _behavior;
            }
        }

        internal static VoidstepProgressionProfile Profile => _profile;
        internal static bool Enabled => _profile.Enabled;
        internal static int Level(VoidstepSkillId id) => _profile.Level(id);
        internal static bool ConfiguredEnabled => VoidstepProgressionSettings.Instance != null && VoidstepProgressionSettings.Instance.EnableProgression;
        internal static float XpMultiplier => VoidstepProgressionSettings.Instance != null ? VoidstepProgressionSettings.Instance.MasteryXpMultiplier : 1f;

        internal static void Attach(VoidstepProgressionBehavior behavior)
        {
            lock (Sync)
            {
                _behavior = behavior;
                _profile = VoidstepProgressionProfile.Build(behavior);
            }
        }

        internal static void ApplyCurrentSetting()
        {
            ApplyConfiguredEnabled(ConfiguredEnabled);
        }

        internal static void ApplyConfiguredEnabled(bool enabled)
        {
            VoidstepProgressionBehavior behavior;
            lock (Sync) behavior = _behavior;
            behavior?.SetEnabled(enabled);
        }

        internal static void SetEnabledFromUi(bool enabled)
        {
            var settings = VoidstepProgressionSettings.Instance;
            if (settings != null) settings.EnableProgression = enabled;
            else ApplyConfiguredEnabled(enabled);
        }

        internal static void AddXp(int amount)
        {
            VoidstepProgressionBehavior behavior;
            lock (Sync) behavior = _behavior;
            behavior?.AddXp(amount);
        }

        internal static void NotifyChanged()
        {
            Action changed;
            lock (Sync)
            {
                _profile = VoidstepProgressionProfile.Build(_behavior);
                changed = Changed;
            }
            changed?.Invoke();
        }

        internal static void Detach()
        {
            lock (Sync)
            {
                _behavior = null;
                _profile = VoidstepProgressionProfile.Disabled;
                Changed = null;
            }
        }
    }
}
