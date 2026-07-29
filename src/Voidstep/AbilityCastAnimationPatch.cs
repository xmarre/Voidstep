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
            __state = ability == AbilityId.DarkVision && __instance.IsDarkVisionActive;
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
