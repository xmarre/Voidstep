using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    public sealed class VoidstepSubModule : MBSubModuleBase
    {
        protected override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            if (mission != null)
                mission.AddMissionBehavior(new VoidstepMissionBehavior());
        }
    }
}
