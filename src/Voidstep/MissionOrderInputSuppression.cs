using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
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
            return settings.Enabled && settings.RequireControlModifier &&
                   settings.ShouldSuppressFormationOrder(gameKey.StringId);
        }

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

        private static void Postfix(object[] __args, ref bool __result)
        {
            if (!__result || __args == null || __args.Length == 0)
                return;

            var gameKey = __args[0] as GameKey;
            if (MissionOrderInputSuppression.ShouldSuppress(gameKey))
                __result = false;
        }
    }
}
