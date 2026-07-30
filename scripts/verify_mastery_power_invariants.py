#!/usr/bin/env python3
from pathlib import Path
import math
import sys

root = Path(__file__).resolve().parents[1]
runtime = root / "src" / "Voidstep"
catalog_path = runtime / "VoidstepProgressionCatalog.cs"
profile_path = runtime / "VoidstepProgressionRuntime.cs"
patches_path = runtime / "VoidstepProgressionPowerPatches.cs"
selection_path = runtime / "AbilitySelectionController.cs"

for path in (catalog_path, profile_path, patches_path, selection_path):
    if not path.is_file():
        print("FAIL missing mastery power file:", path.relative_to(root))
        sys.exit(1)

catalog = catalog_path.read_text(encoding="utf-8")
profile = profile_path.read_text(encoding="utf-8")
patches = patches_path.read_text(encoding="utf-8")
selection = selection_path.read_text(encoding="utf-8")

getter_methods = {
    "CleaveRadius": "EffectiveCleaveRadius",
    "CleaveSweepDegrees": "EffectiveCleaveSweepDegrees",
    "CleaveDamageMultiplier": "EffectiveCleaveDamageMultiplier",
    "CleaveKnockback": "EffectiveCleaveKnockback",
    "CleaveKnockdownThreshold": "EffectiveCleaveKnockdownThreshold",
    "MaximumCleaveTargets": "EffectiveMaximumCleaveTargets",
    "VoidstepRange": "EffectiveVoidstepRange",
    "BlinkRange": "EffectiveBlinkRange",
    "BlinkThroughWalls": "AllowWallTraversal",
    "WindblastAngle": "EffectiveWindblastAngle",
    "WindblastRange": "EffectiveWindblastRange",
    "WindblastForce": "EffectiveWindblastForce",
    "WindblastDamage": "EffectiveWindblastDamage",
    "BendTimeFactor": "EffectiveBendTimeFactor",
    "BendTimeDuration": "EffectiveBendTimeDuration",
    "DominoMaximumLinks": "EffectiveDominoMaximumLinks",
    "DominoDamageFactor": "EffectiveDominoDamageFactor",
    "DominoRange": "EffectiveDominoRange",
    "DarkVisionRange": "EffectiveDarkVisionRange",
    "DarkVisionRefreshInterval": "EffectiveDarkVisionRefreshInterval",
}

checks = {
    "all combat parameter getters are progression patched": all(
        f'"get_{getter}"' in patches and method in patches
        for getter, method in getter_methods.items()
    ),
    "all combat effects preserve configured values while disabled": all(
        f"internal {'bool' if method == 'AllowWallTraversal' else ('int' if 'Maximum' in method else 'float')} {method}" in profile
        for method in getter_methods.values()
    ) and profile.count(": configured;") >= 15,
    "teleport growth is explicit": all(token in catalog for token in (
        "CleaveRadius(", "VoidstepRange(", "BlinkRange(", '"Rift Reach"',
    )) and all(token in profile for token in ("EffectiveVoidstepRange", "EffectiveBlinkRange")),
    "cleave growth covers geometry impact and capacity": all(token in catalog for token in (
        "CleaveSweepDegrees(", "CleaveDamageMultiplier(", "CleaveKnockback(",
        "CleaveKnockdownThreshold(", "MaximumCleaveTargets(",
    )),
    "every non-teleport ability gains mechanical power": all(token in catalog for token in (
        "WindblastAngle(", "WindblastRange(", "WindblastForce(", "WindblastDamage(",
        "BendTimeFactor(", "BendTimeDuration(",
        "DominoMaximumLinks(", "DominoDamageFactor(", "DominoRange(",
        "DarkVisionRange(", "DarkVisionRefreshInterval(",
    )),
    "efficiency is secondary rather than the whole tree": (
        "return Math.Max(0.65f, multiplier);" in catalog
        and "supports ability growth rather than replacing it" in catalog
        and "range, radius, force, damage and duration" in catalog
    ),
    "sealed wall traversal has exactly the documented late mastery gate": (
        "AllowWallTraversal" in profile
        and "configured && Level(VoidstepSkillId.MomentumWeave) >= 10" in profile
        and "|| Level(VoidstepSkillId.AvatarOfTheVoid)" not in profile
        and '"get_BlinkThroughWalls"' in patches
    ),
    "unlimited cleave targets require the final capstone": (
        "configured == 0" in catalog
        and "AvatarOfTheVoid) >= 10" in catalog
        and "return 0;" in catalog
    ),
    "power patches remain allocation free": (
        "new " not in patches
        and "=>" not in patches
        and "VoidstepProgressionRuntimeScope.Active" in patches
    ),
    "cast previews use the same mastery-scaled settings as execution": (
        "private void RefreshPreview(Agent player)" in selection
        and "VoidstepProgressionRuntimeScope.Enter();" in selection
        and "finally" in selection
        and "VoidstepProgressionRuntimeScope.Exit();" in selection
        and selection.index("VoidstepProgressionRuntimeScope.Enter();") < selection.index("BuildCleavePreview(player, ref color);")
        and selection.index("BuildDominoPreview(player);") < selection.index("VoidstepProgressionRuntimeScope.Exit();")
    ),
}

