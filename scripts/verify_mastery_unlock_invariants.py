#!/usr/bin/env python3
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
fix_path = root / "src" / "Voidstep" / "VoidstepProgressionBoundaryFixes.cs"
patch_path = root / "src" / "Voidstep" / "VoidstepProgressionPatches.cs"
context_path = root / "src" / "Voidstep" / "AbilityContext.cs"
catalog_path = root / "src" / "Voidstep" / "VoidstepProgressionCatalog.cs"
runtime_path = root / "src" / "Voidstep" / "VoidstepProgressionRuntime.cs"
mission_path = root / "src" / "Voidstep" / "VoidstepMissionBehavior.cs"
submodule_path = root / "src" / "Voidstep" / "VoidstepSubModule.cs"
standalone_path = root / "src" / "Voidstep" / "StandaloneAbilityWheel.cs"
wheel_suppression_path = root / "src" / "Voidstep" / "AbilityWheelInputSuppressionPatch.cs"

for path in (
    fix_path,
    patch_path,
    context_path,
    catalog_path,
    runtime_path,
    mission_path,
    submodule_path,
    standalone_path,
    wheel_suppression_path,
):
    if not path.is_file():
        print("FAIL missing mastery unlock file:", path.relative_to(root))
        sys.exit(1)

fix = fix_path.read_text(encoding="utf-8")
patches = patch_path.read_text(encoding="utf-8")
context = context_path.read_text(encoding="utf-8")
catalog = catalog_path.read_text(encoding="utf-8")
runtime = runtime_path.read_text(encoding="utf-8")
mission = mission_path.read_text(encoding="utf-8")
submodule = submodule_path.read_text(encoding="utf-8")
standalone = standalone_path.read_text(encoding="utf-8")
wheel_suppression = wheel_suppression_path.read_text(encoding="utf-8")


def extract_named_block(source, declaration):
    start = source.find(declaration)
    if start < 0:
        return None
    opening = source.find("{", start + len(declaration))
    if opening < 0:
        return None
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1:index]
    return None


description_definitions = re.findall(
    r'D\(VoidstepSkillId\.[A-Za-z]+,\s*"[^"]+",\s*"[^"]+",\s*"[^"]+",.*?\n\s*"([^"]+)"',
    catalog,
    flags=re.DOTALL,
)

activation_class = extract_named_block(
    patches,
    "internal static class ProgressionAbilityActivationPatch",
)
context_sync = "VoidstepProgressionBoundarySynchronizer.SynchronizeAll();"
activation_sync = "VoidstepProgressionBoundarySynchronizer.SynchronizeUnlock(ability);"
scope_enter = "VoidstepProgressionRuntimeScope.Enter();"
scope_exit = "VoidstepProgressionRuntimeScope.Exit();"
profile_gate = "VoidstepProgressionService.Profile.CanUse(ability, out reason)"

checks = {
    "mission construction synchronizes before entering the settings scope": (
        context_sync in context
        and scope_enter in context
        and scope_exit in context
        and context.index(context_sync) < context.index(scope_enter)
        and "finally" in context
        and context.index("finally") < context.index(scope_exit)
        and "foreach (var skill in VoidstepSkillCatalog.All)" in fix
        and "profile.Level(skill.Id) == behavior.GetSkillLevel(skill.Id)" in fix
    ),
    "AbilityContext uses direct owned scoping instead of a Harmony constructor patch": (
        "ProgressionAbilityContextScopePatch" not in patches
        and "HarmonyPatch(typeof(AbilityContext)" not in patches
        and "older shipped Harmony" in context
    ),
    "activation synchronizes the requested unlock immediately before the gate": (
        activation_class is not None
        and activation_sync in activation_class
        and profile_gate in activation_class
        and activation_class.index(activation_sync) < activation_class.index(profile_gate)
        and "var required = VoidstepSkillCatalog.RequiredSkill(ability);" in fix
        and "profile.Level(required) != behavior.GetSkillLevel(required)" in fix
    ),
    "shared enabled-state synchronization is centralized": (
        "private static bool TryGetEnabledState(" in fix
        and fix.count("TryGetEnabledState(out behavior, out profile)") == 2
        and fix.count("if (profile.Enabled != behavior.Enabled)") == 1
        and fix.count("behavior = VoidstepProgressionService.Current;") == 1
    ),
    "no independent Harmony ordering dependency remains": (
        "HarmonyPatch" not in fix
        and "HarmonyPriority" not in fix
        and "ProgressionMissionBoundarySynchronizationPatch" not in fix
        and "ProgressionActivationBoundarySynchronizationPatch" not in fix
    ),
    "profile rebuild occurs only when live state differs": (
        fix.count("VoidstepProgressionService.NotifyChanged();") == 3
        and "if (profile.Enabled != behavior.Enabled)" in fix
        and "if (profile.Level(required) != behavior.GetSkillLevel(required))" in fix
    ),
    "Void Affinity rank one is the Voidstep Cleave gate": (
        "case AbilityId.VoidstepCleave: return VoidstepSkillId.VoidAffinity;" in catalog
        and "if (Level(required) > 0)" in runtime
    ),
    "catalog contains exactly nineteen canonical mastery descriptions": len(description_definitions) == 19,
    "Blink description is direct gameplay text": (
        '"Unlocks Blink. Increases Blink teleport range."' in catalog
    ),
    "mastery descriptions contain no comparative implementation commentary": all(
        phrase not in "\n".join(description_definitions).lower()
        for phrase in (
            "instead of merely",
            "rather than replacing",
            "the tree's primary reward",
            "meta",
        )
    ),
    "temporary description mutation path is absent": (
        "VoidstepMasteryDescriptions" not in fix
        and "ProgressionMasteryDescriptionPatch" not in fix
    ),
    "standalone wheel remains display-only and owns no mission input": (
        "display-only Gauntlet layer" in standalone
        and all(
            token not in standalone
            for token in (
                "IsFocusLayer",
                "ConfigureInputRestrictions",
                "SetInputRestrictions",
                "TrySetFocus",
                "TryLoseFocus",
                "InputUsageMask",
            )
        )
        and all(
            token not in wheel_suppression
            for token in (
                "MouseScrollUp",
                "MouseScrollDown",
                "MouseScrollAxis",
            )
        )
    ),
    "runtime version literals match v1.2.3": (
        "Voidstep v1.2.3 active" in mission
        and "Voidstep v1.2.3 submodule loaded." in submodule
        and "Voidstep v1.1.0 active" not in mission
        and "Voidstep v1.2.0 submodule loaded." not in submodule
    ),
}

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(("PASS " if passed else "FAIL ") + name)

if failed:
    sys.exit(1)

print("Voidstep mastery unlock invariants passed.")
