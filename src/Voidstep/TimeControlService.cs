using System;
using TaleWorlds.Library;
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
        private float _lastApplicationTime;
        private Agent _player;
        private Agent _mount;
        private bool _playerDrivenSnapshotCaptured;
        private bool _mountDrivenSnapshotCaptured;
        private bool _playerPropertiesApplied;
        private bool _mountPropertiesApplied;
        private bool _actionSpeedsApplied;
        private bool _actionSpeedFailureLogged;
        private bool _cleanupPending;

        private float _originalMaxSpeedMultiplier;
        private float _originalCombatMaxSpeedMultiplier;
        private float _originalTopSpeedReachDuration;
        private float _originalSwingSpeedMultiplier;
        private float _originalReadySpeedMultiplier;
        private float _originalReloadSpeed;
        private float _originalRangedReadySpeedMultiplier;
        private float _originalRangedReloadSpeedMultiplier;
        private float _appliedMaxSpeedMultiplier;
        private float _appliedCombatMaxSpeedMultiplier;
        private float _appliedTopSpeedReachDuration;
        private float _appliedSwingSpeedMultiplier;
        private float _appliedReadySpeedMultiplier;
        private float _appliedReloadSpeed;
        private float _appliedRangedReadySpeedMultiplier;
        private float _appliedRangedReloadSpeedMultiplier;

        private float _originalMountSpeed;
        private float _originalMountManeuver;
        private float _originalMountDashAcceleration;
        private float _appliedMountSpeed;
        private float _appliedMountManeuver;
        private float _appliedMountDashAcceleration;

        public TimeControlService(Mission mission, VoidstepLogger logger)
        {
            _mission = mission;
            _logger = logger;
        }

        public bool Active => !_cleanupPending && _token != 0 && _ownership.Owns(_token);
        public float Remaining => _remaining;

        public bool Begin(Agent player, float requestedFactor, float duration, bool allowCompleteSuspension)
        {
            Release();
            if (_token != 0)
            {
                _logger.Info("Bend Time is waiting for a previous mission speed request to finish cleanup.");
                return false;
            }
            if (player == null || !player.IsActive() || duration <= 0f)
                return false;

            var minimum = allowCompleteSuspension ? 0f : 0.02f;
            _factor = Math.Max(minimum, Math.Min(1f, requestedFactor));
            _remaining = duration;
            _lastApplicationTime = MBCommon.GetApplicationTime();
            _player = player;
            _actionSpeedFailureLogged = false;
            CapturePlayerSnapshot();
            CaptureCurrentMountSnapshot();

            try
            {
                float existingFactor;
                if (_mission.GetRequestedTimeSpeed(RequestId, out existingFactor))
                {
                    _logger.Info("Bend Time found an existing mission speed request with its reserved ID; refusing to replace a request it does not own.");
                    CompleteLocalState();
                    return false;
                }

                // Bannerlord 1.3.15 removes by request ID but calls RemoveAt(-1)
                // when the ID is absent. Acquire ownership before adding so the
                // catch path can safely verify and remove any partially added request.
                _token = _ownership.Acquire(RequestId);
                _cleanupPending = false;
                _mission.AddTimeSpeedRequest(new Mission.TimeSpeedRequest(_factor, RequestId));
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
            if (_cleanupPending)
            {
                TryCompleteRelease();
                return;
            }
            if (!Active)
                return;
            if (_player == null || !_player.IsActive() || _player.Health <= 0f)
            {
                Release();
                return;
            }

            var now = MBCommon.GetApplicationTime();
            var realDt = _lastApplicationTime > 0f ? Math.Max(0f, now - _lastApplicationTime) : Math.Max(0f, dt);
            _lastApplicationTime = now;
            _remaining -= realDt;

            RefreshControlledMount();
            if (VoidstepSettings.Current.PreservePlayerSpeed && _factor > 0.001f && _factor < 0.999f)
            {
                var compensation = Math.Min(8f, 1f / _factor);
                ApplyPlayerCompensation(compensation);
            }
            else
            {
                RestoreCompensation();
            }

            if (_remaining <= 0f)
                Release();
        }

        public void Release()
        {
            _remaining = 0f;
            if (_token == 0)
            {
                _cleanupPending = false;
                CompleteLocalState();
                return;
            }

            _cleanupPending = true;
            TryCompleteRelease();
        }

        private bool TryCompleteRelease()
        {
            if (_token == 0)
            {
                _cleanupPending = false;
                CompleteLocalState();
                return true;
            }

            int requestId;
            if (!_ownership.TryGet(_token, out requestId))
            {
                _token = 0;
                _cleanupPending = false;
                CompleteLocalState();
                return true;
            }

            try
            {
                float requestedFactor;
                if (_mission.GetRequestedTimeSpeed(requestId, out requestedFactor))
                {
                    _mission.RemoveTimeSpeedRequest(requestId);
                    if (_mission.GetRequestedTimeSpeed(requestId, out requestedFactor))
                        return false;
                }

                var token = _token;
                int releasedRequestId;
                if (!_ownership.Release(token, out releasedRequestId))
                    return false;

                _token = 0;
                _cleanupPending = false;
                CompleteLocalState();
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug("Owned time request cleanup failed; ownership retained for retry: " + ex.Message);
                return false;
            }
        }

        private void CapturePlayerSnapshot()
        {
            _playerDrivenSnapshotCaptured = false;
            try
            {
                var driven = _player?.AgentDrivenProperties;
                if (driven == null) return;
                _originalMaxSpeedMultiplier = driven.MaxSpeedMultiplier;
                _originalCombatMaxSpeedMultiplier = driven.CombatMaxSpeedMultiplier;
                _originalTopSpeedReachDuration = driven.TopSpeedReachDuration;
                _originalSwingSpeedMultiplier = driven.SwingSpeedMultiplier;
                _originalReadySpeedMultiplier = driven.ThrustOrRangedReadySpeedMultiplier;
                _originalReloadSpeed = driven.ReloadSpeed;
                _originalRangedReadySpeedMultiplier = driven.BipedalRangedReadySpeedMultiplier;
                _originalRangedReloadSpeedMultiplier = driven.BipedalRangedReloadSpeedMultiplier;
                _playerDrivenSnapshotCaptured = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("Bend Time player-speed snapshot unavailable: " + ex.Message);
            }
        }

        private void CaptureCurrentMountSnapshot()
        {
            var mount = _player?.MountAgent;
            if (mount != null && !mount.IsActive()) mount = null;
            _mount = mount;
            TryCaptureMountSnapshot(mount, "mount-speed");
        }

        private void TryCaptureMountSnapshot(Agent mount, string label)
        {
            _mountDrivenSnapshotCaptured = false;
            if (mount == null) return;

            try
            {
                var driven = mount.AgentDrivenProperties;
                if (driven == null) return;
                _originalMountSpeed = driven.MountSpeed;
                _originalMountManeuver = driven.MountManeuver;
                _originalMountDashAcceleration = driven.MountDashAccelerationMultiplier;
                _mountDrivenSnapshotCaptured = true;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Bend Time {label} snapshot unavailable: {ex.Message}");
            }
        }

        private void RefreshControlledMount()
        {
            var current = _player?.MountAgent;
            if (current != null && !current.IsActive()) current = null;
            if (ReferenceEquals(current, _mount)) return;

            RestoreMountProperties();
            _mount = current;
            TryCaptureMountSnapshot(current, "replacement-mount");
        }

        private void ApplyPlayerCompensation(float compensation)
        {
            if (_playerDrivenSnapshotCaptured)
            {
                try
                {
                    var driven = _player.AgentDrivenProperties;
                    RefreshPlayerBaselinesAfterExternalUpdate(driven);
                    _appliedMaxSpeedMultiplier = _originalMaxSpeedMultiplier * compensation;
                    _appliedCombatMaxSpeedMultiplier = _originalCombatMaxSpeedMultiplier * compensation;
                    _appliedTopSpeedReachDuration = Math.Max(0.01f, _originalTopSpeedReachDuration / compensation);
                    _appliedSwingSpeedMultiplier = _originalSwingSpeedMultiplier * compensation;
                    _appliedReadySpeedMultiplier = _originalReadySpeedMultiplier * compensation;
                    _appliedReloadSpeed = _originalReloadSpeed * compensation;
                    _appliedRangedReadySpeedMultiplier = _originalRangedReadySpeedMultiplier * compensation;
                    _appliedRangedReloadSpeedMultiplier = _originalRangedReloadSpeedMultiplier * compensation;
                    _playerPropertiesApplied = true;
                    driven.MaxSpeedMultiplier = _appliedMaxSpeedMultiplier;
                    driven.CombatMaxSpeedMultiplier = _appliedCombatMaxSpeedMultiplier;
                    driven.TopSpeedReachDuration = _appliedTopSpeedReachDuration;
                    driven.SwingSpeedMultiplier = _appliedSwingSpeedMultiplier;
                    driven.ThrustOrRangedReadySpeedMultiplier = _appliedReadySpeedMultiplier;
                    driven.ReloadSpeed = _appliedReloadSpeed;
                    driven.BipedalRangedReadySpeedMultiplier = _appliedRangedReadySpeedMultiplier;
                    driven.BipedalRangedReloadSpeedMultiplier = _appliedRangedReloadSpeedMultiplier;
                }
                catch (Exception ex)
                {
                    _logger.Debug("Bend Time player-property compensation failed: " + ex.Message);
                }
            }

            if (_mountDrivenSnapshotCaptured && _mount != null && _mount.IsActive())
            {
                try
                {
                    var driven = _mount.AgentDrivenProperties;
                    RefreshMountBaselinesAfterExternalUpdate(driven);
                    _appliedMountSpeed = _originalMountSpeed * compensation;
                    _appliedMountManeuver = _originalMountManeuver * compensation;
                    _appliedMountDashAcceleration = _originalMountDashAcceleration * compensation;
                    _mountPropertiesApplied = true;
                    driven.MountSpeed = _appliedMountSpeed;
                    driven.MountManeuver = _appliedMountManeuver;
                    driven.MountDashAccelerationMultiplier = _appliedMountDashAcceleration;
                }
                catch (Exception ex)
                {
                    _logger.Debug("Bend Time mount-property compensation failed: " + ex.Message);
                }
            }

            SetActionSpeeds(compensation);
        }

        private void RefreshPlayerBaselinesAfterExternalUpdate(AgentDrivenProperties driven)
        {
            if (!_playerPropertiesApplied) return;
            if (!Approximately(driven.MaxSpeedMultiplier, _appliedMaxSpeedMultiplier))
                _originalMaxSpeedMultiplier = driven.MaxSpeedMultiplier;
            if (!Approximately(driven.CombatMaxSpeedMultiplier, _appliedCombatMaxSpeedMultiplier))
                _originalCombatMaxSpeedMultiplier = driven.CombatMaxSpeedMultiplier;
            if (!Approximately(driven.TopSpeedReachDuration, _appliedTopSpeedReachDuration))
                _originalTopSpeedReachDuration = driven.TopSpeedReachDuration;
            if (!Approximately(driven.SwingSpeedMultiplier, _appliedSwingSpeedMultiplier))
                _originalSwingSpeedMultiplier = driven.SwingSpeedMultiplier;
            if (!Approximately(driven.ThrustOrRangedReadySpeedMultiplier, _appliedReadySpeedMultiplier))
                _originalReadySpeedMultiplier = driven.ThrustOrRangedReadySpeedMultiplier;
            if (!Approximately(driven.ReloadSpeed, _appliedReloadSpeed))
                _originalReloadSpeed = driven.ReloadSpeed;
            if (!Approximately(driven.BipedalRangedReadySpeedMultiplier, _appliedRangedReadySpeedMultiplier))
                _originalRangedReadySpeedMultiplier = driven.BipedalRangedReadySpeedMultiplier;
            if (!Approximately(driven.BipedalRangedReloadSpeedMultiplier, _appliedRangedReloadSpeedMultiplier))
                _originalRangedReloadSpeedMultiplier = driven.BipedalRangedReloadSpeedMultiplier;
        }

        private void RefreshMountBaselinesAfterExternalUpdate(AgentDrivenProperties driven)
        {
            if (!_mountPropertiesApplied) return;
            if (!Approximately(driven.MountSpeed, _appliedMountSpeed))
                _originalMountSpeed = driven.MountSpeed;
            if (!Approximately(driven.MountManeuver, _appliedMountManeuver))
                _originalMountManeuver = driven.MountManeuver;
            if (!Approximately(driven.MountDashAccelerationMultiplier, _appliedMountDashAcceleration))
                _originalMountDashAcceleration = driven.MountDashAccelerationMultiplier;
        }

        private void RestoreCompensation()
        {
            RestorePlayerProperties();
            RestoreMountProperties();
            RestoreActionSpeeds();
        }

        private void RestorePlayerProperties()
        {
            if (!_playerPropertiesApplied) return;
            try
            {
                if (_playerDrivenSnapshotCaptured && _player != null && _player.IsActive())
                {
                    var driven = _player.AgentDrivenProperties;
                    if (Approximately(driven.MaxSpeedMultiplier, _appliedMaxSpeedMultiplier)) driven.MaxSpeedMultiplier = _originalMaxSpeedMultiplier;
                    if (Approximately(driven.CombatMaxSpeedMultiplier, _appliedCombatMaxSpeedMultiplier)) driven.CombatMaxSpeedMultiplier = _originalCombatMaxSpeedMultiplier;
                    if (Approximately(driven.TopSpeedReachDuration, _appliedTopSpeedReachDuration)) driven.TopSpeedReachDuration = _originalTopSpeedReachDuration;
                    if (Approximately(driven.SwingSpeedMultiplier, _appliedSwingSpeedMultiplier)) driven.SwingSpeedMultiplier = _originalSwingSpeedMultiplier;
                    if (Approximately(driven.ThrustOrRangedReadySpeedMultiplier, _appliedReadySpeedMultiplier)) driven.ThrustOrRangedReadySpeedMultiplier = _originalReadySpeedMultiplier;
                    if (Approximately(driven.ReloadSpeed, _appliedReloadSpeed)) driven.ReloadSpeed = _originalReloadSpeed;
                    if (Approximately(driven.BipedalRangedReadySpeedMultiplier, _appliedRangedReadySpeedMultiplier)) driven.BipedalRangedReadySpeedMultiplier = _originalRangedReadySpeedMultiplier;
                    if (Approximately(driven.BipedalRangedReloadSpeedMultiplier, _appliedRangedReloadSpeedMultiplier)) driven.BipedalRangedReloadSpeedMultiplier = _originalRangedReloadSpeedMultiplier;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("Player driven-property cleanup failed: " + ex.Message);
            }
            finally
            {
                _playerPropertiesApplied = false;
            }
        }

        private void RestoreMountProperties()
        {
            if (!_mountPropertiesApplied) return;
            try
            {
                if (_mountDrivenSnapshotCaptured && _mount != null && _mount.IsActive())
                {
                    var driven = _mount.AgentDrivenProperties;
                    if (Approximately(driven.MountSpeed, _appliedMountSpeed)) driven.MountSpeed = _originalMountSpeed;
                    if (Approximately(driven.MountManeuver, _appliedMountManeuver)) driven.MountManeuver = _originalMountManeuver;
                    if (Approximately(driven.MountDashAccelerationMultiplier, _appliedMountDashAcceleration)) driven.MountDashAccelerationMultiplier = _originalMountDashAcceleration;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("Mount driven-property cleanup failed: " + ex.Message);
            }
            finally
            {
                _mountPropertiesApplied = false;
            }
        }

        private void SetActionSpeeds(float speed)
        {
            if (_player == null || !_player.IsActive()) return;
            for (var channel = 0; channel < 4; channel++)
            {
                try
                {
                    _player.SetCurrentActionSpeed(channel, speed);
                    _actionSpeedsApplied = true;
                }
                catch (Exception ex)
                {
                    if (_actionSpeedFailureLogged) continue;
                    _actionSpeedFailureLogged = true;
                    _logger.Debug($"Player action-speed channel {channel} unavailable: {ex.Message}");
                }
            }
        }

        private void RestoreActionSpeeds()
        {
            if (!_actionSpeedsApplied) return;
            if (_player != null && _player.IsActive())
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    try { _player.SetCurrentActionSpeed(channel, 1f); }
                    catch (Exception ex)
                    {
                        if (_actionSpeedFailureLogged) continue;
                        _actionSpeedFailureLogged = true;
                        _logger.Debug($"Player action-speed cleanup channel {channel} unavailable: {ex.Message}");
                    }
                }
            }
            _actionSpeedsApplied = false;
        }

        private void CompleteLocalState()
        {
            RestoreCompensation();
            _playerDrivenSnapshotCaptured = false;
            _mountDrivenSnapshotCaptured = false;
            _actionSpeedFailureLogged = false;
            _player = null;
            _mount = null;
            _factor = 1f;
            _remaining = 0f;
            _lastApplicationTime = 0f;
        }

        private static bool Approximately(float left, float right) => Math.Abs(left - right) <= 0.001f * Math.Max(1f, Math.Max(Math.Abs(left), Math.Abs(right)));

        public void Cleanup()
        {
            Release();
            if (_token == 0)
                _ownership.Clear();
        }
    }
}
