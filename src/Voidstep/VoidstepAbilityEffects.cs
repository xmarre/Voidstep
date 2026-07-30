using System;
using TaleWorlds.Library;

namespace Voidstep
{
    /// <summary>
    /// Bounded, cast-time-only particle compositions built from EffectController's
    /// already validated native/TOR particle fallbacks. No entity or agent state is retained.
    /// </summary>
    internal static class VoidstepAbilityEffects
    {
        internal static void VoidCleave(EffectController effects, Vec3 center, float cleaveRadius)
        {
            if (!CanPlay(effects)) return;

            var outerRadius = Clamp(Math.Max(3.2f, cleaveRadius * 1.15f), 3.2f, 7.5f);
            effects.Departure(center + Vec3.Up * 0.35f);
            effects.Impact(center + Vec3.Up * 0.85f);
            BurstRing(effects, center, outerRadius, 20, EffectKind.Impact, 0.20f);
            BurstRing(effects, center, outerRadius * 0.58f, 14, EffectKind.Arrival, 0.55f);

            // A raised crown makes the effect read as a large spell detonation rather than
            // only a flat ground ring.
            BurstRing(effects, center, outerRadius * 0.34f, 10, EffectKind.WeaponTrail, 1.35f);
        }

        internal static void Blink(EffectController effects, Vec3 departure, Vec3 arrival)
        {
            if (!CanPlay(effects)) return;

            VoidPulse(effects, departure, 1.45f, 8);
            VoidPulse(effects, arrival, 2.05f, 12);
            effects.Arrival(arrival + Vec3.Up * 1.0f);
        }

        internal static void BendTime(EffectController effects, Vec3 center)
        {
            if (!CanPlay(effects)) return;

            VoidPulse(effects, center, 3.4f, 16);
            BurstRing(effects, center, 1.75f, 10, EffectKind.Departure, 1.25f);
        }

        internal static void Domino(EffectController effects, Vec3 center, int linkedTargets)
        {
            if (!CanPlay(effects)) return;

            var radius = Clamp(1.8f + Math.Max(0, linkedTargets) * 0.28f, 2.2f, 4.8f);
            VoidPulse(effects, center, radius, 10 + Math.Min(8, Math.Max(0, linkedTargets)));
            effects.Impact(center + Vec3.Up * 1.15f);
        }

        internal static void DarkVision(EffectController effects, Vec3 center, float visionRange)
        {
            if (!CanPlay(effects)) return;

            var radius = Clamp(visionRange * 0.13f, 2.5f, 5.5f);
            VoidPulse(effects, center, radius, 16);
            BurstRing(effects, center, radius * 0.48f, 10, EffectKind.Departure, 1.1f);
        }

        internal static void Windblast(EffectController effects, Vec3 origin, Vec3 forward, float range, float angleDegrees)
        {
            if (!CanPlay(effects)) return;

            forward.z = 0f;
            if (forward.Normalize() < 0.001f) forward = Vec3.Forward;
            var right = new Vec3(-forward.y, forward.x, 0f, 0f);
            var effectiveRange = Clamp(range, 3f, 18f);
            var halfAngle = Clamp(angleDegrees * 0.5f, 8f, 70f) * (float)Math.PI / 180f;

            // Five bounded gust anchors form a visible widening cone without any persistent
            // particle ownership or per-frame work.
            EmitWindAnchor(effects, origin + forward * (effectiveRange * 0.18f), 0.15f);
            EmitWindAnchor(effects, origin + forward * (effectiveRange * 0.43f), 0.35f);
            EmitWindAnchor(effects, origin + forward * (effectiveRange * 0.72f), 0.45f);

            var farDistance = effectiveRange * 0.68f;
            var farHalfWidth = (float)Math.Tan(halfAngle) * farDistance * 0.58f;
            EmitWindAnchor(effects, origin + forward * farDistance + right * farHalfWidth, 0.30f);
            EmitWindAnchor(effects, origin + forward * farDistance - right * farHalfWidth, 0.30f);
        }

        private static void VoidPulse(EffectController effects, Vec3 center, float radius, int count)
        {
            effects.Departure(center + Vec3.Up * 0.35f);
            effects.Arrival(center + Vec3.Up * 0.75f);
            BurstRing(effects, center, radius, count, EffectKind.Impact, 0.20f);
        }

        private static void EmitWindAnchor(EffectController effects, Vec3 position, float height)
        {
            effects.Windblast(position + Vec3.Up * height);
        }

        private static void BurstRing(EffectController effects, Vec3 center, float radius, int count, EffectKind kind, float height)
        {
            count = Math.Max(1, Math.Min(24, count));
            radius = Math.Max(0.1f, radius);
            for (var i = 0; i < count; i++)
            {
                var angle = i * Math.PI * 2.0 / count;
                var position = new Vec3(
                    center.x + (float)Math.Cos(angle) * radius,
                    center.y + (float)Math.Sin(angle) * radius,
                    center.z + height,
                    1f);
                Emit(effects, position, kind);
            }
        }

        private static void Emit(EffectController effects, Vec3 position, EffectKind kind)
        {
            try
            {
                switch (kind)
                {
                    case EffectKind.Departure:
                        effects.Departure(position);
                        break;
                    case EffectKind.Arrival:
                        effects.Arrival(position);
                        break;
                    case EffectKind.WeaponTrail:
                        effects.WeaponTrail(position);
                        break;
                    default:
                        effects.Impact(position);
                        break;
                }
            }
            catch
            {
                // Visual enhancement must never affect ability execution.
            }
        }

        private static bool CanPlay(EffectController effects)
        {
            return effects != null && VoidstepSettings.Current.EffectIntensity > 0f;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private enum EffectKind
        {
            Impact,
            Departure,
            Arrival,
            WeaponTrail
        }
    }
}
