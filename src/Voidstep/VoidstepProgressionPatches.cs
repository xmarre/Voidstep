using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal static class VoidstepProgressionRuntimeScope
    {
        [ThreadStatic]
        private static int _depth;

        internal static bool Active => _depth > 0;

        internal static Lease Enter()
        {
            _depth++;
            return new Lease();
        }

        internal sealed class Lease : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                if (_depth > 0) _depth--;
            }
        }
    }

    [HarmonyPatch(typeof(AbilityContext), MethodType.Constructor)]
    internal static class ProgressionAbilityContextScopePatch
    {
        private static void Prefix(out VoidstepProgressionRuntimeScope.Lease __state)
        {
            __state = VoidstepProgressionRuntimeScope.Enter();
        }

        private static void Postfix(VoidstepProgressionRuntimeScope.Lease __state)
        {
            __state?.Dispose();
        }

        private static Exception Finalizer(Exception __exception, VoidstepProgressionRuntimeScope.Lease __state)
        {
            __state?.Dispose();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(AbilityManager), nameof(AbilityManager.Tick))]
    internal static class ProgressionAbilityTickScopePatch
    {
        private static void Prefix(out VoidstepProgressionRuntimeScope.Lease __state)
        {
            __state = VoidstepProgressionRuntimeScope.Enter();
        }

        private static void Postfix(VoidstepProgressionRuntimeScope.Lease __state)
        {
            __state?.Dispose();
        }

        private static Exception Finalizer(Exception __exception, VoidstepProgressionRuntimeScope.Lease __state)
        {
            __state?.Dispose();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(AbilityManager), nameof(AbilityManager.TryActivate))]
    internal static class ProgressionAbilityActivationPatch
    {
        private static bool Prefix(
            AbilityManager __instance,
            AbilityId ability,
            ref bool __result,
            out VoidstepProgressionRuntimeScope.Lease __state)
        {
            __state = VoidstepProgressionRuntimeScope.Enter();

            if (ability == AbilityId.DarkVision && __instance.IsDarkVisionActive)
                return true;

            string reason;
            if (VoidstepProgressionService.Profile.CanUse(ability, out reason))
                return true;

            __result = false;
            try { InformationManager.DisplayMessage(new InformationMessage(reason)); }
            catch { }
            return false;
        }

        private static void Postfix(VoidstepProgressionRuntimeScope.Lease __state)
        {
            __state?.Dispose();
        }

        private static Exception Finalizer(Exception __exception, VoidstepProgressionRuntimeScope.Lease __state)
        {
            __state?.Dispose();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_MaximumEnergy")]
    internal static class ProgressionMaximumEnergyPatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveMaximumEnergy(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_EnergyRegeneration")]
    internal static class ProgressionEnergyRegenerationPatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveEnergyRegeneration(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_UnlimitedEnergy")]
    internal static class ProgressionUnlimitedEnergyPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.AllowUnlimitedEnergy(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_CooldownOnlyMode")]
    internal static class ProgressionCooldownOnlyPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.AllowCooldownOnlyMode(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_BlinkPreserveMomentum")]
    internal static class ProgressionBlinkMomentumPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.AllowMomentumPreservation(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_AllowCompleteSuspension")]
    internal static class ProgressionCompleteSuspensionPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.AllowCompleteSuspension(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), nameof(VoidstepSettings.Cost))]
    internal static class ProgressionAbilityCostPatch
    {
        private static void Postfix(AbilityId id, ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveCost(id, __result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), nameof(VoidstepSettings.Cooldown))]
    internal static class ProgressionAbilityCooldownPatch
    {
        private static void Postfix(AbilityId id, ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveCooldown(id, __result);
        }
    }

    internal static class VoidstepProgressionAwardLimiter
    {
        private sealed class AwardState
        {
            internal readonly Dictionary<AbilityId, double> LastAwards = new Dictionary<AbilityId, double>();
        }

        private static readonly ConditionalWeakTable<object, AwardState> States =
            new ConditionalWeakTable<object, AwardState>();

        internal static void Award(object owner, AbilityId ability, int amount, double minimumIntervalSeconds)
        {
            if (owner == null || amount <= 0 || !VoidstepProgressionService.Enabled) return;

            var state = States.GetOrCreateValue(owner);
            var now = (double)MBCommon.GetApplicationTime();
            double last;
            if (state.LastAwards.TryGetValue(ability, out last) && now - last < minimumIntervalSeconds)
                return;

            state.LastAwards[ability] = now;
            VoidstepProgressionService.AddXp(amount);
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "ConfirmBlink")]
    internal static class ProgressionBlinkXpPatch
    {
        private static void Postfix(AbilityManager __instance, bool __result)
        {
            if (__result) VoidstepProgressionAwardLimiter.Award(__instance, AbilityId.Blink, 3, 1.5d);
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "CastWindblast")]
    internal static class ProgressionWindblastXpPatch
    {
        private static void Postfix(AbilityManager __instance, bool __result)
        {
            if (__result) VoidstepProgressionAwardLimiter.Award(__instance, AbilityId.Windblast, 5, 2d);
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "CastBendTime")]
    internal static class ProgressionBendTimeXpPatch
    {
        private static void Postfix(AbilityManager __instance, bool __result)
        {
            if (__result) VoidstepProgressionAwardLimiter.Award(__instance, AbilityId.BendTime, 4, 8d);
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "CastDomino")]
    internal static class ProgressionDominoXpPatch
    {
        private static void Postfix(AbilityManager __instance, bool __result)
        {
            if (__result) VoidstepProgressionAwardLimiter.Award(__instance, AbilityId.Domino, 6, 4d);
        }
    }

    [HarmonyPatch(typeof(AbilityManager), "CastDarkVision")]
    internal static class ProgressionDarkVisionXpPatch
    {
        private static void Postfix(AbilityManager __instance, bool __result)
        {
            if (__result) VoidstepProgressionAwardLimiter.Award(__instance, AbilityId.DarkVision, 2, 8d);
        }
    }

    [HarmonyPatch(typeof(CleaveSweepController), nameof(CleaveSweepController.Tick))]
    internal static class ProgressionCleaveXpPatch
    {
        private static void Prefix(CleaveSweepController __instance, out bool __state)
        {
            __state = __instance.Active;
        }

        private static void Postfix(CleaveSweepController __instance, bool __state, bool __result)
        {
            if (!__state || !__result || __instance.SuccessfulHits <= 0) return;
            var amount = 3 + Math.Min(18, __instance.SuccessfulHits * 2);
            VoidstepProgressionAwardLimiter.Award(__instance, AbilityId.VoidstepCleave, amount, 2d);
        }
    }
}
