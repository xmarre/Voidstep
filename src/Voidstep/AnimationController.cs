using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    internal sealed class AnimationController
    {
        // This action is present as a concrete ActionIndexCache field in the supplied
        // Bannerlord 1.3.15 assembly. Weapon-specific candidates remain an isolated
        // future extension point rather than unsafe startup-time string lookups.
        private static ActionIndexCache VerifiedStrike => ActionIndexCache.act_strike_bent_over;

        public void BeginCleave(Agent actor, bool clockwise)
        {
            if (actor == null || !actor.IsActive()) return;
            var action = VerifiedStrike;
            actor.SetActionChannel(1, ref action, true, (AnimFlags)0, 0f, 0.9f, 0.08f, 0.15f, 0f, true, 0.1f, 0, true);
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

        public void Cancel(Agent actor)
        {
            if (actor == null || !actor.IsActive()) return;
            actor.SetCurrentActionSpeed(1, 1f);
        }
    }
}
