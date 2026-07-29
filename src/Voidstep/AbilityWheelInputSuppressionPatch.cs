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
            var names = new[]
            {
                nameof(Input.IsKeyPressed),
                nameof(Input.IsKeyDown),
                nameof(Input.IsKeyDownImmediate),
                nameof(Input.IsKeyReleased)
            };
            for (var i = 0; i < names.Length; i++)
            {
                var method = AccessTools.Method(typeof(Input), names[i], new[] { typeof(InputKey) });
                if (method != null)
                    yield return method;
            }
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
