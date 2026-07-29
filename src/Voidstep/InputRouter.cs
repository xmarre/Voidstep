using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class InputRouter
    {
        private readonly Mission _mission;
        private readonly VoidstepLogger _logger;

        public InputRouter(Mission mission, VoidstepLogger logger)
        {
            _mission = mission;
            _logger = logger;
        }

        public AbilityId? PollAbility()
        {
            InputConflictSuppression.RefreshLatches();
            if (!VoidstepSubModule.InputSuppressionReady || !VoidstepSubModule.NativeHotkeysReady)
                return null;
            if (Input.IsOnScreenKeyboardActive || !_mission.IsLoadingFinished || _mission.MissionEnded || _mission.MissionIsEnding)
                return null;

            for (var i = 0; i < VoidstepInputBindings.Abilities.Length; i++)
            {
                var ability = VoidstepInputBindings.Abilities[i];
                if (!VoidstepInputBindings.TryGetPressedKey(ability, out var inputKey))
                    continue;

                InputConflictSuppression.Latch(inputKey);
                _logger.Debug($"Input accepted: {ability} ({VoidstepInputBindings.FormatBinding(ability)}; primary={inputKey}).");
                return ability;
            }
            return null;
        }

        public void Cleanup() => InputConflictSuppression.Reset();
    }
}
