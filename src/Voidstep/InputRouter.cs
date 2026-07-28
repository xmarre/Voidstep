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

        public InputRouter(Mission mission) => _mission = mission;

        public AbilityId? PollAbility()
        {
            var settings = VoidstepSettings.Current;
            var input = _mission.InputManager;
            if (input == null || Input.IsOnScreenKeyboardActive || !_mission.IsLoadingFinished || _mission.MissionEnded || _mission.MissionIsEnding || _mission.PauseAITick)
                return null;
            if (settings.RequireControlModifier && !IsControlDown(input))
                return null;

            if (Pressed(input, settings.VoidstepKey)) return AbilityId.VoidstepCleave;
            if (Pressed(input, settings.BlinkKey)) return AbilityId.Blink;
            if (Pressed(input, settings.WindblastKey)) return AbilityId.Windblast;
            if (Pressed(input, settings.BendTimeKey)) return AbilityId.BendTime;
            if (Pressed(input, settings.DominoKey)) return AbilityId.Domino;
            if (Pressed(input, settings.DarkVisionKey)) return AbilityId.DarkVision;
            return null;
        }

        private static bool Pressed(IInputContext input, Dropdown<string> setting)
        {
            if (setting == null || setting.Count == 0)
                return false;
            if (!Enum.TryParse(setting.SelectedValue, false, out InputKey key))
                return false;
            return input.IsKeyPressed(key);
        }

        private static bool IsControlDown(IInputContext input) =>
            input.IsKeyDown(InputKey.LeftControl) || input.IsKeyDown(InputKey.RightControl);
    }
}
