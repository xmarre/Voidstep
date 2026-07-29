using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    internal sealed class BlinkController
    {
        private const int AimTimeRequestId = 0x5653424C; // VSBL
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
        private bool _ownsTimeRequest;
        private bool _timeCleanupPending;

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
            if (VoidstepSettings.Current.BlinkAimSlowdown)
            {
                if (_ownsTimeRequest)
                {
                    _logger.Debug("Blink aim slowdown skipped while a previous time request is pending cleanup.");
                }
                else
                {
                    try
                    {
                        float existingFactor;
                        if (_mission.GetRequestedTimeSpeed(AimTimeRequestId, out existingFactor))
                        {
                            _logger.Debug("Blink aim slowdown skipped because its reserved mission speed request ID is already active.");
                        }
                        else
                        {
                            // Mark ownership before adding so a partially completed
                            // native call is still cleaned through the verified release path.
                            _ownsTimeRequest = true;
                            _timeCleanupPending = false;
                            _mission.AddTimeSpeedRequest(new Mission.TimeSpeedRequest(0.35f, AimTimeRequestId));
                        }
                    }
                    catch (Exception ex)
                    {
                        ReleaseAimTimeRequest();
                        _logger.Debug("Blink aim slowdown unavailable: " + ex.Message);
                    }
                }
            }
            RefreshPreview();
            _hud.Show("Blink aiming — press the Blink key again to confirm.");
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
            _elapsed += Math.Max(0f, dt);
            _refresh -= Math.Max(0f, dt);
            if (_refresh <= 0f)
            {
                _refresh = 0.05f;
                RefreshPreview();
            }
            if (_elapsed >= 8f)
            {
                _hud.Show("Blink aiming expired.");
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
                return false;
            }
            destination = _validation.Position;
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
            _actor = null;
            _validation = default(TeleportValidationResult);
        }

        private void RefreshPreview()
        {
            if (_actor == null) return;
            var settings = VoidstepSettings.Current;
            var requested = ResolveRequestedPosition(_actor, settings.BlinkRange);
            _validation = _validator.Validate(_actor, requested, settings.BlinkRange, settings.BlinkThroughWalls);
            var position = _validation.Success ? _validation.Position : requested;
            var color = _validation.Success ? 0x60E080FFu : 0xE05050FFu;
            if (_preview == null) _preview = _effects.CreateWorldMarker(position + Vec3.Up * 0.15f, color);
            else
            {
                _effects.MoveMarker(_preview, position + Vec3.Up * 0.15f);
                try { _preview.SetContourColor(color, true); } catch { }
            }
        }

        private Vec3 ResolveRequestedPosition(Agent actor, float range)
        {
            var locked = _targeting.FindLockedEnemy(actor, range, 18f);
            if (locked != null)
            {
                var away = locked.Position - actor.Position;
                away.z = 0f;
                if (away.Normalize() < 0.001f) away = actor.LookDirection;
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
                // RemoveTimeSpeedRequest throws when its ID is absent, so always
                // confirm removal and retain ownership if native cleanup fails.
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
