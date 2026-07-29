using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MCM.Common;
using TaleWorlds.InputSystem;

namespace Voidstep
{
    internal static class MissionOrderInputSuppression
    {
        internal static bool ShouldSuppress(GameKey gameKey)
        {
            if (gameKey == null || !IsControlDown())
                return false;

            var settings = VoidstepSettings.Current;
            if (!settings.Enabled || !settings.RequireControlModifier)
                return false;

            var key = ToNumberRowKey(gameKey.StringId);
            return key != null &&
                   (IsSelected(settings.VoidstepKey, key) ||
                    IsSelected(settings.BlinkKey, key) ||
                    IsSelected(settings.WindblastKey, key) ||
                    IsSelected(settings.BendTimeKey, key) ||
                    IsSelected(settings.DominoKey, key) ||
                    IsSelected(settings.DarkVisionKey, key));
        }

        private static string ToNumberRowKey(string gameKeyId)
        {
            switch (gameKeyId)
            {
                case "SelectOrder1": return "D1";
                case "SelectOrder2": return "D2";
                case "SelectOrder3": return "D3";
                case "SelectOrder4": return "D4";
                case "SelectOrder5": return "D5";
                case "SelectOrder6": return "D6";
                default: return null;
            }
        }

        private static bool IsSelected(Dropdown<string> setting, string value) =>
            setting != null && setting.Count > 0 && setting.SelectedValue == value;

        private static bool IsControlDown() =>
            Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
    }

    [HarmonyPatch]
    internal static class InputContextMissionOrderPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var signatures = new[]
            {
                "IsGameKeyPressed",
                "IsGameKeyDown",
                "IsGameKeyDownImmediate",
                "IsGameKeyReleased"
            };

            for (var i = 0; i < signatures.Length; i++)
            {
                var method = AccessTools.Method(typeof(InputContext), signatures[i], new[] { typeof(GameKey) });
                if (method != null)
                    yield return method;
            }
        }

        private static void Postfix(GameKey __0, ref bool __result)
        {
            if (__result && MissionOrderInputSuppression.ShouldSuppress(__0))
                __result = false;
        }
    }
}
