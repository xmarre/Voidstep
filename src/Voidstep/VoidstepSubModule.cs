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

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            _logger = new VoidstepLogger();
            _logger.Info("Voidstep v1.0.4 submodule loaded.");
            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(typeof(VoidstepSubModule).Assembly);
                _logger.Info("Ctrl+number formation-input suppression patches installed.");
            }
            catch (Exception ex)
            {
                _logger.Error("Voidstep input-conflict patches could not be installed.", ex);
            }
        }

        protected override void OnSubModuleUnloaded()
        {
            try { _harmony?.UnpatchAll(HarmonyId); }
            catch (Exception ex) { _logger?.Debug("Harmony cleanup failed: " + ex.Message); }
            _harmony = null;
            base.OnSubModuleUnloaded();
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            if (mission == null) return;

            var logger = _logger ?? new VoidstepLogger();
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
    }
}
