using HarmonyLib;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    [HarmonyPatch(typeof(AbilityManager), nameof(AbilityManager.TryActivate))]
    internal static class AbilityCastAnimationPatch
    {
        private static void Prefix(AbilityManager __instance, AbilityId ability, out bool __state)
        {
            var disablingDarkVision = ability == AbilityId.DarkVision && __instance.IsDarkVisionActive;
            var confirmingBlink = ability == AbilityId.Blink && __instance.IsBusy &&
                                  __instance.ActiveAbility == AbilityId.Blink &&
                                  __instance.Phase == AbilityPhase.Targeting;
            var enteringBlinkTargeting = ability == AbilityId.Blink && !confirmingBlink;
            __state = disablingDarkVision || enteringBlinkTargeting;
        }

        private static void Postfix(AbilityManager __instance, AbilityId ability, bool __result, bool __state)
        {
            if (!__result || __state) return;
            var actor = Agent.Main;
            if (actor == null || !actor.IsActive()) return;
            AnimationController.PlayAbilityCast(actor, ability, __instance.Logger);
        }
    }
}
