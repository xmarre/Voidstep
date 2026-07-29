using System;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class AnimationController
    {
        private static readonly string[] HeavyCastActions = { "act_release_heavy_thrown", "act_command_2h", "act_command_unarmed" };
        private static readonly string[] QuickCastActions = { "act_release_stone", "act_command_follow_unarmed", "act_command_unarmed" };
        private static readonly string[] VisionCastActions = { "act_command_unarmed", "act_taunt_cheer_2", "act_release_heavy_thrown" };
        private static readonly string[] MountedCastActions = { "act_horse_command_unarmed", "act_horse_command", "act_release_heavy_thrown" };

        private readonly VoidstepLogger _logger;
        private Agent _cleaveActor;
        private bool _cleaveActionOwned;

        public AnimationController(VoidstepLogger logger) => _logger = logger;

        public void PlayCast(Agent actor, AbilityId ability)
        {
            if (actor == null || !actor.IsActive()) return;
            var candidates = actor.MountAgent != null && actor.MountAgent.IsActive()
                ? MountedCastActions
                : ResolveCastActions(ability);
            TryPlay(actor, candidates, false);
        }

        public void BeginCleave(Agent actor)
        {
            _cleaveActor = null;
            _cleaveActionOwned = false;
            if (actor == null || !actor.IsActive()) return;

            // A native heavy-release action gives the rotating sweep a visible full-body
            // motion while the actual damage remains the separately registered melee blows.
            // Progress is driven by the sweep so animation and hit timing stay aligned.
            if (!TryPlay(actor, HeavyCastActions, true)) return;
            try
            {
                actor.SetCurrentActionSpeed(1, 0.01f);
                actor.SetCurrentActionProgress(1, 0f);
                _cleaveActor = actor;
                _cleaveActionOwned = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("Cleave action synchronization unavailable: " + ex.Message);
                ResetActionSpeed(actor);
            }
        }

        public void SetCleaveProgress(Agent actor, float progress)
        {
            if (!_cleaveActionOwned || actor == null || !ReferenceEquals(actor, _cleaveActor) || !actor.IsActive()) return;
            try
            {
                actor.SetCurrentActionProgress(1, Math.Max(0f, Math.Min(0.98f, progress)));
            }
            catch (Exception ex)
            {
                _logger.Debug("Cleave action progress update failed: " + ex.Message);
                ResetActionSpeed(actor);
            }
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
            var owned = _cleaveActionOwned && actor != null && ReferenceEquals(actor, _cleaveActor);
            _cleaveActionOwned = false;
            _cleaveActor = null;
            if (!owned || actor == null || !actor.IsActive()) return;
            try
            {
                actor.SetCurrentActionSpeed(1, 1f);
                actor.SetCurrentActionProgress(1, 0.99f);
            }
            catch (Exception ex)
            {
                _logger.Debug("Cleave action cleanup failed: " + ex.Message);
            }
        }

        private bool TryPlay(Agent actor, string[] candidates, bool cleave)
        {
            for (var i = 0; i < candidates.Length; i++)
            {
                try
                {
                    var action = ActionIndexCache.Create(candidates[i]);
                    if (action.Index < 0) continue;
                    if (!actor.SetActionChannel(1, action)) continue;
                    _logger.Debug($"Started {(cleave ? "Cleave execution" : "ability cast")} action '{candidates[i]}' on actor={actor.Index}.");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Optional cast action '{candidates[i]}' unavailable: {ex.Message}");
                }
            }
            _logger.Debug($"No compatible native {(cleave ? "Cleave execution" : "cast")} action was accepted for actor={actor.Index}.");
            return false;
        }

        private static string[] ResolveCastActions(AbilityId ability)
        {
            switch (ability)
            {
                case AbilityId.VoidstepCleave:
                case AbilityId.Windblast:
                case AbilityId.BendTime:
                    return HeavyCastActions;
                case AbilityId.DarkVision:
                    return VisionCastActions;
                case AbilityId.Blink:
                case AbilityId.Domino:
                default:
                    return QuickCastActions;
            }
        }
    }
}