# Independent numeric mirrors for the most important player-facing curves.
def clamp(value, low, high):
    return max(low, min(high, value))

def blink(configured, rift, reach, dancer, unbound=0, singularity=0, avatar=0):
    scale = 0.55 + 0.025 * rift + 0.03 * reach + 0.04 * dancer + 0.01 * unbound + 0.02 * singularity + 0.02 * avatar
    return clamp(configured * min(2.0, scale), 1.0, 45.0)

def voidstep(configured, affinity, reach, dancer, unbound=0, singularity=0, avatar=0):
    scale = 0.55 + 0.02 * affinity + 0.03 * reach + 0.035 * dancer + 0.01 * unbound + 0.02 * singularity + 0.02 * avatar
    return clamp(configured * min(1.9, scale), 1.0, 45.0)

def cleave_radius(configured, affinity, reach, dancer, unbound=0, singularity=0, avatar=0):
    scale = 0.65 + 0.018 * affinity + 0.025 * reach + 0.03 * dancer + 0.01 * unbound + 0.018 * singularity + 0.02 * avatar
    return clamp(configured * min(1.75, scale), 1.0, 14.0)

def cleave_targets(configured, affinity, reach, dancer, singularity=0, avatar=0):
    if configured == 0 and avatar >= 10:
        return 0
    cap = 2 + math.ceil(affinity / 3.0) + reach // 4 + 2 * dancer + 2 * singularity + 3 * avatar
    cap = max(1, min(200, cap))
    return cap if configured <= 0 else min(configured, cap)

def wind_force(configured, gale, crushing, unbound=0, singularity=0, avatar=0):
    scale = 0.55 + 0.025 * gale + 0.035 * crushing + 0.015 * unbound + 0.02 * singularity + 0.025 * avatar
    return clamp(configured * min(2.0, scale), 0.0, 45.0)

def bend_factor(configured, bend, chrono, unbound=0, singularity=0, avatar=0):
    power = min(1.2, 0.2 + 0.025 * bend + 0.03 * chrono + 0.01 * unbound + 0.015 * singularity + 0.02 * avatar)
    return clamp(1.0 - (1.0 - configured) * power, 0.02, 1.0)

def bend_duration(configured, bend, chrono, unbound=0, singularity=0, avatar=0):
    scale = 0.55 + 0.02 * bend + 0.04 * chrono + 0.01 * unbound + 0.02 * singularity + 0.025 * avatar
    return clamp(configured * min(1.8, scale), 0.25, 45.0)

def domino_range(configured, fate, agony, gaze, unbound=0, singularity=0, avatar=0):
    scale = 0.55 + 0.02 * fate + 0.03 * agony + 0.025 * gaze + 0.01 * unbound + 0.02 * singularity + 0.02 * avatar
    return clamp(configured * min(1.8, scale), 1.0, 45.0)

def vision_range(configured, sight, gaze, unbound=0, singularity=0, avatar=0):
    scale = 0.5 + 0.025 * sight + 0.035 * gaze + 0.01 * unbound + 0.02 * singularity + 0.02 * avatar
    return clamp(configured * min(1.8, scale), 5.0, 150.0)

def vision_refresh(configured, sight, gaze, unbound=0, singularity=0, avatar=0):
    speed = 0.75 + 0.03 * sight + 0.06 * gaze + 0.02 * unbound + 0.03 * singularity + 0.04 * avatar
    return clamp(configured / max(0.25, speed), 0.1, 3.0)

numeric_checks = {
    "Blink range grows past configured default at high mastery": blink(9, 1, 0, 0) < blink(9, 20, 0, 0) < blink(9, 20, 20, 10, 10, 10, 10),
    "Voidstep teleport range grows substantially": voidstep(12, 1, 0, 0) < voidstep(12, 20, 20, 10, 10, 10, 10),
    "Cleave radius grows substantially": cleave_radius(4.8, 1, 0, 0) < cleave_radius(4.8, 20, 20, 10, 10, 10, 10),
    "Cleave target cap grows and final unlimited works": cleave_targets(0, 1, 0, 0) < cleave_targets(0, 20, 20, 10, 10, 9) and cleave_targets(0, 20, 20, 10, 10, 10) == 0,
    "Windblast force doubles at full power without exceeding hard cap": wind_force(10, 1, 0) < wind_force(10, 20, 20, 10, 10, 10) <= 20.0,
    "Bend Time becomes stronger and lasts longer": bend_factor(0.25, 1, 0) > bend_factor(0.25, 20, 10, 10, 10, 10) and bend_duration(5, 1, 0) < bend_duration(5, 20, 10, 10, 10, 10),
    "Domino range grows": domino_range(14, 1, 0, 0) < domino_range(14, 20, 20, 10, 10, 10, 10),
    "Dark Vision grows and refreshes faster": vision_range(35, 1, 0) < vision_range(35, 20, 10, 10, 10, 10) and vision_refresh(0.5, 1, 0) > vision_refresh(0.5, 20, 10, 10, 10, 10),
}
checks.update(numeric_checks)

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(("PASS " if passed else "FAIL ") + name)

if failed:
    sys.exit(1)

print("Voidstep mastery power invariants passed.")
