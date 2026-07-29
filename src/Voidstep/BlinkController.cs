using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    internal sealed class BlinkController
    {
        private const int AimTimeRequestId = 0x5653424C;
        private readonly Mission _mission;
        private readonly TargetingService _targeting;
        private readonly TeleportValidator _validator;
        private readonly EffectController _effects;
        private readonly HudService _hud;
        private readonly VoidstepLogger _logger;
        private Agent _actor;
        private GameEntity _preview;
        private TeleportValidationResult _validation;
        private float _elapsed;
        private float _refresh;
        private float _lastApplicationTime;
        private bool _ownsTimeRequest;
        private bool _timeCleanupPending;
        private bool? _lastLoggedSuccess;
        private string _lastLoggedReason;
        private Vec3 _lastLoggedPosition;
        private bool _hasLastLoggedPosition;

        public BlinkController(Mission mission, TargetingService targeting, TeleportValidator validator, EffectController effects, HudService hud, VoidstepLogger logger)
        {
            _mission = mission;
            _targeting = targeting;
            _validator = validator;
            _effects = effects;
            _hud = hud;
            _logger = logger;
        }

        public bool IsAiming { get; private set; }
        public TeleportValidationResult CurrentValidation => _validation;

        public bool Begin(Agent actor)
        {
            Cancel();
            if (actor == null || !actor.IsActive()) return false;
            _actor = actor;
            IsAiming = true;
            _elapsed = 0f;
            _refresh = 0f;
            _lastApplicationTime = MBCommon.GetApplicationTime();
            _lastLoggedSuccess = null;
            _lastLoggedReason = null;
            _lastLoggedPosition = Vec3.Invalid;
            _hasLastLoggedPosition = false;
            if (VoidstepSettings.Current.BlinkAimSlowdown)
            {
                if (_ownsTimeRequest)
                {
                    _logger.Debug("Blink aim freeze skipped while a previous time request is pending cleanup.");
                }
                else
                {
                    try
                    {
                        float existingFactor;
                        if (_mission.GetRequestedTimeSpeed(AimTimeRequestId, out existingFactor))
                        {
                            _logger.Debug("Blink aim freeze skipped because its reserved mission speed request ID is already active.");
                        }
                        else
                        {
                            _ownsTimeRequest = true;
                            _timeCleanupPending = false;
                            _mission.AddTimeSpeedRequest(new Mission.TimeSpeedRequest(0f, AimTimeRequestId));
                            _logger.Debug("Blink aim freeze acquired factor=0.00.");
                        }
                    }
                    catch (Exception ex)
                    {
                        ReleaseAimTimeRequest();
                        _logger.Debug("Blink aim freeze unavailable: " + ex.Message);
                    }
                }
            }
            RefreshPreview();
            _hud.Show("Blink targeting — time frozen. Move the camera; green is valid, red is blocked. Press Blink again to teleport.");
            _logger.Debug("Blink targeting started.");
            return true;
        }

        public void Tick(float dt)
        {
            if (_timeCleanupPending)
                ReleaseAimTimeRequest();
            if (!IsAiming) return;
            if (_actor == null || !_actor.IsActive() || _actor.Health <= 0f)
            {
                Cancel();
                return;
            }

            var now = MBCommon.GetApplicationTime();
            var realDt = _lastApplicationTime > 0f ? Math.Max(0f, now - _lastApplicationTime) : Math.Max(0f, dt);
            _lastApplicationTime = now;
            _elapsed += realDt;
            _refresh -= realDt;
            if (_refresh <= 0f)
            {
                _refresh = 0.05f;
                RefreshPreview();
            }
            if (_elapsed >= 8f)
            {
                _hud.Show("Blink targeting expired.");
                _logger.Debug("Blink targeting expired.");
                Cancel();
            }
        }

        public bool Confirm(out Vec3 destination, out string failure)
        {
            destination = Vec3.Invalid;
            failure = null;
            if (!IsAiming || _actor == null)
            {
                failure = "Blink is not currently aiming.";
                return false;
            }
            RefreshPreview();
            if (!_validation.Success)
            {
                failure = _validation.Reason ?? "No safe Blink destination was found.";
                _logger.Debug("Blink confirmation rejected: " + failure);
                return false;
            }
            destination = _validation.Position;
            _logger.Debug($"Blink confirmed destination=({destination.x:0.00}, {destination.y:0.00}, {destination.z:0.00}), fallback={_validation.UsedFallback}.");
            CancelPreviewAndTime();
            IsAiming = false;
            _actor = null;
            return true;
        }

        public void Cancel()
        {
            CancelPreviewAndTime();
            IsAiming = false;
            _elapsed = 0f;
            _refresh = 0f;
            _lastApplicationTime = 0f;
            _actor = null;
            _validation = default(TeleportValidationResult);
            _lastLoggedSuccess = null;
            _lastLoggedReason = null;
            _lastLoggedPosition = Vec3.Invalid;
            _hasLastLoggedPosition = false;
        }

        private void RefreshPreview()
        {
            if (_actor == null) return;
            var settings = VoidstepSettings.Current;
            var requested = ResolveRequestedPosition(_actor, settings.BlinkRange);
            _validation = _validator.Validate(_actor, requested, settings.BlinkRange, settings.BlinkThroughWalls);
            var position = _validation.Success ? _validation.Position : requested;
            var color = _validation.Success ? 0x60E080FFu : 0xE05050FFu;
            if (_preview == null)
                _preview = _effects.CreateWorldMarker(position + Vec3.Up * 0.15f, color);
            else
            {
                _effects.MoveMarker(_preview, position + Vec3.Up * 0.15f);
                _effects.SetMarkerColor(_preview, color);
            }

            var moved = !_hasLastLoggedPosition || (_lastLoggedPosition - position).LengthSquared > 1f;
            if (_lastLoggedSuccess != _validation.Success ||
                !string.Equals(_lastLoggedReason, _validation.Reason, StringComparison.Ordinal) || moved)
            {
                _logger.Debug($"Blink preview valid={_validation.Success}, fallback={_validation.UsedFallback}, position=({position.x:0.00}, {position.y:0.00}, {position.z:0.00}), reason={_validation.Reason ?? "none"}.");
                _lastLoggedSuccess = _validation.Success;
                _lastLoggedReason = _validation.Reason;
                _lastLoggedPosition = position;
                _hasLastLoggedPosition = true;
            }
        }

        private Vec3 ResolveRequestedPosition(Agent actor, float range)
        {
            var locked = _targeting.FindLockedEnemy(actor, range, 18f);
            if (locked != null)
            {
                var away = locked.Position - actor.Position;
                away.z = 0f;
                if (away.Normalize() < 0.001f) away = _targeting.GetAimDirection(actor);
                return locked.Position + away * 1.35f;
            }
            if (_targeting.TryGetAimedGroundPosition(actor, range, out var aimed))
                return aimed;
            return _targeting.GetForwardFallback(actor, range);
        }

        private void CancelPreviewAndTime()
        {
            if (_preview != null)
            {
                _effects.RemoveMarker(_preview);
                _preview = null;
            }
            if (_ownsTimeRequest)
            {
                _timeCleanupPending = true;
                ReleaseAimTimeRequest();
            }
        }

        private bool ReleaseAimTimeRequest()
        {
            if (!_ownsTimeRequest)
            {
                _timeCleanupPending = false;
                return true;
            }

            _timeCleanupPending = true;
            try
            {
                float requestedFactor;
                if (_mission.GetRequestedTimeSpeed(AimTimeRequestId, out requestedFactor))
                {
                    _mission.RemoveTimeSpeedRequest(AimTimeRequestId);
                    if (_mission.GetRequestedTimeSpeed(AimTimeRequestId, out requestedFactor))
                        return false;
                }

                _ownsTimeRequest = false;
                _timeCleanupPending = false;
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug("Blink aim time cleanup failed; ownership retained for retry: " + ex.Message);
                return false;
            }
        }
    }
}
