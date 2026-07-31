using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Voidstep
{
    internal static class BendTimeMainAgentControllerInstaller
    {
        private const string HarmonyId = "xmarre.voidstep.bendtime.mainagent.controller";
        private const string ControllerTypeName =
            "TaleWorlds.MountAndBlade.View.MissionViews.MissionMainAgentController";
        private static bool _attempted;

        internal static void EnsureInstalled(VoidstepLogger logger)
        {
            if (_attempted)
                return;

            var type = AccessTools.TypeByName(ControllerTypeName);
            var target = type == null
                ? null
                : AccessTools.Method(type, "OnPreMissionTick", new[] { typeof(float) });
            if (target == null)
            {
                logger?.Debug("Bend Time main-agent controller delta exemption is not yet available.");
                return;
            }

            _attempted = true;
            try
            {
                var patchInfo = Harmony.GetPatchInfo(target);
                if (patchInfo != null && patchInfo.Prefixes.Any(prefix =>
                        prefix.PatchMethod?.DeclaringType == typeof(MissionMainAgentControllerDeltaPatch) ||
                        prefix.PatchMethod?.DeclaringType == typeof(BendTimeMainAgentControllerInstaller)))
                {
                    logger?.Debug("Bend Time main-agent controller delta exemption already installed.");
                    return;
                }

                var prefix = AccessTools.Method(
                    typeof(BendTimeMainAgentControllerInstaller),
                    nameof(ScalePrefix));
                new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(prefix));
                logger?.Debug("Installed Bend Time main-agent OnPreMissionTick delta exemption.");
            }
            catch (Exception ex)
            {
                _attempted = false;
                logger?.Debug("Bend Time main-agent controller patch failed safely: " + ex.Message);
            }
        }

        private static void ScalePrefix(ref float dt) =>
            BendTimeMainAgentTickRuntime.Scale(ref dt);
    }

    [HarmonyPatch(typeof(VoidstepMissionBehavior), "EnsureInitialized")]
    internal static class BendTimeMainAgentControllerMissionInstallPatch
    {
        private static void Postfix(VoidstepLogger ____logger) =>
            BendTimeMainAgentControllerInstaller.EnsureInstalled(____logger);
    }
}
