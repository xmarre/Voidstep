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
            float damageMultiplier,
            float knockback,
            float knockdownThreshold,
            float attackProgress,
            bool propagated = false)
        {
            if (!IsValid(attacker, victim))
                return false;

            try
            {
                var weapon = attacker.WieldedWeapon;
                if (weapon.IsEmpty)
                    weapon = default(MissionWeapon);

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
                    _logger.Error("Bannerlord's native melee-blow factory could not be resolved.");
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

                if (damageMultiplier != 1f)
                {
                    blow.InflictedDamage = Math.Max(0, (int)Math.Round(blow.InflictedDamage * damageMultiplier));
                    blow.BaseMagnitude *= damageMultiplier;
                }

                if (knockback > 0f)
                {
                    blow.BaseMagnitude += knockback;
                    blow.BlowFlag |= BlowFlags.KnockBack;
                }
                if (blow.InflictedDamage >= knockdownThreshold && knockdownThreshold > 0f)
                    blow.BlowFlag |= BlowFlags.KnockDown;
                if (propagated)
                    blow.BlowFlag |= BlowFlags.NoSound;

                victim.RegisterBlow(blow, ref collision);
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
                victim.RegisterBlow(blow, ref collision);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to register direct ability damage safely.", ex);
                return false;
            }
        }


        private static CreateMeleeBlowDelegate ResolveCreateMeleeBlow()
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

        private static AttackCollisionData CreateCollision(Vec3 direction, Vec3 swing, Vec3 impact, float progress)
        {
            return AttackCollisionData.GetAttackCollisionDataForDebugPurpose(
                false,
                false,
                false,
                true,
                false,
                false,
                false,
                false,
                true,
                false,
                false,
                false,
                CombatCollisionResult.StrikeAgent,
                -1,
                (int)StrikeType.Swing,
                (int)DamageTypes.Cut,
                0,
                BoneBodyPartType.Chest,
                -1,
                Agent.UsageDirection.AttackLeft,
                0,
                CombatHitResultFlags.NormalHit,
                progress,
                1f,
                0f,
                0.2f,
                0f,
                0f,
                0f,
                0f,
                Vec3.Up,
                direction,
                impact,
                Vec3.Zero,
                Vec3.Zero,
                Vec3.Zero,
                Vec3.Up);
        }

        private static bool IsValid(Agent attacker, Agent victim) =>
            attacker != null && victim != null && attacker != victim && attacker.IsActive() && victim.IsActive() && victim.Health > 0f;
    }
}
