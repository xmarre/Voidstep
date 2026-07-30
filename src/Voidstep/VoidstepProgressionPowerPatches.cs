using HarmonyLib;

namespace Voidstep
{
    [HarmonyPatch(typeof(VoidstepSettings), "get_CleaveRadius")]
    internal static class ProgressionCleaveRadiusPatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveCleaveRadius(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_CleaveSweepDegrees")]
    internal static class ProgressionCleaveSweepPatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveCleaveSweepDegrees(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_CleaveDamageMultiplier")]
    internal static class ProgressionCleaveDamagePatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveCleaveDamageMultiplier(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_CleaveKnockback")]
    internal static class ProgressionCleaveKnockbackPatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveCleaveKnockback(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_CleaveKnockdownThreshold")]
    internal static class ProgressionCleaveKnockdownPatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveCleaveKnockdownThreshold(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_MaximumCleaveTargets")]
    internal static class ProgressionCleaveTargetsPatch
    {
        private static void Postfix(ref int __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveMaximumCleaveTargets(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_VoidstepRange")]
    internal static class ProgressionVoidstepRangePatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveVoidstepRange(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_BlinkRange")]
    internal static class ProgressionBlinkRangePatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveBlinkRange(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_BlinkThroughWalls")]
    internal static class ProgressionBlinkWallTraversalPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.AllowWallTraversal(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_WindblastAngle")]
    internal static class ProgressionWindblastAnglePatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveWindblastAngle(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_WindblastRange")]
    internal static class ProgressionWindblastRangePatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveWindblastRange(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_WindblastForce")]
    internal static class ProgressionWindblastForcePatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveWindblastForce(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_WindblastDamage")]
    internal static class ProgressionWindblastDamagePatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveWindblastDamage(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_BendTimeFactor")]
    internal static class ProgressionBendTimeFactorPatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveBendTimeFactor(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_BendTimeDuration")]
    internal static class ProgressionBendTimeDurationPatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveBendTimeDuration(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_DominoMaximumLinks")]
    internal static class ProgressionDominoLinksPatch
    {
        private static void Postfix(ref int __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveDominoMaximumLinks(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_DominoDamageFactor")]
    internal static class ProgressionDominoDamagePatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveDominoDamageFactor(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_DominoRange")]
    internal static class ProgressionDominoRangePatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveDominoRange(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_DarkVisionRange")]
    internal static class ProgressionDarkVisionRangePatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveDarkVisionRange(__result);
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_DarkVisionRefreshInterval")]
    internal static class ProgressionDarkVisionRefreshPatch
    {
        private static void Postfix(ref float __result)
        {
            if (VoidstepProgressionRuntimeScope.Active)
                __result = VoidstepProgressionService.Profile.EffectiveDarkVisionRefreshInterval(__result);
        }
    }
}
