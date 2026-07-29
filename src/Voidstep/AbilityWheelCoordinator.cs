using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class AbilityWheelCoordinator
    {
        private readonly InputRouter _input;
        private readonly AbilitySelectionController _selection;
        private readonly StandaloneAbilityWheel _standalone;
        private readonly TorAbilityWheelAdapter _tor;
        private readonly VoidstepLogger _logger;
        private bool _suppressRightMouseUntilRelease;
        private bool _suppressEscapeUntilRelease;

        internal AbilityWheelCoordinator(Mission mission, AbilityManager manager, InputRouter input, VoidstepLogger logger)
        {
            _input = input;
            _logger = logger;
            _selection = new AbilitySelectionController(mission, manager, logger);
            _standalone = new StandaloneAbilityWheel(logger, ability => _selection.Select(ability, "standalone Q cast wheel"));
            _tor = new TorAbilityWheelAdapter(mission, _selection, logger);
            VoidstepWheelRuntime.Attach(this);
        }

        internal bool UsesTorWheel => _tor.IsAvailable;

        internal void Tick(float dt)
        {
            RefreshSuppressionLatches();
            _selection.Tick(dt);
            _tor.Tick();

            if (!_tor.IsAvailable)
            {
                bool qPressed;
                using (InputConflictSuppression.EnterBypass())
                    qPressed = Input.IsKeyPressed(InputKey.Q);
                if (qPressed && _selection.HasSelection)
                    _selection.Cancel(true);
                _standalone.Tick();
            }

            var directAbility = _input.PollAbility();
            if (directAbility.HasValue)
            {
                if (_tor.OwnsTargeting)
                    _tor.CloseTargetingMode();
                _selection.Select(directAbility.Value, "configured direct selector");
            }

            if (!_selection.HasSelection)
                return;

            bool confirm;
            bool cancel;
            using (InputConflictSuppression.EnterBypass())
            {
                confirm = Input.IsKeyPressed(InputKey.RightMouseButton);
                cancel = Input.IsKeyPressed(InputKey.Escape);
            }

            if (cancel)
            {
                _suppressEscapeUntilRelease = true;
                _selection.Cancel(true);
                if (_tor.OwnsTargeting)
                    _tor.CloseTargetingMode();
                return;
            }

            if (!confirm)
                return;

            _suppressRightMouseUntilRelease = true;
            var torOwned = _tor.OwnsTargeting;
            if (_selection.Confirm() && torOwned)
                _tor.CloseTargetingMode();
        }

        internal bool ShouldSuppress(InputKey key)
        {
            if (key == InputKey.Q)
                return !_tor.IsAvailable;
            if (key == InputKey.RightMouseButton)
                return _selection.HasSelection || _suppressRightMouseUntilRelease;
            if (key == InputKey.Escape)
                return _standalone.IsOpen || _selection.HasSelection || _suppressEscapeUntilRelease;
            return false;
        }

        internal bool IsTorProxy(object instance) => _tor.TryGetProxyAbility(instance, out _);

        internal void Cleanup()
        {
            VoidstepWheelRuntime.Detach(this);
            _standalone.Cleanup();
            _tor.Cleanup();
            _selection.Cleanup();
            _suppressRightMouseUntilRelease = false;
            _suppressEscapeUntilRelease = false;
            _logger.Debug("Ability wheel and selection ownership cleaned up.");
        }

        private void RefreshSuppressionLatches()
        {
            using (InputConflictSuppression.EnterBypass())
            {
                if (_suppressRightMouseUntilRelease &&
                    !Input.IsKeyPressed(InputKey.RightMouseButton) &&
                    !Input.IsKeyDown(InputKey.RightMouseButton) &&
                    !Input.IsKeyDownImmediate(InputKey.RightMouseButton) &&
                    !Input.IsKeyReleased(InputKey.RightMouseButton))
                {
                    _suppressRightMouseUntilRelease = false;
                }

                if (_suppressEscapeUntilRelease &&
                    !Input.IsKeyPressed(InputKey.Escape) &&
                    !Input.IsKeyDown(InputKey.Escape) &&
                    !Input.IsKeyDownImmediate(InputKey.Escape) &&
                    !Input.IsKeyReleased(InputKey.Escape))
                {
                    _suppressEscapeUntilRelease = false;
                }
            }
        }
    }
}
