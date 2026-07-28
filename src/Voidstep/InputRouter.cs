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
        private readonly CachedKey _voidstep = new CachedKey();
        private readonly CachedKey _blink = new CachedKey();
        private readonly CachedKey _windblast = new CachedKey();
        private readonly CachedKey _bendTime = new CachedKey();
        private readonly CachedKey _domino = new CachedKey();
        private readonly CachedKey _darkVision = new CachedKey();

        public InputRouter(Mission mission) => _mission = mission;

        public AbilityId? PollAbility()
        {
            var settings = VoidstepSettings.Current;
            var input = _mission.InputManager;
            if (input == null || Input.IsOnScreenKeyboardActive || !_mission.IsLoadingFinished || _mission.MissionEnded || _mission.MissionIsEnding || _mission.PauseAITick)
                return null;
            if (settings.RequireControlModifier && !IsControlDown(input))
                return null;

            if (_voidstep.Pressed(input, settings.VoidstepKey)) return AbilityId.VoidstepCleave;
            if (_blink.Pressed(input, settings.BlinkKey)) return AbilityId.Blink;
            if (_windblast.Pressed(input, settings.WindblastKey)) return AbilityId.Windblast;
            if (_bendTime.Pressed(input, settings.BendTimeKey)) return AbilityId.BendTime;
            if (_domino.Pressed(input, settings.DominoKey)) return AbilityId.Domino;
            if (_darkVision.Pressed(input, settings.DarkVisionKey)) return AbilityId.DarkVision;
            return null;
        }

        private static bool IsControlDown(IInputContext input) =>
            input.IsKeyDown(InputKey.LeftControl) || input.IsKeyDown(InputKey.RightControl);

        private sealed class CachedKey
        {
            private string _selectedValue;
            private InputKey _key;
            private bool _valid;

            public bool Pressed(IInputContext input, Dropdown<string> setting)
            {
                var selected = setting != null && setting.Count > 0 ? setting.SelectedValue : null;
                if (!string.Equals(selected, _selectedValue, StringComparison.Ordinal))
                {
                    _selectedValue = selected;
                    _valid = !string.IsNullOrEmpty(selected) && Enum.TryParse(selected, false, out _key);
                }
                return _valid && input.IsKeyPressed(_key);
            }
        }
    }
}
