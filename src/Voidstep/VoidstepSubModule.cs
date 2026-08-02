using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace Voidstep
{
    public sealed class VoidstepSubModule : MBSubModuleBase
    {
        private enum CharacterScreenOpenPhase
        {
            None,
            CloseCharacterState,
            WaitForCampaignMap,
            SettleCampaignMap
        }

        private const string HarmonyId = "xmarre.voidstep";
        private static VoidstepLogger _logger;
        private static Harmony _harmony;

        private readonly VoidstepCharacterScreenButtonController _characterButton;
        private bool _masteryOpenLatch;
        private bool _pendingCharacterScreenOpen;
        private int _pendingCharacterScreenOpenFrames;
        private int _pendingCharacterScreenOpenTimeoutFrames;
        private CharacterScreenOpenPhase _characterScreenOpenPhase;

        internal static bool InputSuppressionReady { get; private set; }
        internal static bool NativeHotkeysReady { get; private set; }

        public VoidstepSubModule()
        {
            _characterButton = new VoidstepCharacterScreenButtonController(RequestOpenFromCharacterScreen);
        }

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            _logger = new VoidstepLogger();
            _logger.Info("Voidstep v1.2.5 submodule loaded.");
            InputSuppressionReady = false;
            NativeHotkeysReady = false;
            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(typeof(VoidstepSubModule).Assembly);
                InputSuppressionReady = true;
                _logger.Info("Generic conflicting-input suppression and mastery integration patches installed.");
            }
            catch (Exception ex)
            {
                _logger.Error("Voidstep patches could not be installed; ability input is disabled.", ex);
                TryCleanFailedInstallation();
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            NativeHotkeysReady = TryInitializeNativeHotkeys(_logger);
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            var starter = gameStarterObject as CampaignGameStarter;
            starter?.AddBehavior(new VoidstepProgressionBehavior());
        }

        public override void OnGameEnd(Game game)
        {
            CancelPendingCharacterScreenOpen();
            CloseMasteryScreen();
            _characterButton.Detach();
            VoidstepProgressionService.Detach();
            _masteryOpenLatch = false;
            base.OnGameEnd(game);
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);

            if (HandlePendingCharacterScreenOpen())
            {
                _masteryOpenLatch = false;
                return;
            }

            var top = ScreenManager.TopScreen;
            if (!(top is VoidstepMasteryScreen) && VoidstepCharacterScreenButtonController.IsCharacterDeveloperScreen(top))
                _characterButton.Resume();
            _characterButton.Tick();

            if (Campaign.Current == null || Mission.Current != null || !IsCampaignMapScreen(ScreenManager.TopScreen))
            {
                _masteryOpenLatch = false;
                return;
            }

            var control = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            var shift = Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift);
            var pressed = control && shift && Input.IsKeyPressed(InputKey.V);
            if (pressed && !_masteryOpenLatch) OpenScreenFromCampaignMap();
            _masteryOpenLatch = pressed;
        }

        protected override void OnSubModuleUnloaded()
        {
            InputSuppressionReady = false;
            NativeHotkeysReady = false;
            CancelPendingCharacterScreenOpen();
            CloseMasteryScreen();
            _characterButton.Detach();
            VoidstepProgressionService.Detach();
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

        internal static void OpenMasteryScreen()
        {
            OpenScreenFromCampaignMap();
        }

        private void RequestOpenFromCharacterScreen()
        {
            if (Campaign.Current == null || Mission.Current != null) return;
            if (!VoidstepCharacterScreenButtonController.IsCharacterDeveloperScreen(ScreenManager.TopScreen)) return;

            _characterButton.Suspend();
            _pendingCharacterScreenOpen = true;
            _pendingCharacterScreenOpenFrames = 1;
            _pendingCharacterScreenOpenTimeoutFrames = 300;
            _characterScreenOpenPhase = CharacterScreenOpenPhase.CloseCharacterState;
            _masteryOpenLatch = false;
        }

        private bool HandlePendingCharacterScreenOpen()
        {
            if (!_pendingCharacterScreenOpen) return false;

            if (Campaign.Current == null || Mission.Current != null || Game.Current == null)
            {
                CancelPendingCharacterScreenOpen();
                return false;
            }

            _pendingCharacterScreenOpenTimeoutFrames--;
            if (_pendingCharacterScreenOpenTimeoutFrames <= 0)
            {
                CancelPendingCharacterScreenOpen();
                return false;
            }

            if (_pendingCharacterScreenOpenFrames > 0)
            {
                _pendingCharacterScreenOpenFrames--;
                return true;
            }

            var top = ScreenManager.TopScreen;
            switch (_characterScreenOpenPhase)
            {
                case CharacterScreenOpenPhase.CloseCharacterState:
                    if (!VoidstepCharacterScreenButtonController.IsCharacterDeveloperScreen(top))
                    {
                        CancelPendingCharacterScreenOpen();
                        return false;
                    }

                    try
                    {
                        Game.Current.GameStateManager.PopState();
                        _characterScreenOpenPhase = CharacterScreenOpenPhase.WaitForCampaignMap;
                        _pendingCharacterScreenOpenFrames = 1;
                    }
                    catch
                    {
                        CancelPendingCharacterScreenOpen();
                        return false;
                    }
                    return true;

                case CharacterScreenOpenPhase.WaitForCampaignMap:
                    if (VoidstepCharacterScreenButtonController.IsCharacterDeveloperScreen(top))
                        return true;
                    if (!IsCampaignMapScreen(top))
                    {
                        CancelPendingCharacterScreenOpen();
                        return false;
                    }

                    _characterScreenOpenPhase = CharacterScreenOpenPhase.SettleCampaignMap;
                    _pendingCharacterScreenOpenFrames = 2;
                    return true;

                case CharacterScreenOpenPhase.SettleCampaignMap:
                    if (!IsCampaignMapScreen(top))
                    {
                        CancelPendingCharacterScreenOpen();
                        return false;
                    }

                    _pendingCharacterScreenOpen = false;
                    _pendingCharacterScreenOpenFrames = 0;
                    _pendingCharacterScreenOpenTimeoutFrames = 0;
                    _characterScreenOpenPhase = CharacterScreenOpenPhase.None;
                    ScreenManager.PushScreen(new VoidstepMasteryScreen());
                    return true;

                default:
                    CancelPendingCharacterScreenOpen();
                    return false;
            }
        }

        private void CancelPendingCharacterScreenOpen()
        {
            _pendingCharacterScreenOpen = false;
            _pendingCharacterScreenOpenFrames = 0;
            _pendingCharacterScreenOpenTimeoutFrames = 0;
            _characterScreenOpenPhase = CharacterScreenOpenPhase.None;
            _characterButton.Resume();
        }

        private static void OpenScreenFromCampaignMap()
        {
            if (Campaign.Current == null || Mission.Current != null) return;
            if (ScreenManager.TopScreen is VoidstepMasteryScreen) return;
            if (!IsCampaignMapScreen(ScreenManager.TopScreen)) return;
            ScreenManager.PushScreen(new VoidstepMasteryScreen());
        }

        private static void CloseMasteryScreen()
        {
            try
            {
                if (ScreenManager.TopScreen is VoidstepMasteryScreen)
                    ScreenManager.PopScreen();
            }
            catch
            {
                // Campaign or module teardown may already own the screen stack.
            }
        }

        private static bool IsCampaignMapScreen(ScreenBase screen)
        {
            if (screen == null) return false;
            var name = screen.GetType().Name;
            return name.Equals("MapScreen", StringComparison.OrdinalIgnoreCase) ||
                   name.IndexOf("CampaignMapScreen", StringComparison.OrdinalIgnoreCase) >= 0;
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

    internal static class VoidstepProgressionConsoleCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("open_mastery", "voidstep")]
        public static string OpenMastery(List<string> args)
        {
            if (Campaign.Current == null) return "Voidstep Mastery requires an active campaign.";
            if (Mission.Current != null) return "Voidstep Mastery cannot be opened during a mission.";
            VoidstepSubModule.OpenMasteryScreen();
            return ScreenManager.TopScreen is VoidstepMasteryScreen
                ? "Voidstep Mastery opened."
                : "Return to the campaign map before opening Voidstep Mastery.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("add_mastery_xp", "voidstep")]
        public static string AddMasteryXp(List<string> args)
        {
            var progression = VoidstepProgressionService.Current;
            if (progression == null) return "No active Voidstep campaign progression state.";
            int amount;
            if (args == null || args.Count != 1 || !int.TryParse(args[0], out amount) || amount <= 0)
                return "Usage: voidstep.add_mastery_xp <positive amount>";
            if (!progression.Enabled) return "Enable Voidstep mastery progression before awarding XP.";
            progression.AddXp(amount);
            return "Added " + amount + " Voidstep mastery XP.";
        }
    }
}
