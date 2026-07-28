using System;

namespace Voidstep.Core
{
    public enum AbilityId
    {
        VoidstepCleave,
        Blink,
        Windblast,
        BendTime,
        Domino,
        DarkVision
    }

    public enum AbilityPhase
    {
        Idle,
        Targeting,
        Validating,
        WindUp,
        Departing,
        Teleporting,
        Arriving,
        Active,
        Recovery,
        Cooldown,
        Cancelled
    }

    public enum SweepDirection
    {
        Clockwise = -1,
        CounterClockwise = 1
    }

    public enum CancelReason
    {
        None,
        UserCancelled,
        InvalidActor,
        ActorDied,
        ActorRemoved,
        ActorReplaced,
        Interrupted,
        InvalidDestination,
        MissionEnded,
        Exception
    }

    public readonly struct CastToken : IEquatable<CastToken>
    {
        public CastToken(long value) => Value = value;
        public long Value { get; }
        public bool Equals(CastToken other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CastToken other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(CastToken left, CastToken right) => left.Equals(right);
        public static bool operator !=(CastToken left, CastToken right) => !left.Equals(right);
        public override string ToString() => Value.ToString();
    }
}
