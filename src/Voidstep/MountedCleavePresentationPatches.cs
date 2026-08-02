using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class CleaveWeaponPresentationState
    {
        internal Agent Actor;
        internal int ActorIndex = -1;
        internal float Duration;
        internal float ActionStart;
        internal float ActionEnd;
        internal bool ActionOwned;
        internal int ActionIndex = -1;
    }

    internal static class CleaveWeaponPresentationRuntime
    {
        private static readonly ConditionalWeakTable<CleaveSweepController, CleaveWeaponPresentationState> States =
            new ConditionalWeakTable<CleaveSweepController, CleaveWeaponPresentationState>();

        private static readonly string[] MountedPolearmActions =
        {
            "act_map_mount_attack_spear",
            "act_map_rider_horse_attack_1h_spear",
            "act_map_rider_camel_attack_1h_spear"
        };

        private static readonly string[] MountedTwoHandedActions =
        {
            "act_map_mount_attack_swing",
            "act_map_rider_horse_attack_2h_swing",
            "act_map_rider_camel_attack_2h_swing"
        };

        private static readonly string[] MountedOneHandedActions =
        {
            "act_map_mount_attack_1h",
            "act_map_rider_horse_attack_1h_swing",
            "act_map_rider_camel_attack_1h_swing"
        };

        private static readonly string[] FootPolearmActions =
        {
            "act_map_attack_spear_1h_or_2h",
            "act_map_attack_2h"
        };

        private static readonly string[] FootTwoHandedActions =
        {
            "act_map_attack_2h",
            "act_map_attack_1h"
        };

        private static readonly string[] FootOneHandedActions =
        {
            "act_map_attack_1h",
            "act_map_attack_2h"
        };

        internal static MissionWeapon ResolveExecutionWeapon(
            Agent actor,
            MissionWeapon captured,
            VoidstepLogger logger)
        {
            if (actor == null || !actor.IsActive())
                return captured;

            var current = actor.WieldedWeapon;
            if (!WeaponValidation.IsUsableMeleeWeapon(current))
                return captured;

            logger?.Debug($"Cleave rebound to currently wielded weapon '{DescribeWeapon(current)}'.");
            return current;
        }

        internal static void Begin(
            CleaveSweepController controller,
            Agent actor,
            MissionWeapon weapon,
            float sweepDegrees,
            VoidstepLogger logger)
        {
            if (controller == null || actor == null || !actor.IsActive())
                return;

            End(controller);
            var state = States.GetValue(controller, _ => new CleaveWeaponPresentationState());
            state.Actor = actor;
            state.ActorIndex = actor.Index;
            state.Duration = CleavePresentationMath.CalculateDuration(sweepDegrees);
            state.ActionStart = CleavePresentationMath.CalculateActionStartProgress(sweepDegrees);
            state.ActionEnd = CleavePresentationMath.CalculateActionEndProgress(sweepDegrees);
            state.ActionOwned = TryStartWeaponAction(
                actor,
                weapon,
                state.ActionStart,
                out var actionIndex,
                out var actionName,
                logger);
            state.ActionIndex = actionIndex;

            logger?.Debug(
                $"Cleave presentation actor={actor.Index}, weapon='{DescribeWeapon(weapon)}', " +
                $"sweep={sweepDegrees:0}deg, duration={state.Duration:0.000}s, " +
                $"action='{actionName ?? "none"}', actionWindow={state.ActionStart:0.00}-{state.ActionEnd:0.00}.");
        }

        internal static bool TryGetDuration(CleaveSweepController controller, out float duration)
        {
            duration = 0f;
            if (controller == null || !States.TryGetValue(controller, out var state))
                return false;
            duration = state.Duration;
            return duration > 0f;
        }

        internal static void Tick(CleaveSweepController controller, float progress, VoidstepLogger logger)
        {
            if (controller == null || !States.TryGetValue(controller, out var state))
                return;

            var actor = state.Actor;
            if (!OwnsCurrentAction(state, actor))
                return;

            try
            {
                progress = Math.Max(0f, Math.Min(1f, progress));
                var eased = progress * progress * (3f - 2f * progress);
                actor.SetCurrentActionProgress(
                    1,
                    state.ActionStart + (state.ActionEnd - state.ActionStart) * eased);
            }
            catch (Exception ex)
            {
                logger?.Debug("Cleave weapon action progress update failed: " + ex.Message);
                state.ActionOwned = false;
            }
        }

        internal static void End(CleaveSweepController controller)
        {
            if (controller == null || !States.TryGetValue(controller, out var state))
                return;

            FinishAction(state);
            state.Actor = null;
            state.ActorIndex = -1;
            state.Duration = 0f;
            state.ActionStart = 0f;
            state.ActionEnd = 0f;
            state.ActionIndex = -1;
            States.Remove(controller);
        }

        internal static void EmitArc(
            BodyAlignedCleaveState bodyState,
            Agent actor,
            EffectController effects,
            float radius,
            double sweepRadians,
            SweepDirection direction,
            float progress)
        {
            if (bodyState == null || actor == null || effects == null || !actor.IsActive())
                return;

            progress = Math.Max(0f, Math.Min(1f, progress));
            var sweepFraction = Math.Max(0.05, Math.Min(1.0, Math.Abs(sweepRadians) / (Math.PI * 2.0)));
            var totalSamples = Math.Max(12, Math.Min(40, (int)Math.Ceiling(36.0 * sweepFraction)));
            var emittedThisTick = 0;
            var center = ResolveSweepCenter(actor);
            var reach = CleavePresentationMath.CalculateVisualReach(ResolveWeaponLength(actor), radius);
            var mounted = actor.MountAgent != null && actor.MountAgent.IsActive();

            while (bodyState.VisualBurstIndex < totalSamples && emittedThisTick < 6)
            {
                var sample = (bodyState.VisualBurstIndex + 1f) / totalSamples;
                if (sample > progress + 0.0001f)
                    break;

                var eased = sample * sample * (3f - 2f * sample);
                var angle = bodyState.StartAngle + (int)direction * sweepRadians * eased;
                var arcDirection = new Vec3((float)Math.Cos(angle), (float)Math.Sin(angle), 0f, 0f);
                var outer = center + arcDirection * reach;
                effects.WeaponTrail(outer);
                effects.WeaponTrail(outer - Vec3.Up * (mounted ? 0.22f : 0.08f));
                if ((bodyState.VisualBurstIndex & 1) == 0)
                    effects.WeaponTrail(center + arcDirection * Math.Max(0.65f, reach * 0.68f));

                bodyState.VisualBurstIndex++;
                emittedThisTick++;
            }

            if (!bodyState.ForwardBurstPlayed && progress >= 0.5f)
            {
                bodyState.ForwardBurstPlayed = true;
                var forward = BodyAlignedCleaveRuntime.NormalizeFacing(bodyState.Facing);
                var right = new Vec3(-forward.y, forward.x, 0f, 0f);
                effects.WeaponTrail(center + forward * reach);
                effects.WeaponTrail(center + forward * (reach * 0.84f) + right * 0.32f);
                effects.WeaponTrail(center + forward * (reach * 0.84f) - right * 0.32f);
            }
        }

        private static bool TryStartWeaponAction(
            Agent actor,
            MissionWeapon weapon,
            float startProgress,
            out int actionIndex,
            out string actionName,
            VoidstepLogger logger)
        {
            actionIndex = -1;
            actionName = null;
            var candidates = ResolveActions(actor, weapon);
            for (var i = 0; i < candidates.Length; i++)
            {
                try
                {
                    var action = ActionIndexCache.Create(candidates[i]);
                    if (action.Index < 0 || !actor.SetActionChannel(1, action, true))
                        continue;

                    actor.SetCurrentActionSpeed(1, 0.01f);
                    actor.SetCurrentActionProgress(1, startProgress);
                    actionIndex = action.Index;
                    actionName = candidates[i];
                    return true;
                }
                catch (Exception ex)
                {
                    logger?.Debug($"Optional Cleave weapon action '{candidates[i]}' unavailable: {ex.Message}");
                }
            }

            return false;
        }

        private static string[] ResolveActions(Agent actor, MissionWeapon weapon)
        {
            var polearm = false;
            var twoHanded = false;
            try
            {
                var usage = weapon.CurrentUsageItem;
                polearm = usage != null && usage.IsPolearm;
                twoHanded = usage != null && usage.IsTwoHanded;
            }
            catch
            {
            }

            var mounted = actor.MountAgent != null && actor.MountAgent.IsActive();
            if (mounted)
                return polearm ? MountedPolearmActions : twoHanded ? MountedTwoHandedActions : MountedOneHandedActions;
            return polearm ? FootPolearmActions : twoHanded ? FootTwoHandedActions : FootOneHandedActions;
        }

        private static Vec3 ResolveSweepCenter(Agent actor)
        {
            var center = actor.GetChestGlobalPosition();
            var mount = actor.MountAgent;
            if (mount == null || !mount.IsActive())
                return center;

            center.z = CleavePresentationMath.CalculateMountedSweepHeight(mount.Position.z, center.z);
            return center;
        }

        private static float ResolveWeaponLength(Agent actor)
        {
            if (actor != null)
            {
                try
                {
                    var usage = actor.WieldedWeapon.CurrentUsageItem;
                    if (usage != null && usage.WeaponLength > 0)
                        return usage.WeaponLength * 0.01f;
                }
                catch
                {
                }
            }

            return 1.10f;
        }

        private static string DescribeWeapon(MissionWeapon weapon)
        {
            try
            {
                var item = weapon.Item;
                var usage = weapon.CurrentUsageItem;
                return item != null
                    ? $"{item.StringId}/{usage?.WeaponClass.ToString() ?? "unknown"}"
                    : usage?.WeaponClass.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static bool OwnsCurrentAction(CleaveWeaponPresentationState state, Agent actor)
        {
            if (!state.ActionOwned || actor == null || !actor.IsActive() || actor.Index != state.ActorIndex)
                return false;

            try
            {
                if (actor.GetCurrentAction(1).Index == state.ActionIndex)
                    return true;
            }
            catch
            {
            }

            state.ActionOwned = false;
            return false;
        }

        private static void FinishAction(CleaveWeaponPresentationState state)
        {
            var actor = state.Actor;
            if (!OwnsCurrentAction(state, actor))
                return;

            state.ActionOwned = false;
            try
            {
                actor.SetCurrentActionSpeed(1, 1f);
                actor.SetCurrentActionProgress(1, 0.99f);
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(CleaveSweepController), nameof(CleaveSweepController.Begin))]
    internal static class CleaveCurrentWeaponAndPresentationPatch
    {
        private static void Prefix(
            Agent actor,
            ref MissionWeapon weapon,
            VoidstepLogger ____logger)
        {
            weapon = CleaveWeaponPresentationRuntime.ResolveExecutionWeapon(actor, weapon, ____logger);
        }

        private static void Postfix(
            CleaveSweepController __instance,
            Agent actor,
            MissionWeapon weapon,
            CleaveExecutionSnapshot snapshot,
            ref float ____duration,
            bool __result,
            VoidstepLogger ____logger)
        {
            if (!__result)
                return;

            CleaveWeaponPresentationRuntime.Begin(
                __instance,
                actor,
                weapon,
                snapshot.SweepDegrees,
                ____logger);
            if (CleaveWeaponPresentationRuntime.TryGetDuration(__instance, out var duration))
                ____duration = duration;
        }
    }

    [HarmonyPatch(typeof(CleaveSweepController), nameof(CleaveSweepController.Tick))]
    internal static class CleaveBroadnessAndWeaponActionTickPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            CleaveSweepController __instance,
            float dt,
            float ____elapsed,
            ref float ____duration,
            VoidstepLogger ____logger)
        {
            if (!CleaveWeaponPresentationRuntime.TryGetDuration(__instance, out var duration))
                return;

            ____duration = duration;
            var progress = duration <= 0f
                ? 1f
                : Math.Min(1f, (____elapsed + Math.Max(0f, dt)) / duration);
            CleaveWeaponPresentationRuntime.Tick(__instance, progress, ____logger);
        }

        private static void Postfix(CleaveSweepController __instance, bool __result)
        {
            if (__result)
                CleaveWeaponPresentationRuntime.End(__instance);
        }
    }

    [HarmonyPatch(
        typeof(CleaveSweepController),
        nameof(CleaveSweepController.Cleanup),
        new Type[] { })]
    internal static class CleaveWeaponActionCleanupPatch
    {
        private static void Prefix(CleaveSweepController __instance) =>
            CleaveWeaponPresentationRuntime.End(__instance);
    }

    [HarmonyPatch(
        typeof(BodyAlignedCleaveRuntime),
        nameof(BodyAlignedCleaveRuntime.EmitArc))]
    internal static class MountedWeaponLengthCleaveArcPatch
    {
        private static bool Prefix(
            BodyAlignedCleaveState state,
            Agent actor,
            EffectController effects,
            float radius,
            double sweepRadians,
            SweepDirection direction,
            float progress)
        {
            CleaveWeaponPresentationRuntime.EmitArc(
                state,
                actor,
                effects,
                radius,
                sweepRadians,
                direction,
                progress);
            return false;
        }
    }
}
