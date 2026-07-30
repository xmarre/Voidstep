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
        private float _castRecoveryRadians;
        private int _controlledAgentIndex;
        private Vec3 _castOriginalLook;
        private MissionWeapon _cleaveWeapon;
        private CleaveExecutionSnapshot _cleaveSnapshot;
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
            _darkVision.OnAgentDeleted(affectedAgent);
            if (affectedAgent != null && affectedAgent.Index == _castActorIndex)
                CancelCurrent(CancelReason.ActorRemoved);
            if (affectedAgent != null && affectedAgent.Index == _controlledAgentIndex)
                CleanupPlayerOwnedState(CancelReason.ActorRemoved);
        }

        public void OnPlayerAgentChanged(Agent previous, Agent current)
        {
            if (previous == current) return;
            CleanupPlayerOwnedState(CancelReason.ActorReplaced);
            _controlledAgentIndex = current != null ? current.Index : -1;
            _context.Energy.Reset();
            _context.Cooldowns.Clear();
        }

        public void Cleanup(CancelReason reason)
        {
            CancelCurrent(reason);
            _blink.Cancel();
            _cleave.Cleanup();
            _time.Cleanup();
            _domino.Clear();
            _darkVision.Disable();
            RestoreFov();
            RemoveCleaveMarker();
            _effects.Cleanup();
            _context.Cooldowns.Clear();
            _context.Energy.Reset();
            _hud.Reset();
        }

        private bool BeginVoidstep(Agent player)
        {
            var wieldedWeapon = player.WieldedWeapon;
            if (!WeaponValidation.IsUsableMeleeWeapon(wieldedWeapon))
                return Fail("Voidstep Cleave requires a currently wielded melee weapon.");

            var settings = VoidstepSettings.Current;
            var snapshot = CleaveExecutionSnapshot.Capture(player, settings);
            var requested = ResolveVoidstepDestination(player, snapshot.TeleportRange);
            var validation = _teleportValidator.Validate(player, requested, snapshot.TeleportRange, false);
            if (!validation.Success)
                return Fail(validation.Reason ?? "No safe Voidstep destination was found.");
            if (!PayAndStartCooldown(AbilityId.VoidstepCleave))
                return Fail("Not enough Void Energy.");

            _cleaveWeapon = wieldedWeapon;
            _cleaveSnapshot = snapshot;
            _token = _state.Begin(AbilityId.VoidstepCleave);
            _castActorIndex = player.Index;
            _destination = validation.Position;
            _castOriginalLook = snapshot.InitialFacing;
            _castRecoveryRadians = (float)((snapshot.Clockwise ? -1.0 : 1.0) * (360.0 - snapshot.SweepDegrees) * Math.PI / 180.0);
            _state.Transition(_token, AbilityPhase.Validating);
            _state.Transition(_token, AbilityPhase.WindUp);
            _cleaveMarker = _effects.CreateWorldMarker(_destination + Vec3.Up * 0.2f, 0x60E080FFu);
            BeginFovPulse();
            _context.Logger.Debug($"Voidstep Cleave locked destination={Format(_destination)}, fallback={validation.UsedFallback}, facing={Format(_castOriginalLook)}.");
            _hud.ShowAbilityResult(AbilityId.VoidstepCleave, _context.Energy, _context.Cooldowns);
            return true;
        }

        private void TickVoidstep(Agent player, float dt)
        {
            _state.Tick(_token, Math.Max(0f, dt));
            switch (_state.Phase)
            {
                case AbilityPhase.WindUp:
                    if (_state.PhaseElapsed >= 0.24f)
                    {
                        _effects.Departure(player.Position);
                        _effects.PlaySound("event:/mission/combat/swing/weapon_swing", player.Position);
                        _state.Transition(_token, AbilityPhase.Departing);
                    }
                    break;
                case AbilityPhase.Departing:
                    if (_state.PhaseElapsed >= 0.055f)
                    {
                        var validation = _teleportValidator.Validate(player, _destination, _cleaveSnapshot.TeleportRange, false);
                        if (!validation.Success)
                        {
                            RollbackPayment(AbilityId.VoidstepCleave);
                            Fail(validation.Reason ?? "The Voidstep destination became invalid.");
                            CancelCurrent(CancelReason.InvalidDestination);
                            return;
                        }
                        _destination = validation.Position;
                        _animation.SetActorFacing(player, _castOriginalLook);
                        TeleportActor(player, _destination, false);
                        _animation.SetActorFacing(player, _castOriginalLook);
                        RemoveCleaveMarker();
                        _context.Logger.Debug($"Voidstep Cleave teleported actor to {Format(player.Position)} without changing facing={Format(player.LookDirection)}.");
                        _state.Transition(_token, AbilityPhase.Teleporting);
                    }
                    break;
                case AbilityPhase.Teleporting:
                    _effects.Arrival(player.Position);
                    _state.Transition(_token, AbilityPhase.Arriving);
                    break;
                case AbilityPhase.Arriving:
                    if (_state.PhaseElapsed >= 0.08f)
                    {
                        _animation.SetActorFacing(player, _castOriginalLook);
                        if (!_cleave.Begin(player, _cleaveWeapon, _cleaveSnapshot, out var failure))
                        {
                            RollbackPayment(AbilityId.VoidstepCleave);
                            Fail(failure ?? "Cleave execution could not start.");
                            CancelCurrent(CancelReason.Interrupted);
                            return;
                        }
                        _state.Transition(_token, AbilityPhase.Active);
                    }
                    break;
                case AbilityPhase.Active:
                    if (_cleave.Tick(dt))
                    {
                        _context.Logger.Debug($"Voidstep Cleave active phase finished; hits={_cleave.SuccessfulHits}.");
                        RestoreFov();
                        _state.Transition(_token, AbilityPhase.Recovery);
                    }
                    break;
                case AbilityPhase.Recovery:
                    var progress = Math.Min(1f, _state.PhaseElapsed / 0.25f);
                    var facing = _castOriginalLook;
                    facing.RotateAboutZ((float)(_cleaveSnapshot.SignedSweepRadians + _castRecoveryRadians * progress));
                    _animation.SetActorFacing(player, facing);
                    if (_state.PhaseElapsed >= 0.25f)
                    {
                        _animation.SetActorFacing(player, _castOriginalLook);
                        CompleteCurrent();
                    }
                    break;
            }
        }

        private bool BeginBlink(Agent player)
        {
            _token = _state.Begin(AbilityId.Blink);
            _castActorIndex = player.Index;
            if (_blink.Begin(player)) return true;
            CancelCurrent(CancelReason.InvalidActor);
            return Fail("Blink aiming could not start.");
        }

        private bool ConfirmBlink(Agent player)
        {
            if (!CanPay(AbilityId.Blink)) return Fail("Not enough Void Energy.");
            if (!_blink.Confirm(out var destination, out var failure))
                return Fail(failure);

            _state.Transition(_token, AbilityPhase.Validating);
            if (!PayAndStartCooldown(AbilityId.Blink))
            {
                CancelCurrent(CancelReason.Interrupted);
                return Fail("Not enough Void Energy.");
            }
            _state.Transition(_token, AbilityPhase.WindUp);
            _effects.Departure(player.Position);
            _state.Transition(_token, AbilityPhase.Departing);
            TeleportActor(player, destination, VoidstepSettings.Current.BlinkPreserveMomentum);
            _state.Transition(_token, AbilityPhase.Teleporting);
            _effects.Arrival(player.Position);
            _effects.PlaySound("event:/mission/combat/swing/weapon_swing", player.Position);
            _state.Transition(_token, AbilityPhase.Arriving);
            _state.Transition(_token, AbilityPhase.Active);
            _state.Transition(_token, AbilityPhase.Recovery);
            _context.Logger.Debug($"Blink completed at {Format(player.Position)}.");
            _hud.ShowAbilityResult(AbilityId.Blink, _context.Energy, _context.Cooldowns);
            CompleteCurrent();
            return true;
        }

        private bool CastWindblast(Agent player)
        {
            if (!PayAndStartCooldown(AbilityId.Windblast))
                return Fail("Not enough Void Energy.");
            var hitCount = _windblast.Cast(player);
            if (hitCount <= 0)
            {
                RollbackPayment(AbilityId.Windblast);
                return Fail("Windblast found no valid enemy in the aimed cone.");
            }
            _effects.PlaySound("event:/mission/combat/hit/weapon_hit", player.Position);
            CompleteImmediate(AbilityId.Windblast, player);
            _hud.Show($"Windblast affected {hitCount} target{(hitCount == 1 ? string.Empty : "s")}.");
            return true;
        }

        private bool CastBendTime(Agent player)
        {
            var settings = VoidstepSettings.Current;
            if (!_time.Begin(player, settings.BendTimeFactor, settings.BendTimeDuration, settings.AllowCompleteSuspension))
                return Fail("Bend Time could not acquire a mission speed request.");
            if (!PayAndStartCooldown(AbilityId.BendTime))
            {
                _time.Release();
                return false;
            }
            _effects.BendTime(player.Position);
            _effects.PlaySound("event:/mission/ambient/night", player.Position);
            CompleteImmediate(AbilityId.BendTime, player);
            _context.Logger.Debug($"Bend Time started factor={settings.BendTimeFactor:0.00}, duration={settings.BendTimeDuration:0.00}.");
            _hud.ShowAbilityResult(AbilityId.BendTime, _context.Energy, _context.Cooldowns);
            return true;
        }

        private bool CastDomino(Agent player)
        {
            var count = _domino.Mark(player);
            _context.Logger.Debug($"Domino targeting selected {count} valid enemies.");
            if (count < 2)
            {
                _domino.Clear();
                return Fail("Domino requires at least two valid enemy targets.");
            }
            if (!PayAndStartCooldown(AbilityId.Domino))
            {
                _domino.Clear();
                return false;
            }
            _effects.PlaySound("event:/mission/combat/hit/weapon_hit", player.Position);
            CompleteImmediate(AbilityId.Domino, player);
            _hud.Show($"Domino linked {count} enemies.");
            return true;
        }

        private bool CastDarkVision(Agent player)
        {
            if (!_darkVision.Toggle(player)) return Fail("Dark Vision could not start.");
            if (!PayAndStartCooldown(AbilityId.DarkVision))
            {
                _darkVision.Disable();
                return false;
            }
            _effects.PlaySound("event:/mission/ambient/night", player.Position);
            CompleteImmediate(AbilityId.DarkVision, player);
            _hud.ShowAbilityResult(AbilityId.DarkVision, _context.Energy, _context.Cooldowns);
            return true;
        }

        private void CompleteImmediate(AbilityId ability, Agent player)
        {
            _token = _state.Begin(ability);
            _castActorIndex = player.Index;
            _state.Transition(_token, AbilityPhase.WindUp);
            _state.Transition(_token, AbilityPhase.Active);
            _state.Transition(_token, AbilityPhase.Recovery);
            CompleteCurrent();
        }

        private void CompleteCurrent()
        {
            if (_token != default(CastToken))
            {
                _state.Finish(_token);
                _token = default(CastToken);
            }
            _castActorIndex = -1;
            _destination = Vec3.Invalid;
            _castRecoveryRadians = 0f;
            _castOriginalLook = Vec3.Zero;
            _cleaveWeapon = default(MissionWeapon);
            _cleaveSnapshot = default(CleaveExecutionSnapshot);
            RemoveCleaveMarker();
            RestoreFov();
        }

        private void CancelCurrent(CancelReason reason)
        {
            if (_token != default(CastToken))
            {
                try { _state.Cancel(_token, reason); }
                catch { _state.ForceReset(reason); }
                _state.ForceReset(reason);
            }
            _blink.Cancel();
            _cleave.Cleanup();
            var actor = _context.Player;
            if (actor != null && actor.IsActive() && actor.Index == _castActorIndex && _castOriginalLook.LengthSquared > 0.001f)
            {
                try { _animation.SetActorFacing(actor, _castOriginalLook); } catch { }
            }
            _context.Logger.Debug($"Ability state cancelled reason={reason}.");
            _token = default(CastToken);
            _castActorIndex = -1;
            _destination = Vec3.Invalid;
            _castRecoveryRadians = 0f;
            _castOriginalLook = Vec3.Zero;
            _cleaveWeapon = default(MissionWeapon);
            _cleaveSnapshot = default(CleaveExecutionSnapshot);
            RemoveCleaveMarker();
            RestoreFov();
        }

        private void CleanupPlayerOwnedState(CancelReason reason)
        {
            CancelCurrent(reason);
            _time.Release();
            _domino.Clear();
            _darkVision.Disable();
        }

        private bool IsCurrentCastActorValid(Agent actor) =>
            actor != null && actor.Index == _castActorIndex && actor.IsActive() && actor.Health > 0f && actor.State == TaleWorlds.Core.AgentState.Active;

        private Vec3 ResolveVoidstepDestination(Agent player, float range)
        {
            var locked = _targeting.FindLockedEnemy(player, range, 30f);
            if (locked != null)
            {
                var travel = locked.Position - player.Position;
                travel.z = 0f;
                if (travel.Normalize() < 0.001f) travel = _targeting.GetAimDirection(player);
                _context.Logger.Debug($"Voidstep Cleave locked enemy={locked.Index} at {Format(locked.Position)}.");
                return locked.Position + travel * 1.5f;
            }
            if (_targeting.TryGetAimedGroundPosition(player, range, out var aimed))
            {
                _context.Logger.Debug($"Voidstep Cleave selected aimed ground {Format(aimed)}.");
                return aimed;
            }
            var fallback = _targeting.GetForwardFallback(player, Math.Min(range, 5f));
            _context.Logger.Debug($"Voidstep Cleave used forward fallback {Format(fallback)}.");
            return fallback;
        }

        private void TeleportActor(Agent actor, Vec3 position, bool preserveMomentum)
        {
            var actorFacing = CaptureHorizontalFacing(actor);
            var mount = actor.MountAgent;
            var mountFacing = mount != null && mount.IsActive() ? CaptureHorizontalFacing(mount) : Vec3.Zero;
            if (mount != null && mount.IsActive())
            {
                mount.TeleportToPosition(position);
                actor.TeleportToPosition(position + Vec3.Up * 0.4f);
            }
            else
            {
                actor.TeleportToPosition(position);
            }

            if (!preserveMomentum)
            {
                var zero = Vec2.Zero;
                actor.MovementInputVector = zero;
                actor.SetMovementDirection(in zero);
                if (mount != null && mount.IsActive())
                {
                    mount.MovementInputVector = zero;
                    mount.SetMovementDirection(in zero);
                }
            }

            if (mount != null && mount.IsActive())
                _animation.SetActorFacing(mount, mountFacing);
            _animation.SetActorFacing(actor, actorFacing);
        }

        private static Vec3 CaptureHorizontalFacing(Agent actor)
        {
            var facing = actor != null ? actor.LookDirection : Vec3.Forward;
            facing.z = 0f;
            if (facing.Normalize() < 0.001f)
                facing = Vec3.Forward;
            return facing;
        }

        private bool CanPay(AbilityId ability)
        {
            var settings = VoidstepSettings.Current;
            return _context.Energy.CanSpend(settings.Cost(ability), settings.UnlimitedEnergy, !settings.EnergyEnabled || settings.CooldownOnlyMode);
        }

        private bool PayAndStartCooldown(AbilityId ability)
        {
            var settings = VoidstepSettings.Current;
            if (!_context.Energy.TrySpend(settings.Cost(ability), settings.UnlimitedEnergy, !settings.EnergyEnabled || settings.CooldownOnlyMode))
                return false;
            _context.Cooldowns.Start(ability, settings.Cooldown(ability));
            return true;
        }

        private void RollbackPayment(AbilityId ability)
        {
            var settings = VoidstepSettings.Current;
            if (settings.EnergyEnabled && !settings.CooldownOnlyMode && !settings.UnlimitedEnergy)
                _context.Energy.Refund(settings.Cost(ability));
            _context.Cooldowns.Clear(ability);
        }

        private bool Fail(string message)
        {
            _context.Logger.Debug("Ability rejected: " + message);
            _hud.Show(message);
            return false;
        }

        private void BeginFovPulse()
        {
            if (!VoidstepSettings.Current.CameraShake || _fovOwned) return;
            try
            {
                _previousFov = _context.Mission.CustomCameraFovMultiplier;
                _context.Mission.SetCustomCameraFovMultiplier(OwnedFov);
                _fovOwned = true;
            }
            catch (Exception ex) { _context.Logger.Debug("Optional FOV pulse unavailable: " + ex.Message); }
        }

        private void RestoreFov()
        {
            if (!_fovOwned) return;
            try
            {
                if (Math.Abs(_context.Mission.CustomCameraFovMultiplier - OwnedFov) < 0.001f)
                    _context.Mission.SetCustomCameraFovMultiplier(_previousFov);
            }
            catch { }
            _fovOwned = false;
        }

        private void RemoveCleaveMarker()
        {
            if (_cleaveMarker == null) return;
            _effects.RemoveMarker(_cleaveMarker);
            _cleaveMarker = null;
        }

        private static string Format(Vec3 value) => $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";
    }
}
