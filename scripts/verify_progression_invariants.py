#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]
runtime = root / "src" / "Voidstep"
behavior_path = runtime / "Progression" / "VoidstepProgressionBehavior.cs"
profile_path = runtime / "VoidstepProgressionRuntime.cs"
patches_path = runtime / "VoidstepProgressionPatches.cs"
settings_path = runtime / "VoidstepProgressionSettings.cs"
catalog_path = runtime / "VoidstepProgressionCatalog.cs"
submodule_path = runtime / "VoidstepSubModule.cs"
viewmodels_path = runtime / "VoidstepMasteryViewModels.cs"
character_button_controller_path = runtime / "VoidstepCharacterScreenButton.cs"
standalone_wheel_path = runtime / "StandaloneAbilityWheel.cs"
button_path = root / "module" / "Voidstep" / "GUI" / "Prefabs" / "VoidstepCharacterButton.xml"
mastery_path = root / "module" / "Voidstep" / "GUI" / "Prefabs" / "VoidstepMastery.xml"
build_path = root / "build.ps1"
ci_build_path = root / "scripts" / "build-ci.ps1"

required_paths = (
    behavior_path,
    profile_path,
    patches_path,
    settings_path,
    catalog_path,
    submodule_path,
    viewmodels_path,
    character_button_controller_path,
    standalone_wheel_path,
    button_path,
    mastery_path,
)

missing = [str(path.relative_to(root)) for path in required_paths if not path.is_file()]
if missing:
    for item in missing:
        print("FAIL missing progression file:", item)
    sys.exit(1)

behavior = behavior_path.read_text(encoding="utf-8")
profile = profile_path.read_text(encoding="utf-8")
patches = patches_path.read_text(encoding="utf-8")
settings = settings_path.read_text(encoding="utf-8")
catalog = catalog_path.read_text(encoding="utf-8")
submodule = submodule_path.read_text(encoding="utf-8")
viewmodels = viewmodels_path.read_text(encoding="utf-8")
character_button_controller = character_button_controller_path.read_text(encoding="utf-8")
standalone_wheel = standalone_wheel_path.read_text(encoding="utf-8")
button = button_path.read_text(encoding="utf-8")
mastery = mastery_path.read_text(encoding="utf-8")
build = build_path.read_text(encoding="utf-8")
ci_build = ci_build_path.read_text(encoding="utf-8")

ability_gate_tokens = (
    "AbilityId.VoidstepCleave",
    "AbilityId.Blink",
    "AbilityId.Windblast",
    "AbilityId.BendTime",
    "AbilityId.Domino",
    "AbilityId.DarkVision",
)
branch_tokens = (
    '"Core"',
    '"Mobility"',
    '"Force"',
    '"Dominion"',
    '"Reservoir"',
    '"Convergence"',
)
prefab_bindings = (
    "{MobilityNodes}",
    "{ForceNodes}",
    "{CoreNodes}",
    "{DominionNodes}",
    "{ReservoirNodes}",
    "{ConvergenceNodes}",
    "@SelectedName",
    "ExecuteConfirm",
    "ExecuteRespec",
    "ExecuteToggleProgression",
)
reachable_capstone_tokens = (
    "R(VoidstepSkillId.BendTheHour, 5)",
    "R(VoidstepSkillId.UmbralSight, 5)",
    "R(VoidstepSkillId.RapidRecovery, 5)",
    "R(VoidstepSkillId.VoidDancer, 1)",
    "R(VoidstepSkillId.Chronomancer, 1)",
    "R(VoidstepSkillId.SharedAgony, 5)",
    "R(VoidstepSkillId.SovereignGaze, 1)",
    'D(VoidstepSkillId.AvatarOfTheVoid, "Avatar of the Void", "Convergence", "✺", 10, 80, 225, 2',
)

