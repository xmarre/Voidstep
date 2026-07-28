using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    internal sealed class AnimationController
    {
        // This action is present as a concrete ActionIndexCache field in the supplied
        // Bannerlord 1.3.15 assembly. Sweep direction is driven by actor rotation;
        // the verified action has no independently verified mirrored counterpart.
        private static ActionIndexCache VerifiedStrike => ActionIndexCache.act_strike_bent_over;

        public void BeginCleave(Agent actor)
        {
            if (actor == null || !actor.IsActive()) return;
            var action = VerifiedStrike;
            actor.SetActionChannel(1, in action, true, (AnimFlags)0, 0f, 0.9f, 0.08f, 0.15f, 0f, true, 0.1f, 0, true);
        }

        public void SetCleaveProgress(Agent actor, float progress)
        {
            if (actor == null || !actor.IsActive()) return;
            actor.SetCurrentActionProgress(1, progress < 0f ? 0f : progress > 1f ? 1f : progress);
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
            if (actor == null || !actor.IsActive()) return;
            actor.SetCurrentActionSpeed(1, 1f);
        }
    }
}
