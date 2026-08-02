using System;
using System.Collections.Generic;

namespace Voidstep.Core
{
    public readonly struct MasteryRequirementSpec
    {
        public MasteryRequirementSpec(int skillId, int level)
        {
            if (skillId < 0) throw new ArgumentOutOfRangeException(nameof(skillId));
            if (level <= 0) throw new ArgumentOutOfRangeException(nameof(level));
            SkillId = skillId;
            Level = level;
        }

        public int SkillId { get; }
        public int Level { get; }
    }

    public static class MasteryGraphPolicy
    {
        public const int SkillCount = 19;

        public const int VoidAffinity = 0;
        public const int RiftStep = 1;
        public const int PhaseRecovery = 2;
        public const int MomentumWeave = 3;
        public const int VoidDancer = 4;
        public const int GaleForce = 5;
        public const int CrushingWave = 6;
        public const int BendTheHour = 7;
        public const int Chronomancer = 8;
        public const int FatefulLink = 9;
        public const int SharedAgony = 10;
        public const int UmbralSight = 11;
        public const int SovereignGaze = 12;
        public const int DeepReservoir = 13;
        public const int EfficientChanneling = 14;
        public const int RapidRecovery = 15;
        public const int UnboundPower = 16;
        public const int Singularity = 17;
        public const int AvatarOfTheVoid = 18;

        private static readonly MasteryRequirementSpec[] Empty = Array.Empty<MasteryRequirementSpec>();
        private static readonly MasteryRequirementSpec[] PhaseRecoveryRequirements = { R(RiftStep, 5) };
        private static readonly MasteryRequirementSpec[] MomentumWeaveRequirements = { R(PhaseRecovery, 5) };
        private static readonly MasteryRequirementSpec[] VoidDancerRequirements = { R(MomentumWeave, 5) };
        private static readonly MasteryRequirementSpec[] CrushingWaveRequirements = { R(GaleForce, 5) };
        private static readonly MasteryRequirementSpec[] ChronomancerRequirements = { R(BendTheHour, 5) };
        private static readonly MasteryRequirementSpec[] SharedAgonyRequirements = { R(FatefulLink, 5) };
        private static readonly MasteryRequirementSpec[] SovereignGazeRequirements = { R(UmbralSight, 5) };
        private static readonly MasteryRequirementSpec[] EfficientChannelingRequirements = { R(DeepReservoir, 5) };
        private static readonly MasteryRequirementSpec[] RapidRecoveryRequirements = { R(EfficientChanneling, 5) };
        private static readonly MasteryRequirementSpec[] UnboundPowerRequirements = { R(RapidRecovery, 5) };
        private static readonly MasteryRequirementSpec[] SingularityRequirements =
        {
            R(VoidAffinity, 1),
            R(RiftStep, 1),
            R(GaleForce, 1),
            R(BendTheHour, 1),
            R(FatefulLink, 1),
            R(UmbralSight, 1)
        };
        private static readonly MasteryRequirementSpec[] AvatarRequirements =
        {
            R(Singularity, 5),
            R(UnboundPower, 5)
        };

        public static IReadOnlyList<MasteryRequirementSpec> GetRequirements(int skillId)
        {
            switch (skillId)
            {
                case VoidAffinity:
                case RiftStep:
                case GaleForce:
                case BendTheHour:
                case FatefulLink:
                case UmbralSight:
                case DeepReservoir:
                    return Empty;
                case PhaseRecovery:
                    return PhaseRecoveryRequirements;
                case MomentumWeave:
                    return MomentumWeaveRequirements;
                case VoidDancer:
                    return VoidDancerRequirements;
                case CrushingWave:
                    return CrushingWaveRequirements;
                case Chronomancer:
                    return ChronomancerRequirements;
                case SharedAgony:
                    return SharedAgonyRequirements;
                case SovereignGaze:
                    return SovereignGazeRequirements;
                case EfficientChanneling:
                    return EfficientChannelingRequirements;
                case RapidRecovery:
                    return RapidRecoveryRequirements;
                case UnboundPower:
                    return UnboundPowerRequirements;
                case Singularity:
                    return SingularityRequirements;
                case AvatarOfTheVoid:
                    return AvatarRequirements;
                default:
                    throw new ArgumentOutOfRangeException(nameof(skillId));
            }
        }

        private static MasteryRequirementSpec R(int skillId, int level) =>
            new MasteryRequirementSpec(skillId, level);
    }
}
