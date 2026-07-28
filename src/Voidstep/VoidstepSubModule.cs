using System;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    public sealed class VoidstepSubModule : MBSubModuleBase
    {
        private static VoidstepLogger _logger;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            _logger = new VoidstepLogger();
            _logger.Info("Voidstep v1.0.2 submodule loaded.");
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