checks = {
    "campaign persistence is save scoped": (
        "VoidstepProgressionBehavior : CampaignBehaviorBase" in behavior
        and "public override void SyncData(IDataStore dataStore)" in behavior
        and "_voidstepMasteryXp_v1" in behavior
        and "_voidstepSkillLevels_v1" in behavior
    ),
    "campaign behavior has no periodic tick listener": (
        "TickEvent" not in behavior
        and "HourlyTick" not in behavior
        and "DailyTick" not in behavior
        and "OnMissionTick" not in behavior
    ),
    "campaign persistence stores no mission agents": (
        "Agent " not in behavior
        and "List<Agent" not in behavior
        and "Dictionary<int, Agent" not in behavior
    ),
    "runtime profile is immutable and cached": (
        "private readonly int[] _levels" in profile
        and "private static volatile VoidstepProgressionProfile _profile" in behavior
        and "internal static VoidstepProgressionProfile Profile => _profile;" in behavior
        and "VoidstepProgressionProfile.Build" in behavior
    ),
    "runtime profile is rebuilt only by lifecycle mutations": (
        behavior.count("VoidstepProgressionProfile.Build") == 2
        and "NotifyChanged()" in behavior
        and "Attach(VoidstepProgressionBehavior behavior)" in behavior
        and "Detach()" in behavior
    ),
    "all six abilities have explicit mastery gates": all(token in catalog for token in ability_gate_tokens)
        and "CanUse(AbilityId ability" in profile
        and "ProgressionAbilityActivationPatch" in patches,
    "disabled progression preserves configured values": (
        "if (!Enabled)" in profile
        and "return true;" in profile
        and profile.count("if (!Enabled) return configured;") == 6
        and "VoidstepProgressionProfile.Disabled" in behavior
    ),
    "runtime setting interception is scope bounded and allocation free": (
        "[ThreadStatic]" in patches
        and "VoidstepProgressionRuntimeScope.Active" in patches
        and "VoidstepProgressionRuntimeScope.Enter();" in patches
        and "VoidstepProgressionRuntimeScope.Exit();" in patches
        and "new Lease" not in patches
        and "ProgressionAbilityTickScopePatch" in patches
        and "ProgressionAbilityContextScopePatch" in patches
    ),
    "runtime scope exits exactly through three finalizers": patches.count("private static Exception Finalizer") == 3,
    "xp awards are bounded and weak mission lifetime scoped": (
        "ConditionalWeakTable<object, AwardState>" in patches
        and "CreateValueCallback StateFactory" in patches
        and "States.GetValue(owner, StateFactory)" in patches
        and "minimumIntervalSeconds" in patches
        and "SuccessfulHits" in patches
        and all(name in patches for name in ("ConfirmBlink", "CastWindblast", "CastBendTime", "CastDomino", "CastDarkVision"))
    ),
    "mastery catalogue contains nineteen skills": catalog.count("D(VoidstepSkillId.") == 19,
    "mastery catalogue contains all branches": all(token in catalog for token in branch_tokens),
    "final capstone is reachable inside the rank-99 budget": all(token in catalog for token in reachable_capstone_tokens)
        and 78 + 10 <= 99,
    "progression has a separate MCM entry": (
        'Id => "Voidstep_Progression_v1"' in settings
        and "Enable Mastery Progression" in settings
        and "Mastery XP Multiplier" in settings
    ),
    "character screen transition closes native state first": (
        "CharacterScreenOpenPhase.CloseCharacterState" in submodule
        and "Game.Current.GameStateManager.PopState();" in submodule
        and "CharacterScreenOpenPhase.WaitForCampaignMap" in submodule
        and "CharacterScreenOpenPhase.SettleCampaignMap" in submodule
        and "_pendingCharacterScreenOpenFrames = 2;" in submodule
    ),
    "mastery screen is map and mission gated": (
        "Campaign.Current == null || Mission.Current != null" in submodule
        and "IsCampaignMapScreen" in submodule
        and "ScreenManager.PushScreen(new VoidstepMasteryScreen())" in submodule
    ),
    "mastery screen is closed before progression teardown": (
        submodule.count("CloseMasteryScreen();") == 2
        and "private static void CloseMasteryScreen()" in submodule
        and "if (ScreenManager.TopScreen is VoidstepMasteryScreen)" in submodule
        and "ScreenManager.PopScreen();" in submodule
    ),
    "mastery XP console command rejects disabled progression": (
        "if (!progression.Enabled) return \"Enable Voidstep mastery progression before awarding XP.\";" in submodule
    ),
    "shortcut avoids Guided Arrow control-U": (
        "InputKey.V" in submodule
        and "InputKey.LeftShift" in submodule
        and "InputKey.LeftControl" in submodule
        and "InputKey.U" not in submodule
    ),
    "character button avoids Guided Arrow button position": (
        'MarginBottom="164"' in button
        and "ExecuteOpenMastery" in button
    ),
    "character button owns mouse buttons only": (
        "SetInputRestrictions(true, InputUsageMask.MouseButtons);" in character_button_controller
        and "SetInputRestrictions();" not in character_button_controller
    ),
    "standalone wheel remains display only in this independent gate": (
        "display-only Gauntlet layer" in standalone_wheel
        and all(token not in standalone_wheel for token in (
            "IsFocusLayer",
            "ConfigureInputRestrictions",
            "SetInputRestrictions",
            "TrySetFocus",
            "TryLoseFocus",
            "InputUsageMask",
        ))
    ),
    "mastery prefab binds every branch and action": all(token in mastery for token in prefab_bindings),
    "mastery header status is read only": mastery.count('Command.Click="ExecuteToggleProgression"') == 1,
    "mastery view model has no unbound XP property": "XpProgress" not in viewmodels and "@XpProgress" not in mastery,
    "view model unsubscribes from progression events": (
        "VoidstepProgressionService.Changed += RefreshAll" in viewmodels
        and "VoidstepProgressionService.Changed -= RefreshAll" in viewmodels
    ),
    "packages require both mastery prefabs": all(
        name in build and name in ci_build
        for name in ("VoidstepCharacterButton.xml", "VoidstepMastery.xml")
    ),
}

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(("PASS " if passed else "FAIL ") + name)

if failed:
    sys.exit(1)

print("Voidstep progression invariants passed.")
