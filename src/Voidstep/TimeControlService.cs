using System;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class TimeControlService
    {
        private const int RequestId = 0x56535450; // "VSTP"
        private readonly Mission _mission;
        private readonly VoidstepLogger _logger;
        private readonly OwnershipLedger<int> _ownership = new OwnershipLedger<int>();
        private long _token;
        private float _remaining;
        private float _factor = 1f;
        private Agent _player;
        private bool _playerCompensationApplied;

        public TimeControlService(Mission mission, VoidstepLogger logger)
        {
            _mission = mission;
            _logger = logger;
        }

        public bool Active => _token != 0 && _ownership.Owns(_token);
        public float Remaining => _remaining;

        public bool Begin(Agent player, float requestedFactor, float duration, bool allowCompleteSuspension)
        {
            Release();
            if (player == null || !player.IsActive() || duration <= 0f)
                return false;

            var minimum = allowCompleteSuspension ? 0f : 0.02f;
            _factor = Math.Max(minimum, Math.Min(1f, requestedFactor));
            _remaining = duration;
            _player = player;

            try
            {
                _mission.RemoveTimeSpeedRequest(RequestId);
                _mission.AddTimeSpeedRequest(new Mission.TimeSpeedRequest(_factor, RequestId));
                _token = _ownership.Acquire(RequestId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Bend Time could not acquire its mission speed request.", ex);
                Release();
                return false;
            }
        }

        public void Tick(float dt)
        {
            if (!Active)
                return;
            if (_player == null || !_player.IsActive() || _player.Health <= 0f)
            {
                Release();
                return;
            }

            _remaining -= Math.Max(0f, dt);
            if (VoidstepSettings.Current.PreservePlayerSpeed && _factor > 0.001f && _factor < 0.999f)
            {
                var compensation = Math.Min(8f, 1f / _factor);
                _player.SetCurrentActionSpeed(0, compensation);
                _player.SetCurrentActionSpeed(1, compensation);
                _playerCompensationApplied = true;
            }

            if (_remaining <= 0f)
                Release();
        }

        public void Release()
        {
            var token = _token;
            _token = 0;
            _remaining = 0f;
            if (token != 0 && _ownership.Release(token, out var requestId))
            {
                try { _mission.RemoveTimeSpeedRequest(requestId); }
                catch (Exception ex) { _logger.Debug("Owned time request cleanup failed: " + ex.Message); }
            }

            if (_playerCompensationApplied && _player != null && _player.IsActive())
            {
                try
                {
                    _player.SetCurrentActionSpeed(0, 1f);
                    _player.SetCurrentActionSpeed(1, 1f);
                }
                catch (Exception ex) { _logger.Debug("Player action-speed cleanup failed: " + ex.Message); }
            }
            _playerCompensationApplied = false;
            _player = null;
            _factor = 1f;
        }

        public void Cleanup()
        {
            Release();
            _ownership.Clear();
        }
    }
}
