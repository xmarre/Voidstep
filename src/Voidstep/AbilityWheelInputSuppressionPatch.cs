using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.InputSystem;

namespace Voidstep
{
    [HarmonyPatch]
    internal static class AbilityWheelInputSuppressionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Input), nameof(Input.IsKeyPressed), new[] { typeof(InputKey) });
            yield return AccessTools.Method(typeof(Input), nameof(Input.IsKeyDown), new[] { typeof(InputKey) });
            yield return AccessTools.Method(typeof(Input), nameof(Input.IsKeyDownImmediate), new[] { typeof(InputKey) });
            yield return AccessTools.Method(typeof(Input), nameof(Input.IsKeyReleased), new[] { typeof(InputKey) });
        }

        private static void Postfix(InputKey __0, ref bool __result)
        {
            if (!__result || InputConflictSuppression.IsBypassed)
                return;
            if (VoidstepWheelRuntime.ShouldSuppress(__0))
                __result = false;
        }
    }
}
