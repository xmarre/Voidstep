using HarmonyLib;

namespace Voidstep
{
    /// <summary>
    /// TOR is retained strictly as a selection UI boundary. Runtime testing showed that global
    /// Agent.Main/action-channel cleanup can escape the live mission and contaminate presentation
    /// agents. Voidstep therefore performs no TOR-side Agent action, frame or orientation mutation.
    /// </summary>
    internal static class TorProxyCastStanceFix
    {
        private static bool _installed;
        private static readonly VoidstepLogger Logger = new VoidstepLogger();

        internal static void Install()
        {
            if (_installed)
                return;
            _installed = true;
            Logger.Info("TOR Voidstep proxy integration is selection-only; global Agent presentation cleanup is disabled.");
        }

        internal static void ReleaseBeforeVoidstepActivation()
        {
            // Deliberately empty. Do not access Agent.Main or mutate any action channel.
        }
    }

    [HarmonyPatch(typeof(AbilitySelectionController), nameof(AbilitySelectionController.Confirm))]
    internal static class TorProxyReleaseBeforeConfirmPatch
    {
        private static void Prefix()
        {
            TorProxyCastStanceFix.ReleaseBeforeVoidstepActivation();
        }
    }

    [HarmonyPatch(typeof(VoidstepMissionBehavior), nameof(VoidstepMissionBehavior.EarlyStart))]
    internal static class TorProxyCastStanceFixInstallerPatch
    {
        private static void Postfix()
        {
            TorProxyCastStanceFix.Install();
        }
    }
}
