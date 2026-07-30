using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    [HarmonyPatch(typeof(CleaveSweepController), nameof(CleaveSweepController.Begin))]
    internal static class VoidstepCleaveEffectPatch
    {
        private static void Postfix(Agent actor, bool __result, EffectController ____effects)
        {
            if (!__result || actor == null) return;
            try
            {
                VoidstepAbilityEffects.VoidCleave(
                    ____effects,
                    actor.Position,
                    VoidstepSettings.Current.CleaveRadius);
            }
            catch
            {
                // Visual enhancement must never interrupt the ability.
            }
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "ConfirmBlink")]
    internal static class BlinkSpellEffectPatch
    {
        private struct BlinkOriginState
        {
            internal bool HasValue;
            internal Vec3 Position;
        }

        private static void Prefix(Agent player, out BlinkOriginState __state)
        {
            __state = new BlinkOriginState
            {
                HasValue = player != null,
                Position = player != null ? player.Position : Vec3.Zero
            };
        }

        private static void Postfix(Agent player, BlinkOriginState __state, bool __result, EffectController ____effects)
        {
            if (!__result || player == null || !__state.HasValue) return;
            try { VoidstepAbilityEffects.Blink(____effects, __state.Position, player.Position); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "CastBendTime")]
    internal static class BendTimeSpellEffectPatch
    {
        private static void Postfix(Agent player, bool __result, EffectController ____effects)
        {
            if (!__result || player == null) return;
            try { VoidstepAbilityEffects.BendTime(____effects, player.Position); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "CastDomino")]
    internal static class DominoSpellEffectPatch
    {
        private static void Postfix(
            Agent player,
            bool __result,
            EffectController ____effects,
            DominoLinkService ____domino)
        {
            if (!__result || player == null) return;
            try { VoidstepAbilityEffects.Domino(____effects, player.Position, ____domino != null ? ____domino.Count : 0); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "CastDarkVision")]
    internal static class DarkVisionSpellEffectPatch
    {
        private static void Postfix(Agent player, bool __result, EffectController ____effects)
        {
            if (!__result || player == null) return;
            try
            {
                VoidstepAbilityEffects.DarkVision(
                    ____effects,
                    player.Position,
                    VoidstepSettings.Current.DarkVisionRange);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(WindblastController), nameof(WindblastController.Cast))]
    internal static class WindblastSpellEffectPatch
    {
        private static void Postfix(
            Agent player,
            int __result,
            EffectController ____effects,
            TargetingService ____targeting)
        {
            if (__result <= 0 || player == null || ____targeting == null) return;
            try
            {
                var settings = VoidstepSettings.Current;
                VoidstepAbilityEffects.Windblast(
                    ____effects,
                    player.GetChestGlobalPosition(),
                    ____targeting.GetAimDirection(player),
                    settings.WindblastRange,
                    settings.WindblastAngle);
            }
            catch { }
        }
    }
}
