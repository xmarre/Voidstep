using System;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class AbilityContext
    {
        public AbilityContext(Mission mission, VoidstepLogger logger)
        {
            Mission = mission ?? throw new ArgumentNullException(nameof(mission));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // AbilityContext is Voidstep-owned code. Enter the progression settings scope
            // directly instead of Harmony-patching this constructor; older shipped Harmony
            // builds can fail to resolve constructor-only patch attributes and abort PatchAll.
            VoidstepProgressionBoundarySynchronizer.SynchronizeAll();
            VoidstepProgressionRuntimeScope.Enter();
            try
            {
                Energy = new VoidEnergyPool(Math.Max(1f, VoidstepSettings.Current.MaximumEnergy));
                Cooldowns = new CooldownBook();
            }
            finally
            {
                VoidstepProgressionRuntimeScope.Exit();
            }
        }

        public Mission Mission { get; }
        public VoidstepLogger Logger { get; }
        public VoidEnergyPool Energy { get; }
        public CooldownBook Cooldowns { get; }
        public Agent Player => Mission?.MainAgent;

        public bool IsPlayerUsable()
        {
            var player = Player;
            return player != null && player.IsActive() && player.Health > 0f && player.State == TaleWorlds.Core.AgentState.Active;
        }
    }
}
