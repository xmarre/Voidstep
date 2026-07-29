using System;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    public sealed class VoidstepSubModule : MBSubModuleBase
    {
        private const string HarmonyId = "xmarre.voidstep";
        private static VoidstepLogger _logger;
        private static Harmony _harmony;

        internal static bool InputSuppressionReady { get; private set; }
        internal static bool NativeHotkeysReady { get; private set; }

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            _logger = new VoidstepLogger();
            _logger.Info("Voidstep v1.0.7 submodule loaded.");
            InputSuppressionReady = false;
            NativeHotkeysReady = false;
            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(typeof(VoidstepSubModule).Assembly);
                InputSuppressionReady = true;
                _logger.Info("Generic conflicting-input suppression patches installed.");
            }
            catch (Exception ex)
            {
                _logger.Error("Voidstep input-conflict patches could not be installed; ability input is disabled.", ex);
                TryCleanFailedInstallation();
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            NativeHotkeysReady = TryInitializeNativeHotkeys(_logger);
        }

        protected override void OnSubModuleUnloaded()
        {
            InputSuppressionReady = false;
            NativeHotkeysReady = false;
            InputConflictSuppression.Reset();
            VoidstepInputBindings.DetachKeybindEvents();
            VoidstepHotKeyContext.Clear();
            if (_harmony != null && !TryUnpatchOwnedPatches())
            {
                _logger?.Error(
                    "Harmony cleanup failed after retry; submodule unload was aborted while Voidstep-owned patches may remain.",
                    new InvalidOperationException("Unable to remove Harmony patches owned by " + HarmonyId + "."));
                return;
            }

            _harmony = null;
            base.OnSubModuleUnloaded();
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            if (mission == null) return;

            var logger = _logger ?? new VoidstepLogger();
            if (!NativeHotkeysReady)
                NativeHotkeysReady = TryInitializeNativeHotkeys(logger);
            if (!InputSuppressionReady || _harmony == null || !NativeHotkeysReady)
            {
                logger.Error(
                    "Mission runtime was not registered because native hotkeys or conflicting-input suppression are unavailable.",
                    new InvalidOperationException("Voidstep input runtime is not ready."));
                return;
            }

            try
            {
                logger.Info("Mission bootstrap received; adding Voidstep mission behavior. Runtime initialization will occur during EarlyStart.");
                mission.AddMissionBehavior(new VoidstepMissionBehavior(logger));
            }
            catch (Exception ex)
            {
                logger.Error("Mission behavior registration failed.", ex);
            }
        }

        private static bool TryInitializeNativeHotkeys(VoidstepLogger logger)
        {
            if (!VoidstepHotKeyContext.TryRegister(logger))
                return false;

            VoidstepInputBindings.AttachKeybindEvents();
            return true;
        }

        private static void TryCleanFailedInstallation()
        {
            InputSuppressionReady = false;
            InputConflictSuppression.Reset();
            VoidstepInputBindings.DetachKeybindEvents();
            VoidstepHotKeyContext.Clear();
            if (_harmony == null)
                return;

            if (TryUnpatchOwnedPatches())
                _harmony = null;
        }

        private static bool TryUnpatchOwnedPatches()
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    _harmony.UnpatchAll(HarmonyId);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Harmony cleanup attempt {attempt} failed.", ex);
                }
            }
            return false;
        }
    }
}
