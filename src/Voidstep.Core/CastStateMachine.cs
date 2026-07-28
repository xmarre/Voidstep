using System;

namespace Voidstep.Core
{
    public sealed class CastStateMachine
    {
        private long _nextToken;

        public CastToken Token { get; private set; }
        public AbilityId Ability { get; private set; }
        public AbilityPhase Phase { get; private set; } = AbilityPhase.Idle;
        public float PhaseElapsed { get; private set; }
        public CancelReason LastCancelReason { get; private set; }
        public bool IsCasting => Phase != AbilityPhase.Idle && Phase != AbilityPhase.Cooldown && Phase != AbilityPhase.Cancelled;

        public CastToken Begin(AbilityId ability)
        {
            if (IsCasting)
                throw new InvalidOperationException("A cast is already active.");
            Token = new CastToken(++_nextToken);
            Ability = ability;
            Phase = AbilityPhase.Targeting;
            PhaseElapsed = 0f;
            LastCancelReason = CancelReason.None;
            return Token;
        }

        public void Transition(CastToken token, AbilityPhase next)
        {
            EnsureOwner(token);
            if (!IsLegalTransition(Phase, next))
                throw new InvalidOperationException($"Illegal ability transition: {Phase} -> {next}.");
            Phase = next;
            PhaseElapsed = 0f;
        }

        public void Tick(CastToken token, float dt)
        {
            EnsureOwner(token);
            if (dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
            PhaseElapsed += dt;
        }

        public void Cancel(CastToken token, CancelReason reason)
        {
            EnsureOwner(token);
            LastCancelReason = reason;
            Phase = AbilityPhase.Cancelled;
            PhaseElapsed = 0f;
        }

        public void Finish(CastToken token)
        {
            EnsureOwner(token);
            Phase = AbilityPhase.Idle;
            PhaseElapsed = 0f;
            LastCancelReason = CancelReason.None;
            Token = default(CastToken);
        }

        public void ForceReset(CancelReason reason)
        {
            LastCancelReason = reason;
            Phase = AbilityPhase.Idle;
            PhaseElapsed = 0f;
            Token = default(CastToken);
        }

        private void EnsureOwner(CastToken token)
        {
            if (Token == default(CastToken) || token != Token)
                throw new InvalidOperationException("The caller does not own the active cast.");
        }

        private static bool IsLegalTransition(AbilityPhase current, AbilityPhase next)
        {
            if (next == AbilityPhase.Cancelled) return true;
            switch (current)
            {
                case AbilityPhase.Targeting: return next == AbilityPhase.Validating || next == AbilityPhase.WindUp;
                case AbilityPhase.Validating: return next == AbilityPhase.WindUp;
                case AbilityPhase.WindUp: return next == AbilityPhase.Departing || next == AbilityPhase.Active;
                case AbilityPhase.Departing: return next == AbilityPhase.Teleporting;
                case AbilityPhase.Teleporting: return next == AbilityPhase.Arriving;
                case AbilityPhase.Arriving: return next == AbilityPhase.Active;
                case AbilityPhase.Active: return next == AbilityPhase.Recovery;
                case AbilityPhase.Recovery: return next == AbilityPhase.Cooldown || next == AbilityPhase.Idle;
                case AbilityPhase.Cooldown: return next == AbilityPhase.Idle;
                case AbilityPhase.Cancelled: return next == AbilityPhase.Idle;
                default: return false;
            }
        }
    }
}
