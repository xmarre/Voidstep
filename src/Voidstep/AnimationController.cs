using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    internal sealed class AnimationController
    {
        // Cleave presentation deliberately does not force an engine action. The former
        // victim-stagger action could replace the player's combat state and wielded
        // weapon. Rotation, weapon trails and timed native blows provide presentation
        // without taking ownership of Bannerlord's weapon-action channels.
        public void BeginCleave(Agent actor)
        {
        }

        public void SetCleaveProgress(Agent actor, float progress)
        {
        }

        public void RotateActor(Agent actor, float radians)
        {
            if (actor == null || !actor.IsActive()) return;
            var look = actor.LookDirection;
            look.z = 0f;
            if (look.Normalize() < 0.001f)
                look = Vec3.Forward;
            look.RotateAboutZ(radians);
            actor.LookDirection = look;
        }

        public void ResetActionSpeed(Agent actor)
        {
            // No action channel is owned by this controller.
        }
    }
}
