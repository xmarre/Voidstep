using System;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    internal sealed class BlowFactory
    {
        private delegate Blow CreateMeleeBlowDelegate(
            Mission mission,
            Agent attackerAgent,
            Agent victimAgent,
            in AttackCollisionData collisionData,
            in MissionWeapon attackerWeapon,
            CrushThroughState crushThroughState,
            Vec3 blowDirection,
            Vec3 swingDirection,
            bool cancelDamage);

        private static readonly CreateMeleeBlowDelegate CreateNativeMeleeBlow = ResolveCreateMeleeBlow();

        private readonly Mission _mission;
        private readonly VoidstepLogger _logger;

        public BlowFactory(Mission mission, VoidstepLogger logger)
        {
            _mission = mission;
            _logger = logger;
        }

        public bool ApplyMeleeBlow(
            Agent attacker,
            Agent victim,
            MissionWeapon weapon,
            float damageMultiplier,
            float knockback,
            float knockdownThreshold,
            float attackProgress,
            bool propagated = false)
        {
            if (!IsValid(attacker, victim) || weapon.IsEmpty)
                return false;

            try
            {
                var direction = victim.GetChestGlobalPosition() - attacker.GetChestGlobalPosition();
                direction.z *= 0.25f;
                if (direction.Normalize() < 0.001f)
                    direction = attacker.LookDirection;
                var swing = direction.CrossProductWithUp();
                if (swing.Normalize() < 0.001f)
                    swing = Vec3.Side;

                var impact = victim.GetChestGlobalPosition();
                var collision = CreateCollision(direction, swing, impact, attackProgress);
                if (CreateNativeMeleeBlow == null)
                {
                    _logger.Error(
                        "Bannerlord's native melee-blow factory could not be resolved.",
                        new MissingMethodException("Mission.CreateMeleeBlow"));
                    return false;
                }

                var blow = CreateNativeMeleeBlow(
                    _mission,
                    attacker,
                    victim,
                    in collision,
                    in weapon,
                    CrushThroughState.None,
                    direction,
                    swing,
                    false);

                var nativeDamage = blow.InflictedDamage;
                if (nativeDamage <= 0)
                    nativeDamage = ResolveMinimumWeaponDamage(weapon);
                blow.InflictedDamage = Math.Max(1, (int)Math.Round(nativeDamage * Math.Max(0.05f, damageMultiplier)));
                blow.BaseMagnitude = Math.Max(blow.BaseMagnitude * Math.Max(0.05f, damageMultiplier), blow.InflictedDamage);
                blow.DamageCalculated = true;

                if (knockback > 0f)
                {
                    blow.BaseMagnitude += knockback;
                    blow.BlowFlag |= BlowFlags.KnockBack;
                }
                if (blow.InflictedDamage >= knockdownThreshold && knockdownThreshold > 0f)
                    blow.BlowFlag |= BlowFlags.KnockDown;
                if (propagated)
                    blow.BlowFlag |= BlowFlags.NoSound;

                victim.RegisterBlow(blow, in collision);
                _logger.Debug($"Registered cleave blow attacker={attacker.Index}, victim={victim.Index}, damage={blow.InflictedDamage}, magnitude={blow.BaseMagnitude:0.00}.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to register a melee blow safely.", ex);
                return false;
            }
        }

        public bool ApplyDirectBlow(Agent attacker, Agent victim, int damage, DamageTypes damageType, BlowFlags flags, float magnitude = -1f)
        {
            if (!IsValid(attacker, victim)) return false;
            try
            {
                var direction = victim.Position - attacker.Position;
                direction.z = 0.15f;
                if (direction.Normalize() < 0.001f)
                    direction = attacker.LookDirection;
                var blow = new Blow(attacker.Index)
                {
                    GlobalPosition = victim.GetChestGlobalPosition(),
                    Direction = direction,
                    SwingDirection = direction.CrossProductWithUp(),
                    InflictedDamage = Math.Max(0, damage),
                    BaseMagnitude = magnitude > 0f ? magnitude : Math.Max(1f, damage),
                    StrikeType = StrikeType.Swing,
                    AttackType = AgentAttackType.Standard,
                    BlowFlag = flags,
                    BoneIndex = 0,
                    VictimBodyPart = BoneBodyPartType.Chest,
                    DamageType = damageType,
                    DamageCalculated = true
                };
                var collision = CreateCollision(direction, blow.SwingDirection, blow.GlobalPosition, 1f);
                victim.RegisterBlow(blow, in collision);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to register direct ability damage safely.", ex);
                return false;
            }
        }

        private static int ResolveMinimumWeaponDamage(MissionWeapon weapon)
        {
            try
            {
                var damage = weapon.GetModifiedSwingDamageForCurrentUsage();
                if (damage > 0) return damage;
            }
            catch
            {
            }
            return 25;
        }

        private static CreateMeleeBlowDelegate ResolveCreateMeleeBlow()
        {
            try
            {
                var method = typeof(Mission).GetMethod(
                    "CreateMeleeBlow",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[]
                    {
                        typeof(Agent),
                        typeof(Agent),
                        typeof(AttackCollisionData).MakeByRefType(),
                        typeof(MissionWeapon).MakeByRefType(),
                        typeof(CrushThroughState),
                        typeof(Vec3),
                        typeof(Vec3),
                        typeof(bool)
                    },
                    null);
                return method == null
                    ? null
                    : (CreateMeleeBlowDelegate)method.CreateDelegate(typeof(CreateMeleeBlowDelegate));
            }
            catch
            {
                return null;
            }
        }

        private static AttackCollisionData CreateCollision(Vec3 direction, Vec3 swing, Vec3 impact, float progress)
        {
            return AttackCollisionData.GetAttackCollisionDataForDebugPurpose(
                _attackBlockedWithShield: false,
                _correctSideShieldBlock: false,
                _isAlternativeAttack: false,
                _isColliderAgent: true,
                _collidedWithShieldOnBack: false,
                _isMissile: false,
                _isMissileBlockedWithWeapon: false,
                _missileHasPhysics: false,
                _entityExists: true,
                _thrustTipHit: false,
                _missileGoneUnderWater: false,
                _missileGoneOutOfBorder: false,
                collisionResult: CombatCollisionResult.StrikeAgent,
                affectorWeaponSlotOrMissileIndex: -1,
                StrikeType: (int)StrikeType.Swing,
                DamageType: (int)DamageTypes.Cut,
                CollisionBoneIndex: 0,
                VictimHitBodyPart: BoneBodyPartType.Chest,
                AttackBoneIndex: -1,
                AttackDirection: Agent.UsageDirection.AttackLeft,
                PhysicsMaterialIndex: 0,
                CollisionHitResultFlags: CombatHitResultFlags.NormalHit,
                AttackProgress: progress,
                CollisionDistanceOnWeapon: 1f,
                AttackerStunPeriod: 0f,
                DefenderStunPeriod: 0.2f,
                MissileTotalDamage: 0f,
                MissileInitialSpeed: 0f,
                ChargeVelocity: 0f,
                FallSpeed: 0f,
                WeaponRotUp: swing,
                _weaponBlowDir: direction,
                CollisionGlobalPosition: impact,
                MissileVelocity: Vec3.Zero,
                MissileStartingPosition: Vec3.Zero,
                VictimAgentCurVelocity: Vec3.Zero,
                GroundNormal: Vec3.Up);
        }

        private static bool IsValid(Agent attacker, Agent victim) =>
            attacker != null && victim != null && attacker != victim && attacker.IsActive() && victim.IsActive() && victim.Health > 0f;
    }
}
