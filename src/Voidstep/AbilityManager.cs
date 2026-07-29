using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class AbilityManager
    {
        private readonly AbilityContext _context;
        private readonly CastStateMachine _state = new CastStateMachine();
        private readonly TargetingService _targeting;
        private readonly TeleportValidator _teleportValidator;
        private readonly AnimationController _animation;
        private readonly EffectController _effects;
        private readonly BlowFactory _blows;
        private readonly CleaveSweepController _cleave;
        private readonly WindblastController _windblast;
        private readonly TimeControlService _time;
        private readonly DominoLinkService _domino;
        private readonly DarkVisionService _darkVision;
        private readonly HudService _hud;
        private readonly BlinkController _blink;

        private CastToken _token;
        private Vec3 _destination;
        private int _castActorIndex = -1;
        private float _configuredMaximum;
        private bool _fovOwned;
        private float _previousFov;
        private float _recoveryRotationProgress;
        private float _castRecoveryRadians;
        private int _controlledAgentIndex;
        private Vec3 _castOriginalLook;
        private MissionWeapon _cleaveWeapon;
        private GameEntity _cleaveMarker;
        private const float OwnedFov = 1.08f;

        public AbilityManager(AbilityContext context)
        {
            _context = context;
            _targeting = new TargetingService(context.Mission);
            _teleportValidator = new TeleportValidator(context.Mission);
            _animation = new AnimationController(context.Logger);
            _effects = new EffectController(context.Mission, context.Logger);
            _blows = new BlowFactory(context.Mission, context.Logger);
            _hud = new HudService();
            _cleave = new CleaveSweepController(context.Mission, _blows, _effects, _animation, context.Logger);
            _windblast = new WindblastController(context.Mission, _blows, _effects, _targeting, context.Logger);
            _time = new TimeControlService(context.Mission, context.Logger);
            _domino = new DominoLinkService(context.Mission, _blows, _effects, context.Logger);
            _darkVision = new DarkVisionService(context.Mission, context.Logger);
            _blink = new BlinkController(context.Mission, _targeting, _teleportValidator, _effects, _hud, context.Logger);
            _configuredMaximum = VoidstepSettings.Current.MaximumEnergy;
            _controlledAgentIndex = context.Player != null ? context.Player.Index : -1;
        }

        public AbilityPhase Phase => _state.Phase;
        public AbilityId ActiveAbility => _state.Ability;
        public bool IsBusy => _state.IsCasting;
        internal bool IsDarkVisionActive => _darkVision.Active;
        internal VoidstepLogger Logger => _context.Logger;

        public void Tick(float dt)
        {
            var settings = VoidstepSettings.Current;
            if (Math.Abs(settings.MaximumEnergy - _configuredMaximum) > 0.001f)
            {
                _configuredMaximum = settings.MaximumEnergy;
                _context.Energy.ConfigureMaximum(_configuredMaximum, false);
            }

            _context.Cooldowns.Tick(Math.Max(0f, dt));
            if (settings.EnergyEnabled && !settings.CooldownOnlyMode && !settings.UnlimitedEnergy)
                _context.Energy.Regenerate(Math.Max(0f, dt) * Math.Max(0f, settings.EnergyRegeneration));

            _time.Tick(dt);
            _blink.Tick(dt);
            _darkVision.Tick(dt);
            _domino.Tick();

            if (_state.IsCasting)
            {
                var actor = _context.Player;
                if (!IsCurrentCastActorValid(actor))
                {
                    CancelCurrent(actor == null ? CancelReason.ActorRemoved : CancelReason.ActorReplaced);
                }
                else if (_state.Ability == AbilityId.VoidstepCleave)
                {
                    TickVoidstep(actor, dt);
                }
                else if (_state.Ability == AbilityId.Blink && _state.Phase == AbilityPhase.Targeting)
                {
                    if (!_blink.IsAiming)
                        CancelCurrent(CancelReason.Interrupted);
                    else
                        _state.Tick(_token, Math.Max(0f, dt));
                }
            }

            _hud.Tick(dt, _context.Energy, _darkVision.Active, _time.Active);
        }

        public bool TryActivate(AbilityId ability)
        {
            var settings = VoidstepSettings.Current;
            if (!settings.Enabled)
                return Fail("Voidstep is disabled in MCM.");

            var player = _context.Player;
            if (!_context.IsPlayerUsable())
                return Fail("No active player agent is available.");

            _context.Logger.Debug($"Activation requested ability={ability}, actor={player.Index}, position={Format(player.Position)}, look={Format(_targeting.GetAimDirection(player))}, weaponEmpty={player.WieldedWeapon.IsEmpty}.");

            if (ability == AbilityId.Blink && _state.IsCasting && _state.Ability == AbilityId.Blink && _state.Phase == AbilityPhase.Targeting)
                return ConfirmBlink(player);

            if (_state.IsCasting)
                return Fail("Another ability is already in progress.");

            if (ability == AbilityId.DarkVision && _darkVision.Active)
            {
                _darkVision.Disable();
                _hud.Show("Dark Vision disabled.");
                _context.Logger.Debug("Dark Vision toggled off.");
                return true;
            }

            if (!_context.Cooldowns.IsReady(ability))
                return Fail($"{ability} is on cooldown for {_context.Cooldowns.GetRemaining(ability):0.0}s.");
            if (!CanPay(ability))
                return Fail("Not enough Void Energy.");

            try
            {
                switch (ability)
                {
                    case AbilityId.VoidstepCleave: return BeginVoidstep(player);
                    case AbilityId.Blink: return BeginBlink(player);
                    case AbilityId.Windblast: return CastWindblast(player);
                    case AbilityId.BendTime: return CastBendTime(player);
                    case AbilityId.Domino: return CastDomino(player);
                    case AbilityId.DarkVision: return CastDarkVision(player);
                    default: return false;
                }
            }
            catch (Exception ex)
            {
                _context.Logger.Error("Ability activation failed and was rolled back.", ex);
                CancelCurrent(CancelReason.Exception);
                return Fail("Ability activation failed safely. See the Voidstep log when debug logging is enabled.");
            }
        }

        public void OnAgentHit(Agent affectedAgent, Agent affectorAgent, ref Blow blow) =>
            _domino.OnAgentHit(affectedAgent, affectorAgent, ref blow);

        public void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, TaleWorlds.Core.AgentState state)
        {
            _domino.OnAgentRemoved(affectedAgent, affectorAgent, state);
            if (affectedAgent != null && affectedAgent.Index == _castActorIndex)
                CancelCurrent(affectedAgent.Health <= 0f ? CancelReason.ActorDied : CancelReason.ActorRemoved);
            if (affectedAgent != null && affectedAgent.Index == _controlledAgentIndex)
                CleanupPlayerOwnedState(CancelReason.ActorDied);
        }

        public void OnAgentDeleted(Agent affectedAgent)
        {
            _domino.OnAgentDeleted(affectedAgent);
            if (affectedAgent != null && affectedAgent.Index == _castActorIndex)
                CancelCurrent(CancelReason.ActorRemoved);
            if (affectedAgent != null && affectedAgent.Index == _controlledAgentIndex)
                CleanupPlayerOwnedState(CancelReason.ActorRemoved);
        }

        public void OnPlayerAgentChanged(Agent previous, Agent current)
        {
            if (ReferenceEquals(previous, current)) return;
            CleanupPlayerOwnedState(CancelReason.ActorReplaced);
            _context.Player = current;
            _controlledAgentIndex = current != null ? current.Index : -1;
            _configuredMaximum = VoidstepSettings.Current.MaximumEnergy;
            _context.Energy.ConfigureMaximum(_configuredMaximum, true);
            _context.Logger.Info($"Controlled agent changed from {previous?.Index.ToString() ?? "none"} to {current?.Index.ToString() ?? "none"}; all prior agent-owned state was cleared.");
        }

        public void Cleanup(CancelReason reason)
        {
            CleanupPlayerOwnedState(reason);
            _effects.Cleanup();
            _hud.Clear();
        }

        private bool BeginVoidstep(Agent player)
        {
            var settings = VoidstepSettings.Current;
            if (!WeaponValidation.IsUsableMeleeWeapon(player.WieldedWeapon))
                return Fail("Voidstep Cleave requires an equipped melee weapon.");

            var locked = _targeting.FindLockedEnemy(player, settings.TeleportRange);
            Vec3 requested;
            if (locked != null)
            {
                var away = locked.Position - player.Position;
                away.z = 0f;
                if (away.Normalize() < 0.001f) away = _targeting.GetAimDirection(player);
                requested = locked.Position - away * 1.15f;
                _context.Logger.Debug($"Voidstep Cleave locked enemy={locked.Index}, enemyPosition={Format(locked.Position)}, requested={Format(requested)}.");
            }
            else if (_targeting.TryGetAimedGroundPosition(player, settings.TeleportRange, out var aimed))
            {
                requested = aimed;
                _context.Logger.Debug($"Voidstep Cleave used camera ground aim requested={Format(requested)}.");
            }
            else
            {
                requested = _targeting.GetForwardFallback(player, Math.Min(settings.TeleportRange, 4f));
                _context.Logger.Debug($"Voidstep Cleave used forward fallback requested={Format(requested)}.");
            }

            var validation = _teleportValidator.Validate(player, requested, settings.TeleportRange, settings.TeleportThroughWalls);
            _context.Logger.Debug($"Voidstep Cleave validation success={validation.Success}, fallback={validation.UsedFallback}, destination={Format(validation.Position)}, reason={validation.Reason ?? "none"}.");
            if (!validation.Success)
                return Fail("Voidstep destination rejected: " + validation.Reason);

            _cleaveWeapon = player.WieldedWeapon;
            _destination = validation.Position;
            _token = _state.Start(AbilityId.VoidstepCleave, AbilityPhase.WindUp, 0.16f);
            _castActorIndex = player.Index;
            _castOriginalLook = player.LookDirection;
            _recoveryRotationProgress = 0f;
            _castRecoveryRadians = 0f;
            _cleaveMarker = _effects.CreateWorldMarker(_destination + Vec3.Up * 0.15f, 0x60E080FFu);
            if (_cleaveMarker == null)
                _context.Logger.Debug("Voidstep Cleave placement reticle was unavailable; cast will continue without it.");
            EmphasizeCamera();
            _effects.Departure(player.Position);
            _context.Logger.Debug($"Voidstep Cleave wind-up started token={_token}, destination={Format(_destination)}, weapon={FormatWeapon(_cleaveWeapon)}.");
            return true;
        }

        private bool BeginBlink(Agent player)
        {
            if (!_blink.Begin(player))
                return Fail("Blink targeting could not start.");
            _token = _state.Start(AbilityId.Blink, AbilityPhase.Targeting, 8f);
            _castActorIndex = player.Index;
            EmphasizeCamera();
            return true;
        }

        private bool ConfirmBlink(Agent player)
        {
            if (!_blink.Confirm(out var destination, out var failure))
                return Fail(failure ?? "Blink destination is invalid.");
            _context.Logger.Debug($"Blink destination accepted destination={Format(destination)}.");
            if (!PayAndStartCooldown(AbilityId.Blink))
                return Fail("Blink could not pay its cost.");
            var original = player.Position;
            _effects.Departure(original);
            if (!TryTeleportActor(player, destination, VoidstepSettings.Current.BlinkPreserveMomentum, out var actual))
            {
                RollbackPayment(AbilityId.Blink);
                return Fail("Blink teleport failed safely.");
            }
            _effects.Arrival(actual);
            _context.Logger.Debug($"Blink completed origin={Format(original)}, destination={Format(actual)}.");
            _state.Cancel(_token);
            _token = default(CastToken);
            _castActorIndex = -1;
            RestoreCamera();
            return true;
        }

        private bool CastWindblast(Agent player)
        {
            if (!PayAndStartCooldown(AbilityId.Windblast)) return false;
            _effects.Windblast(player.Position);
            _windblast.Cast(player);
            return true;
        }

        private bool CastBendTime(Agent player)
        {
            var settings = VoidstepSettings.Current;
            if (!PayAndStartCooldown(AbilityId.BendTime)) return false;
            if (!_time.Begin(player, settings.BendTimeFactor, settings.BendTimeDuration, false))
            {
                RollbackPayment(AbilityId.BendTime);
                return Fail("Bend Time request could not be acquired.");
            }
            _effects.BendTime(player.Position);
            _context.Logger.Debug($"Bend Time started factor={settings.BendTimeFactor:0.00}, duration={settings.BendTimeDuration:0.00}.");
            return true;
        }

        private bool CastDomino(Agent player)
        {
            if (!PayAndStartCooldown(AbilityId.Domino)) return false;
            var count = _domino.Mark(player);
            if (count == 0)
            {
                RollbackPayment(AbilityId.Domino);
                return Fail("Domino found no valid hostile humans.");
            }
            _context.Logger.Debug($"Domino linked {count} hostile agents.");
            _hud.Show($"Domino linked {count} targets.");
            return true;
        }

        private bool CastDarkVision(Agent player)
        {
            if (!PayAndStartCooldown(AbilityId.DarkVision)) return false;
            _darkVision.Enable(player);
            _context.Logger.Debug($"Dark Vision enabled range={VoidstepSettings.Current.DarkVisionRange:0.0}.");
            return true;
        }

        private void TickVoidstep(Agent actor, float dt)
        {
            var settings = VoidstepSettings.Current;
            if (_state.Phase == AbilityPhase.WindUp)
            {
                var transition = _state.Tick(_token, Math.Max(0f, dt));
                if (transition == StateTransition.Completed)
                {
                    if (!PayAndStartCooldown(AbilityId.VoidstepCleave))
                    {
                        CancelCurrent(CancelReason.UserCancelled);
                        return;
                    }
                    var validation = _teleportValidator.Validate(actor, _destination, settings.TeleportRange, settings.TeleportThroughWalls);
                    _context.Logger.Debug($"Voidstep pre-teleport revalidation success={validation.Success}, fallback={validation.UsedFallback}, destination={Format(validation.Position)}, reason={validation.Reason ?? "none"}.");
                    if (!validation.Success)
                    {
                        RollbackPayment(AbilityId.VoidstepCleave);
                        Fail("Voidstep destination became unsafe: " + validation.Reason);
                        CancelCurrent(CancelReason.Interrupted);
                        return;
                    }
                    _destination = validation.Position;
                    RemoveCleaveMarker();
                    var origin = actor.Position;
                    if (!TryTeleportActor(actor, _destination, settings.PreserveMomentum, out var actual))
                    {
                        RollbackPayment(AbilityId.VoidstepCleave);
                        Fail("Voidstep teleport failed safely.");
                        CancelCurrent(CancelReason.Exception);
                        return;
                    }
                    _destination = actual;
                    _effects.Arrival(actual);
                    _context.Logger.Debug($"Voidstep teleport completed origin={Format(origin)}, destination={Format(actual)}, actor={actor.Index}.");

                    if (!_cleave.Begin(actor, _cleaveWeapon, _targeting.GetAimDirection(actor), settings))
                    {
                        RollbackPayment(AbilityId.VoidstepCleave);
                        Fail("Voidstep Cleave could not start its active sweep.");
                        CancelCurrent(CancelReason.Exception);
                        return;
                    }
                    _token = _state.Start(AbilityId.VoidstepCleave, AbilityPhase.Active, 0.72f);
                }
                return;
            }

            if (_state.Phase == AbilityPhase.Active)
            {
                var elapsed = Math.Max(0f, _state.Duration - _state.Remaining);
                var progress = Math.Min(1f, elapsed / Math.Max(0.01f, _state.Duration));
                _cleave.Tick(actor, progress);
                var transition = _state.Tick(_token, Math.Max(0f, dt));
                if (transition == StateTransition.Completed)
                {
                    _cleave.Complete();
                    _recoveryRotationProgress = 0f;
                    _castRecoveryRadians = 0f;
                    var currentLook = actor.LookDirection;
                    currentLook.z = 0f;
                    var originalLook = _castOriginalLook;
                    originalLook.z = 0f;
                    if (currentLook.Normalize() >= 0.001f && originalLook.Normalize() >= 0.001f)
                    {
                        var cross = currentLook.x * originalLook.y - currentLook.y * originalLook.x;
                        var dot = Math.Max(-1f, Math.Min(1f, Vec3.DotProduct(currentLook, originalLook)));
                        _castRecoveryRadians = (float)Math.Atan2(cross, dot);
                    }
                    _token = _state.Start(AbilityId.VoidstepCleave, AbilityPhase.Recovery, 0.18f);
                }
                return;
            }

            if (_state.Phase == AbilityPhase.Recovery)
            {
                var before = _state.Remaining;
                var transition = _state.Tick(_token, Math.Max(0f, dt));
                var consumed = Math.Max(0f, before - _state.Remaining);
                var total = Math.Max(0.01f, _state.Duration);
                var targetProgress = Math.Min(1f, _recoveryRotationProgress + consumed / total);
                var deltaProgress = targetProgress - _recoveryRotationProgress;
                if (deltaProgress > 0f && Math.Abs(_castRecoveryRadians) > 0.0001f)
                    _animation.RotateActor(actor, _castRecoveryRadians * deltaProgress);
                _recoveryRotationProgress = targetProgress;
                if (transition == StateTransition.Completed)
                {
                    actor.LookDirection = _castOriginalLook;
                    CancelCurrent(CancelReason.Completed);
                }
            }
        }

        private bool PayAndStartCooldown(AbilityId ability)
        {
            if (!CanPay(ability)) return false;
            var settings = VoidstepSettings.Current;
            if (settings.EnergyEnabled && !settings.CooldownOnlyMode && !settings.UnlimitedEnergy)
                _context.Energy.TrySpend(settings.GetCost(ability));
            _context.Cooldowns.Start(ability, settings.GetCooldown(ability));
            _context.Logger.Debug($"Paid ability={ability}, energy={_context.Energy.Current:0.0}, cooldown={settings.GetCooldown(ability):0.0}.");
            return true;
        }

        private void RollbackPayment(AbilityId ability)
        {
            var settings = VoidstepSettings.Current;
            if (settings.EnergyEnabled && !settings.CooldownOnlyMode && !settings.UnlimitedEnergy)
                _context.Energy.Add(settings.GetCost(ability));
            _context.Cooldowns.Reset(ability);
            _context.Logger.Debug($"Rolled back ability payment for {ability}.");
        }

        private bool CanPay(AbilityId ability)
        {
            var settings = VoidstepSettings.Current;
            return settings.CooldownOnlyMode || !settings.EnergyEnabled || settings.UnlimitedEnergy ||
                   _context.Energy.Current >= settings.GetCost(ability);
        }

        private bool TryTeleportActor(Agent actor, Vec3 destination, bool preserveMomentum, out Vec3 actual)
        {
            actual = destination;
            try
            {
                Vec2 velocity = Vec2.Zero;
                if (preserveMomentum)
                    velocity = actor.Velocity.AsVec2;
                actor.TeleportToPosition(destination);
                actual = actor.Position;
                if (preserveMomentum)
                    actor.SetMovementDirection(in velocity);
                return true;
            }
            catch (Exception ex)
            {
                _context.Logger.Error("Teleport failed.", ex);
                return false;
            }
        }

        private void EmphasizeCamera()
        {
            if (!VoidstepSettings.Current.CameraEmphasis || _fovOwned) return;
            try
            {
                _previousFov = _context.Mission.GetCameraFov();
                _context.Mission.SetCustomCameraFovMultiplier(OwnedFov);
                _fovOwned = true;
            }
            catch (Exception ex) { _context.Logger.Debug("Camera emphasis unavailable: " + ex.Message); }
        }

        private void RestoreCamera()
        {
            if (!_fovOwned) return;
            try { _context.Mission.SetCustomCameraFovMultiplier(1f); }
            catch (Exception ex) { _context.Logger.Debug("Camera cleanup failed: " + ex.Message); }
            _fovOwned = false;
            _previousFov = 0f;
        }

        private bool IsCurrentCastActorValid(Agent actor) =>
            actor != null && actor.IsActive() && actor.Health > 0f && actor.Index == _castActorIndex;

        private void CancelCurrent(CancelReason reason)
        {
            _blink.Cancel();
            _cleave.Cancel();
            RemoveCleaveMarker();
            RestoreCamera();
            if (_state.IsCasting)
                _state.Cancel(_token);
            _token = default(CastToken);
            _castActorIndex = -1;
            _destination = Vec3.Invalid;
            _recoveryRotationProgress = 0f;
            _castRecoveryRadians = 0f;
            _cleaveWeapon = default(MissionWeapon);
            _context.Logger.Debug("Current cast cancelled: " + reason);
        }

        private void CleanupPlayerOwnedState(CancelReason reason)
        {
            CancelCurrent(reason);
            _time.Cleanup();
            _domino.Clear();
            _darkVision.Disable();
            _controlledAgentIndex = -1;
        }

        private void RemoveCleaveMarker()
        {
            if (_cleaveMarker == null) return;
            _effects.RemoveMarker(_cleaveMarker);
            _cleaveMarker = null;
        }

        private bool Fail(string message)
        {
            _hud.Show(message);
            _context.Logger.Debug("Activation rejected: " + message);
            return false;
        }

        private static string Format(Vec3 value) => $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";

        private static string FormatWeapon(MissionWeapon weapon)
        {
            if (weapon.IsEmpty) return "empty";
            try { return weapon.Item?.StringId ?? "unknown"; }
            catch { return "unknown"; }
        }
    }
}
