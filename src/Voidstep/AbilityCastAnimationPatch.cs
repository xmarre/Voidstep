using HarmonyLib;
using Voidstep.Core;

namespace Voidstep
{
    [HarmonyPatch(typeof(AbilityManager), nameof(AbilityManager.TryActivate))]
    internal static class AbilityCastAnimationPatch
    {
        private static void Prefix(
            AbilityManager __instance,
            AbilityId ability,
            out bool __state)
        {
            var disablingDarkVision =
                ability == AbilityId.DarkVision && __instance.IsDarkVisionActive;
            var blinkOwnsItsPresentation = ability == AbilityId.Blink;
            var cleaveOwnsExecutionAction = ability == AbilityId.VoidstepCleave;
            __state = disablingDarkVision ||
                      blinkOwnsItsPresentation ||
                      cleaveOwnsExecutionAction;
        }

        private static void Postfix(
            AbilityManager __instance,
            AbilityId ability,
            bool __result,
            bool __state,
            AbilityContext ____context)
        {
            if (!__result || __state)
                return;

            var actor = ____context?.Player;
            if (actor == null || !actor.IsActive())
                return;

            AnimationController.PlayAbilityCast(
                actor,
                ability,
                __instance.Logger);
        }
    }
}
