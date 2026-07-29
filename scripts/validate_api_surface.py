#!/usr/bin/env python3
import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from metadata_reader import CLR

REQUIRED = {
    "TaleWorlds.Library.dll": {
        "TaleWorlds.Library.MBCommon": {
            "GetApplicationTime": [[]],
        },
    },
    "TaleWorlds.MountAndBlade.dll": {
        "TaleWorlds.MountAndBlade.MBSubModuleBase": {
            "OnMissionBehaviorInitialize": [["TaleWorlds.MountAndBlade.Mission"]],
        },
        "TaleWorlds.MountAndBlade.MissionBehavior": {
            "EarlyStart": [[]],
            "OnMissionTick": [["float"]],
            "OnEndMission": [[]],
            "OnAgentHit": [["TaleWorlds.MountAndBlade.Agent", "TaleWorlds.MountAndBlade.Agent", "ref TaleWorlds.MountAndBlade.MissionWeapon", "ref TaleWorlds.MountAndBlade.Blow", "ref TaleWorlds.MountAndBlade.AttackCollisionData"]],
            "OnAgentRemoved": [["TaleWorlds.MountAndBlade.Agent", "TaleWorlds.MountAndBlade.Agent", "TaleWorlds.Core.AgentState", "TaleWorlds.MountAndBlade.KillingBlow"]],
            "OnAgentDeleted": [["TaleWorlds.MountAndBlade.Agent"]],
            "OnAgentControllerSetToPlayer": [["TaleWorlds.MountAndBlade.Agent"]],
        },
        "TaleWorlds.MountAndBlade.Mission": {
            "GetNearbyEnemyAgents": [["TaleWorlds.Library.Vec2", "float", "TaleWorlds.MountAndBlade.Team", "TaleWorlds.Library.MBList`1<TaleWorlds.MountAndBlade.Agent>"]],
            "GetNearbyAgents": [["TaleWorlds.Library.Vec2", "float", "TaleWorlds.Library.MBList`1<TaleWorlds.MountAndBlade.Agent>"]],
            "CreateMeleeBlow": [["TaleWorlds.MountAndBlade.Agent", "TaleWorlds.MountAndBlade.Agent", "ref TaleWorlds.MountAndBlade.AttackCollisionData", "ref TaleWorlds.MountAndBlade.MissionWeapon", "TaleWorlds.MountAndBlade.CrushThroughState", "TaleWorlds.Library.Vec3", "TaleWorlds.Library.Vec3", "bool"]],
            "AddTimeSpeedRequest": [["TaleWorlds.MountAndBlade.Mission+TimeSpeedRequest"]],
            "RemoveTimeSpeedRequest": [["int"]],
            "GetRequestedTimeSpeed": [["int", "ref float"]],
            "SetCustomCameraFovMultiplier": [["float"]],
            "FindAgentWithIndex": [["int"]],
        },
        "TaleWorlds.MountAndBlade.Agent": {
            "get_Main": [[]],
            "TeleportToPosition": [["TaleWorlds.Library.Vec3"]],
            "SetActionChannel": [["int", "ref TaleWorlds.MountAndBlade.ActionIndexCache", "bool", "TaleWorlds.MountAndBlade.AnimFlags", "float", "float", "float", "float", "float", "bool", "float", "int", "bool"]],
            "SetCurrentActionProgress": [["int", "float"]],
            "SetCurrentActionSpeed": [["int", "float"]],
            "RegisterBlow": None,
        },
        "TaleWorlds.MountAndBlade.AgentDrivenProperties": {
            "get_MaxSpeedMultiplier": None,
            "set_MaxSpeedMultiplier": None,
            "get_CombatMaxSpeedMultiplier": None,
            "set_CombatMaxSpeedMultiplier": None,
            "get_TopSpeedReachDuration": None,
            "set_TopSpeedReachDuration": None,
            "get_SwingSpeedMultiplier": None,
            "set_SwingSpeedMultiplier": None,
            "get_ThrustOrRangedReadySpeedMultiplier": None,
            "set_ThrustOrRangedReadySpeedMultiplier": None,
            "get_ReloadSpeed": None,
            "set_ReloadSpeed": None,
            "get_BipedalRangedReadySpeedMultiplier": None,
            "set_BipedalRangedReadySpeedMultiplier": None,
            "get_BipedalRangedReloadSpeedMultiplier": None,
            "set_BipedalRangedReloadSpeedMultiplier": None,
            "get_MountSpeed": None,
            "set_MountSpeed": None,
            "get_MountManeuver": None,
            "set_MountManeuver": None,
            "get_MountDashAccelerationMultiplier": None,
            "set_MountDashAccelerationMultiplier": None,
        },
        "TaleWorlds.MountAndBlade.AttackCollisionData": {
            "GetAttackCollisionDataForDebugPurpose": None,
        },
        "TaleWorlds.MountAndBlade.ActionIndexCache": {
            "Create": [["string"]],
            "get_Index": [[]],
        },
    },
    "TaleWorlds.Engine.dll": {
        "TaleWorlds.Engine.Scene": {
            "GetGroundHeightAtPosition": None,
            "GetWaterLevelAtPosition": [["TaleWorlds.Library.Vec2", "bool", "bool"]],
            "RayCastForClosestEntityOrTerrain": None,
            "GetNearestNavigationMeshForPosition": [["ref TaleWorlds.Library.Vec3", "float", "bool"]],
            "CreateBurstParticle": [["int", "TaleWorlds.Library.MatrixFrame"]],
        },
        "TaleWorlds.Engine.GameEntity": {
            "CreateEmpty": [["TaleWorlds.Engine.Scene", "bool", "bool", "bool"]],
            "SetContourColor": [["System.Nullable`1<uint>", "bool"]],
            "SetFactorColor": [["uint"]],
            "SetAlpha": [["float"]],
            "SetVisibilityExcludeParents": [["bool"]],
            "SetReadyToRender": [["bool"]],
            "set_EntityVisibilityFlags": [["TaleWorlds.Engine.EntityVisibilityFlags"]],
            "SetDoNotCheckVisibility": [["bool"]],
            "SetForceNotAffectedBySeason": [["bool"]],
            "AddMesh": None,
            "Remove": [["int"]],
        },
        "TaleWorlds.Engine.Mesh": {
            "GetFromResource": None,
            "CreateMeshWithMaterial": None,
            "GetMaterial": None,
            "set_Name": [["string"]],
            "set_Color": None,
            "set_Color2": None,
            "set_CullingMode": [["TaleWorlds.Engine.MBMeshCullingMode"]],
            "SetColorAndStroke": [["uint", "uint", "bool"]],
            "SetColorAlpha": [["uint"]],
            "SetMeshRenderOrder": [["int"]],
            "SetVisibilityMask": [["TaleWorlds.Engine.VisibilityMaskFlags"]],
            "SetAsNotEffectedBySeason": [[]],
            "LockEditDataWrite": None,
            "AddTriangle": None,
            "UnlockEditDataWrite": None,
            "ComputeNormals": None,
            "ComputeTangents": None,
            "RecomputeBoundingBox": None,
            "PreloadForRendering": None,
        },
        "TaleWorlds.Engine.ParticleSystem": {
            "CreateParticleSystemAttachedToEntity": None,
        },
        "TaleWorlds.Engine.MBMeshCullingMode": {
            "__fields__": ["None"],
        },
        "TaleWorlds.Engine.VisibilityMaskFlags": {
            "__fields__": ["Final"],
        },
        "TaleWorlds.Engine.EntityVisibilityFlags": {
            "__fields__": ["NoShadow"],
        },
    },
    "TaleWorlds.InputSystem.dll": {
        "TaleWorlds.InputSystem.InputKey": {
            "__fields__": ["Numpad1", "Numpad2", "Numpad3", "Numpad4", "Numpad5", "Numpad6", "Q", "RightMouseButton", "Escape"],
        },
        "TaleWorlds.InputSystem.IInputContext": {
            "IsKeyPressed": [["TaleWorlds.InputSystem.InputKey"]],
            "IsKeyDown": [["TaleWorlds.InputSystem.InputKey"]],
        },
        "TaleWorlds.InputSystem.Input": {
            "SetMousePosition": [["int", "int"]],
            "get_MousePositionPixel": [[]],
        },
    },
}

