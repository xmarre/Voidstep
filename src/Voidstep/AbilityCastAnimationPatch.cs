using HarmonyLib;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    [HarmonyPatch(typeof(AbilityManager), nameof(AbilityManager.TryActivate))]
    internal static class AbilityCastAnimationPatch
    {
        private static void Postfix(AbilityId ability, bool __result)
        {
            if (!__result) return;
            var actor = Agent.Main;
            if (actor == null || !actor.IsActive()) return;
            AnimationController.PlayAbilityCast(actor, ability, null);
        }
    }
}
