#!/usr/bin/env python3
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
fix_path = root / "src" / "Voidstep" / "VoidstepProgressionBoundaryFixes.cs"
patch_path = root / "src" / "Voidstep" / "VoidstepProgressionPatches.cs"
catalog_path = root / "src" / "Voidstep" / "VoidstepProgressionCatalog.cs"
runtime_path = root / "src" / "Voidstep" / "VoidstepProgressionRuntime.cs"
mission_path = root / "src" / "Voidstep" / "VoidstepMissionBehavior.cs"
submodule_path = root / "src" / "Voidstep" / "VoidstepSubModule.cs"

for path in (fix_path, patch_path, catalog_path, runtime_path, mission_path, submodule_path):
    if not path.is_file():
        print("FAIL missing mastery unlock file:", path.relative_to(root))
        sys.exit(1)

fix = fix_path.read_text(encoding="utf-8")
patches = patch_path.read_text(encoding="utf-8")
catalog = catalog_path.read_text(encoding="utf-8")
runtime = runtime_path.read_text(encoding="utf-8")
mission = mission_path.read_text(encoding="utf-8")
submodule = submodule_path.read_text(encoding="utf-8")

description_definitions = re.findall(
    r'D\(VoidstepSkillId\.[A-Za-z]+,\s*"[^"]+",\s*"[^"]+",\s*"[^"]+",.*?\n\s*"([^"]+)"',
    catalog,
    flags=re.DOTALL,
)

checks = {
    "mission boundary synchronizes the complete profile": all(token in fix for token in (
        "ProgressionMissionBoundarySynchronizationPatch",
        "[HarmonyPriority(Priority.First)]",
        "VoidstepProgressionBoundarySynchronizer.SynchronizeAll();",
        "foreach (var skill in VoidstepSkillCatalog.All)",
        "profile.Level(skill.Id) == behavior.GetSkillLevel(skill.Id)",
    )),
    "activation synchronizes the requested unlock before the existing gate": all(token in fix for token in (
        "ProgressionActivationBoundarySynchronizationPatch",
        "private static void Prefix(AbilityId ability)",
        "VoidstepProgressionBoundarySynchronizer.SynchronizeUnlock(ability);",
        "var required = VoidstepSkillCatalog.RequiredSkill(ability);",
        "profile.Level(required) != behavior.GetSkillLevel(required)",
    )) and "VoidstepProgressionService.Profile.CanUse(ability, out reason)" in patches,
    "profile rebuild occurs only when live state differs": (
        fix.count("VoidstepProgressionService.NotifyChanged();") == 4
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
    "runtime version literals match v1.2.2": (
        "Voidstep v1.2.2 active" in mission
        and "Voidstep v1.2.2 submodule loaded." in submodule
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