OPTIONAL = {
    "TOR_Core.dll": {
        "TOR_Core.AbilitySystem.Ability": {
            "get_StringID": [[]],
            "get_Template": [[]],
            "IsDisabled": [["TaleWorlds.MountAndBlade.Agent", "ref TaleWorlds.Localization.TextObject"]],
            "TryCast": [["TaleWorlds.MountAndBlade.Agent", "ref TaleWorlds.Localization.TextObject"]],
            "DoCast": [["TaleWorlds.MountAndBlade.Agent"]],
            "SetCrosshair": [["TOR_Core.AbilitySystem.Crosshairs.AbilityCrosshair"]],
        },
        "TOR_Core.AbilitySystem.Spells.Spell": {
            "IsDisabled": [["TaleWorlds.MountAndBlade.Agent", "ref TaleWorlds.Localization.TextObject"]],
            "DoCast": [["TaleWorlds.MountAndBlade.Agent"]],
        },
        "TOR_Core.AbilitySystem.AbilityComponent": {
            "get_KnownAbilitySystem": None,
            "get_CurrentAbility": [[]],
        },
        "TOR_Core.AbilitySystem.AbilityFactory": {
            "InitializeAbility": [["TOR_Core.AbilitySystem.AbilityTemplate", "TaleWorlds.MountAndBlade.Agent"]],
            "InitializeCrosshair": [["TOR_Core.AbilitySystem.AbilityTemplate"]],
        },
        "TOR_Core.AbilitySystem.AbilityManagerMissionLogic": {
            "get_CurrentState": [[]],
            "DisableAbilityMode": [["bool", "TaleWorlds.Localization.TextObject"]],
        },
        "TOR_Core.AbilitySystem.AbilityTemplate": {
            "set_StringID": [["string"]],
            "set_Name": [["string"]],
            "set_SpriteName": [["string"]],
            "set_TooltipDescription": [["string"]],
            "set_AbilityType": [["TOR_Core.AbilitySystem.AbilityType"]],
            "set_AbilityTargetType": [["TOR_Core.AbilitySystem.AbilityTargetType"]],
            "set_CrosshairType": [["TOR_Core.AbilitySystem.Crosshairs.CrosshairType"]],
            "set_CastType": [["TOR_Core.AbilitySystem.CastType"]],
            "set_CoolDown": [["int"]],
            "set_WindsOfMagicCost": [["int"]],
            "set_CastTime": [["float"]],
            "set_Duration": [["float"]],
            "set_Radius": [["float"]],
            "set_MinDistance": [["float"]],
            "set_MaxDistance": [["float"]],
            "set_MaxDistanceSpecified": [["bool"]],
            "set_TargetCapturingRadius": [["float"]],
            "set_BelongsToLoreID": [["string"]],
        },
        "TOR_Core.AbilitySystem.AbilityType": {
            "__fields__": ["Spell"],
        },
        "TOR_Core.AbilitySystem.AbilityTargetType": {
            "__fields__": ["WorldPosition"],
        },
        "TOR_Core.AbilitySystem.Crosshairs.CrosshairType": {
            "__fields__": ["Pointer"],
        },
        "TOR_Core.AbilitySystem.CastType": {
            "__fields__": ["Instant"],
        },
    },
}


