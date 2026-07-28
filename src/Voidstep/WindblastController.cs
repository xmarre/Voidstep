using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class WindblastController
    {
        private readonly Mission _mission;
        private readonly BlowFactory _blows;
        private readonly EffectController _effects;
        private readonly VoidstepLogger _logger;
        private readonly MBList<Agent> _nearby = new MBList<Agent>();
        private readonly HitRegistry<int> _hits = new HitRegistry<int>();

        public WindblastController(Mission mission, BlowFactory blows, EffectController effects, VoidstepLogger logger)
        {
            _mission = mission;
            _blows = blows;
            _effects = effects;
            _logger = logger;
        }

        public int Cast(Agent player)
        {
            _hits.Clear();
            if (player == null || !player.IsActive() || player.Team == null)
                return 0;

            var settings = VoidstepSettings.Current;
            _nearby.Clear();
            _mission.GetNearbyEnemyAgents(player.Position.AsVec2, settings.WindblastRange, player.Team, _nearby);
            var forward = player.LookDirection;
            forward.z = 0f;
            if (forward.Normalize() < 0.001f) forward = Vec3.Forward;
            var minDot = (float)Math.Cos(settings.WindblastAngle * 0.5f * Math.PI / 180.0);
            var count = 0;

            try
            {
                for (var i = 0; i < _nearby.Count; i++)
                {
                    var target = _nearby[i];
                    if (target == null || target == player || !target.IsActive() || target.Health <= 0f)
                        continue;
                    if (!settings.WindblastMounts && target.IsMount)
                        continue;
                    if ((!target.IsHuman && !target.IsMount) || !_hits.TryRegister(target.Index))
                        continue;

                    var delta = target.Position - player.Position;
                    delta.z = 0f;
                    var distance = delta.Normalize();
                    if (distance <= 0.001f || distance > settings.WindblastRange)
                        continue;
                    var centre = Vec3.DotProduct(forward, delta);
                    if (centre < minDot)
                        continue;

                    var distanceFactor = 1f - Math.Min(1f, distance / settings.WindblastRange);
                    var angularFactor = minDot >= 0.999f ? 1f : Math.Max(0f, (centre - minDot) / (1f - minDot));
                    var forceFactor = 0.25f + 0.75f * distanceFactor * angularFactor;
                    var force = settings.WindblastForce * forceFactor;
                    var damage = (int)Math.Round(settings.WindblastDamage * (0.35f + 0.65f * distanceFactor));
                    var flags = BlowFlags.KnockBack;
                    if (forceFactor >= 0.6f)
                        flags |= BlowFlags.KnockDown;
                    if (_blows.ApplyDirectBlow(player, target, damage, DamageTypes.Blunt, flags, force))
                        count++;
                }

                if (count > 0)
                    _effects.Windblast(player.GetChestGlobalPosition() + forward * 1.2f);
                if (settings.WindblastProjectiles)
                    _logger.Debug("Projectile deflection was requested but remains disabled because Bannerlord 1.3.15 exposes no safe public missile-velocity mutation path.");
            }
            finally
            {
                _nearby.Clear();
                _hits.Clear();
            }
            return count;
        }
    }
}
