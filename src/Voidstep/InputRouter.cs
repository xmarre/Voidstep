using System;
using MCM.Common;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class InputRouter
    {
        private readonly Mission _mission;
        private readonly VoidstepLogger _logger;
        private readonly CachedKey _voidstep = new CachedKey();
        private readonly CachedKey _blink = new CachedKey();
        private readonly CachedKey _windblast = new CachedKey();
        private readonly CachedKey _bendTime = new CachedKey();
        private readonly CachedKey _domino = new CachedKey();
        private readonly CachedKey _darkVision = new CachedKey();

        public InputRouter(Mission mission, VoidstepLogger logger)
        {
            _mission = mission;
            _logger = logger;
        }

        public AbilityId? PollAbility()
        {
            if (!VoidstepSubModule.InputSuppressionReady)
                return null;

            var settings = VoidstepSettings.Current;
            if (Input.IsOnScreenKeyboardActive || !_mission.IsLoadingFinished || _mission.MissionEnded || _mission.MissionIsEnding)
                return null;
            if (settings.RequireControlModifier && !IsControlDown())
                return null;

            if (_voidstep.Pressed(settings.VoidstepKey)) return Pressed(AbilityId.VoidstepCleave, settings.VoidstepKey);
            if (_blink.Pressed(settings.BlinkKey)) return Pressed(AbilityId.Blink, settings.BlinkKey);
            if (_windblast.Pressed(settings.WindblastKey)) return Pressed(AbilityId.Windblast, settings.WindblastKey);
            if (_bendTime.Pressed(settings.BendTimeKey)) return Pressed(AbilityId.BendTime, settings.BendTimeKey);
            if (_domino.Pressed(settings.DominoKey)) return Pressed(AbilityId.Domino, settings.DominoKey);
            if (_darkVision.Pressed(settings.DarkVisionKey)) return Pressed(AbilityId.DarkVision, settings.DarkVisionKey);
            return null;
        }

        private AbilityId Pressed(AbilityId ability, Dropdown<string> setting)
        {
            _logger.Debug($"Input accepted: {ability} ({SelectedValue(setting)}), Ctrl required={VoidstepSettings.Current.RequireControlModifier}.");
            return ability;
        }

        private static bool IsControlDown() =>
            Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);

        private static string SelectedValue(Dropdown<string> setting) =>
            setting != null && setting.Count > 0 ? setting.SelectedValue : "<unset>";

        private sealed class CachedKey
        {
            private string _selectedValue;
            private InputKey _key;
            private bool _valid;

            public bool Pressed(Dropdown<string> setting)
            {
                var selected = SelectedValue(setting);
                if (!string.Equals(selected, _selectedValue, StringComparison.Ordinal))
                {
                    _selectedValue = selected;
                    _valid = !string.IsNullOrEmpty(selected) && selected != "<unset>" && Enum.TryParse(selected, false, out _key);
                }
                return _valid && Input.IsKeyPressed(_key);
            }
        }
    }
}