def signatures(methods, name):
    return [[p["type"] for p in m["params"]] for m in methods if m["name"] == name]


def validate_assembly(path, assembly_name, types, failures):
    checks = 0
    clr = CLR(path)
    for type_name, method_map in types.items():
        dumped = clr.dump_type(type_name)
        if not dumped:
            failures.append(f"{assembly_name}: missing type {type_name}")
            continue
        methods = dumped[0]["methods"]
        fields = {field["name"] for field in dumped[0]["fields"]}
        for method_name, accepted in method_map.items():
            if method_name == "__fields__":
                for field_name in accepted:
                    checks += 1
                    if field_name not in fields:
                        failures.append(f"{assembly_name}: {type_name}.{field_name} field missing")
                continue
            actual = signatures(methods, method_name)
            checks += 1
            if not actual:
                failures.append(f"{assembly_name}: {type_name}.{method_name} missing")
            elif accepted is not None and not any(sig in accepted for sig in actual):
                failures.append(f"{assembly_name}: {type_name}.{method_name} signature mismatch: {actual}")
    return checks


def validate(root: Path):
    failures = []
    checks = 0
    for assembly_name, types in REQUIRED.items():
        path = root / assembly_name
        if not path.is_file():
            failures.append(f"missing required assembly: {path}")
            continue
        checks += validate_assembly(path, assembly_name, types, failures)

    optional_status = []
    for assembly_name, types in OPTIONAL.items():
        path = root / assembly_name
        if not path.is_file():
            optional_status.append(f"{assembly_name} absent; optional TOR integration checks skipped")
            continue
        checks += validate_assembly(path, assembly_name, types, failures)
        optional_status.append(f"{assembly_name} present; TOR integration API checked")

    if failures:
        for failure in failures:
            print("FAIL:", failure)
        raise SystemExit(1)
    for status in optional_status:
        print("NOTE:", status)
    print(f"Validated {checks} required Bannerlord 1.3.15 and available optional integration API surface checks.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("reference_root", type=Path)
    args = parser.parse_args()
    validate(args.reference_root.resolve())
