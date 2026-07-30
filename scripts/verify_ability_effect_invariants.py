#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]
runtime = root / "src" / "Voidstep"
context_path = runtime / "AbilityContext.cs"
progression_patches_path = runtime / "VoidstepProgressionPatches.cs"
effects_path = runtime / "VoidstepAbilityEffects.cs"
effect_patches_path = runtime / "VoidstepAbilityEffectPatches.cs"

for path in (context_path, progression_patches_path, effects_path, effect_patches_path):
    if not path.is_file():
        print("FAIL missing runtime/effect file:", path.relative_to(root))
        sys.exit(1)

context = context_path.read_text(encoding="utf-8")
progression_patches = progression_patches_path.read_text(encoding="utf-8")
effects = effects_path.read_text(encoding="utf-8")
effect_patches = effect_patches_path.read_text(encoding="utf-8")

sync = "VoidstepProgressionBoundarySynchronizer.SynchronizeAll();"
enter = "VoidstepProgressionRuntimeScope.Enter();"
exit_scope = "VoidstepProgressionRuntimeScope.Exit();"

checks = {
    "AbilityContext constructor cannot abort PatchAll": (
        "ProgressionAbilityContextScopePatch" not in progression_patches
        and "HarmonyPatch(typeof(AbilityContext)" not in progression_patches
        and sync in context
        and enter in context
        and exit_scope in context
        and context.index(sync) < context.index(enter)
        and context.index("finally") < context.index(exit_scope)
    ),
    "all six abilities receive cast-time visual hooks": all(
        token in effect_patches
        for token in (
            "VoidstepCleaveEffectPatch",
            "BlinkSpellEffectPatch",
            "WindblastSpellEffectPatch",
            "BendTimeSpellEffectPatch",
            "DominoSpellEffectPatch",
            "DarkVisionSpellEffectPatch",
        )
    ),
    "Cleave effect is substantially larger and layered": all(
        token in effects
        for token in (
            "cleaveRadius * 1.15f",
            "Clamp(Math.Max(3.2f",
            "BurstRing(effects, center, outerRadius, 20",
            "outerRadius * 0.58f",
            "outerRadius * 0.34f",
        )
    ),
    "Windblast uses a directional widening cone": (
        effects.count("EmitWindAnchor(effects,") == 5
        and "Math.Tan(halfAngle)" in effects
        and "origin + forward * farDistance + right * farHalfWidth" in effects
        and "origin + forward * farDistance - right * farHalfWidth" in effects
    ),
    "effect workload is bounded": (
        "Math.Min(24, count)" in effects
        and "Clamp(range, 3f, 18f)" in effects
        and "Clamp(visionRange * 0.13f, 2.5f, 5.5f)" in effects
        and "OnMissionTick" not in effects + effect_patches
        and "List<Agent" not in effects + effect_patches
        and "Dictionary<int, Agent" not in effects + effect_patches
    ),
    "visual failures cannot interrupt abilities": (
        "Visual enhancement must never interrupt the ability." in effect_patches
        and effect_patches.count("catch") >= 6
        and "Visual enhancement must never affect ability execution." in effects
    ),
    "failed casts do not emit success visuals": (
        "if (!__result || actor == null) return;" in effect_patches
        and effect_patches.count("if (!__result || player == null") >= 4
        and "if (__result <= 0 || player == null" in effect_patches
    ),
}

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(("PASS " if passed else "FAIL ") + name)
if failed:
    sys.exit(1)
print("Voidstep runtime patch and ability effect invariants passed.")
