#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]
policy_path = root / "src" / "Voidstep.Core" / "MasteryGraphPolicy.cs"
runtime_path = root / "src" / "Voidstep" / "VoidstepMasteryGraphPolicy.cs"
catalog_path = root / "src" / "Voidstep" / "VoidstepProgressionCatalog.cs"
behavior_path = root / "src" / "Voidstep" / "Progression" / "VoidstepProgressionBehavior.cs"

for path in (policy_path, runtime_path, catalog_path, behavior_path):
    if not path.is_file():
        print("FAIL missing mastery graph file:", path.relative_to(root))
        sys.exit(1)

policy = policy_path.read_text(encoding="utf-8")
runtime = runtime_path.read_text(encoding="utf-8")
catalog = catalog_path.read_text(encoding="utf-8")
behavior = behavior_path.read_text(encoding="utf-8")

foundation_tokens = (
    "case VoidAffinity:",
    "case RiftStep:",
    "case GaleForce:",
    "case BendTheHour:",
    "case FatefulLink:",
    "case UmbralSight:",
    "case DeepReservoir:",
)

stable_id_tokens = (
    "VoidAffinity = 0",
    "RiftStep = 1",
    "PhaseRecovery = 2",
    "MomentumWeave = 3",
    "VoidDancer = 4",
    "GaleForce = 5",
    "CrushingWave = 6",
    "BendTheHour = 7",
    "Chronomancer = 8",
    "FatefulLink = 9",
    "SharedAgony = 10",
    "UmbralSight = 11",
    "SovereignGaze = 12",
    "DeepReservoir = 13",
    "EfficientChanneling = 14",
    "RapidRecovery = 15",
    "UnboundPower = 16",
    "Singularity = 17",
    "AvatarOfTheVoid = 18",
)

checks = {
    "ability and reservoir foundations are independent": (
        all(token in policy for token in foundation_tokens)
        and "return Empty;" in policy
        and "ChronomancerRequirements = { R(BendTheHour, 5) }" in policy
        and "CrushingWaveRequirements = { R(GaleForce, 5) }" in policy
    ),
    "singularity converges shallow ability foundations": (
        "R(VoidAffinity, 1)" in policy
        and "R(RiftStep, 1)" in policy
        and "R(GaleForce, 1)" in policy
        and "R(BendTheHour, 1)" in policy
        and "R(FatefulLink, 1)" in policy
        and "R(UmbralSight, 1)" in policy
        and "R(SharedAgony, 5)" not in policy
    ),
    "avatar retains intentional final requirements": (
        "R(Singularity, 5)" in policy
        and "R(UnboundPower, 5)" in policy
    ),
    "runtime applies the graph before investment checks": (
        "VoidstepMasteryGraphRuntime.EnsureApplied();" in runtime
        and "private static bool Prepare()" in runtime
        and "HarmonyPatch(typeof(VoidstepProgressionBehavior), \"CanInvest\")" in runtime
        and "skill.Prerequisites = translated;" in runtime
        and "Array.Empty<VoidstepSkillRequirement>()" in runtime
    ),
    "runtime graph migration does not mutate saved progression": all(
        token not in runtime for token in (
            "_skillLevels",
            "_masteryXp",
            "Respec(",
            "SetSkillLevel",
            "AvailablePoints",
        )
    ),
    "existing save keys remain unchanged": all(
        token in behavior for token in (
            '_voidstepMasteryXp_v1',
            '_voidstepSkillLevels_v1',
            '_voidstepProgressionEnabled_v1',
            '_voidstepProgressionDataVersion',
        )
    ),
    "saved skill identifiers remain stable": all(token in catalog for token in stable_id_tokens),
    "runtime rejects accidental identifier remapping": (
        "MasteryGraphPolicy.SkillCount" in runtime
        and "changed without a save migration" in runtime
        and runtime.count("MasteryGraphPolicy.") >= 20
    ),
}

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(("PASS " if passed else "FAIL ") + name)

if failed:
    sys.exit(1)

print("Voidstep mastery graph and save-compatibility invariants passed.")
