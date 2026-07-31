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

            // Blink owns a two-stage targeting/teleport presentation. Applying the generic
            // act_release_stone action after confirmation can rotate the skeleton immediately
            // after teleport and fights TOR proxy cleanup on the same action channel.
            var blinkOwnsItsPresentation = ability == AbilityId.Blink;
            var cleaveOwnsExecutionAction = ability == AbilityId.VoidstepCleave;
            __state = disablingDarkVision || blinkOwnsItsPresentation || cleaveOwnsExecutionAction;
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
