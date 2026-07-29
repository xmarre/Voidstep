using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.InputSystem;

namespace Voidstep
{
    internal static class MissionOrderInputSuppression
    {
        // Bannerlord 1.3.15 CombatHotKeyCategory registers Attack and Defend as GameKey IDs 9 and 10.
        private const int Attack = 9;
        private const int Defend = 10;

        // Bannerlord's MissionOrderHotkeyCategory registers SelectOrder1..6 as GameKey IDs 69..74.
        private const int SelectOrder1 = 69;
        private const int SelectOrder6 = 74;

        internal static bool ShouldSuppress(int gameKeyId)
        {
            if (gameKeyId == Attack || gameKeyId == Defend)
                return VoidstepWheelRuntime.ShouldSuppress(InputKey.RightMouseButton);

            if (!TryGetNumberRowKey(gameKeyId, out var inputKey))
                return false;

            // Input.UpdateKeyData is not guaranteed to pass through the public static wrapper that
            // Voidstep previously patched. Read the live modifier state at the actual native query.
            InputConflictSuppression.CaptureCurrentModifiers();
            return InputConflictSuppression.ShouldSuppress(inputKey);
        }

        private static bool TryGetNumberRowKey(int gameKeyId, out InputKey inputKey)
        {
            if (gameKeyId < SelectOrder1 || gameKeyId > SelectOrder6)
            {
                inputKey = InputKey.Invalid;
                return false;
            }

            inputKey = (InputKey)((int)InputKey.D1 + gameKeyId - SelectOrder1);
            return true;
        }
    }

    [HarmonyPatch]
    internal static class InputContextMissionOrderBooleanSuppressionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new[]
            {
                nameof(InputContext.IsGameKeyPressed),
                nameof(InputContext.IsGameKeyDown),
                nameof(InputContext.IsGameKeyDownImmediate),
                nameof(InputContext.IsGameKeyReleased)
            };

            for (var i = 0; i < names.Length; i++)
            {
                var method = AccessTools.Method(typeof(InputContext), names[i], new[] { typeof(int) });
                if (method != null)
                    yield return method;
            }
        }

        private static void Postfix(int __0, ref bool __result)
        {
            if (__result && MissionOrderInputSuppression.ShouldSuppress(__0))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(InputContext), nameof(InputContext.GetGameKeyState), typeof(int))]
    internal static class InputContextMissionOrderStateSuppressionPatch
    {
        private static void Postfix(int __0, ref float __result)
        {
            if (__result != 0f && MissionOrderInputSuppression.ShouldSuppress(__0))
                __result = 0f;
        }
    }

    [HarmonyPatch]
    internal static class RawInputModifierRefreshPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var booleanMethods = new[]
            {
                nameof(Input.IsKeyPressed),
                nameof(Input.IsKeyDown),
                nameof(Input.IsKeyDownImmediate),
                nameof(Input.IsKeyReleased)
            };

            for (var i = 0; i < booleanMethods.Length; i++)
            {
                var method = AccessTools.Method(typeof(Input), booleanMethods[i], new[] { typeof(InputKey) });
                if (method != null)
                    yield return method;
            }

            var stateMethod = AccessTools.Method(typeof(Input), nameof(Input.GetKeyState), new[] { typeof(InputKey) });
            if (stateMethod != null)
                yield return stateMethod;
        }

        private static void Prefix(InputKey __0)
        {
            if (InputConflictSuppression.IsBypassed || !VoidstepInputBindings.IsBoundPrimaryKey(__0))
                return;

            InputConflictSuppression.CaptureCurrentModifiers();
        }
    }
}
