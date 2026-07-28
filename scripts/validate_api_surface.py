#!/usr/bin/env python3
import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from metadata_reader import CLR

REQUIRED = {
    "TaleWorlds.MountAndBlade.dll": {
        "TaleWorlds.MountAndBlade.MBSubModuleBase": {
            "OnMissionBehaviorInitialize": [["TaleWorlds.MountAndBlade.Mission"]],
        },
        "TaleWorlds.MountAndBlade.MissionBehavior": {
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
            "TeleportToPosition": [["TaleWorlds.Library.Vec3"]],
            "SetActionChannel": [["int", "ref TaleWorlds.MountAndBlade.ActionIndexCache", "bool", "TaleWorlds.MountAndBlade.AnimFlags", "float", "float", "float", "float", "float", "bool", "float", "int", "bool"]],
            "SetCurrentActionProgress": [["int", "float"]],
            "SetCurrentActionSpeed": [["int", "float"]],
            "RegisterBlow": None,
        },
        "TaleWorlds.MountAndBlade.AttackCollisionData": {
            "GetAttackCollisionDataForDebugPurpose": None,
        },
        "TaleWorlds.MountAndBlade.ActionIndexCache": {
            "__fields__": ["act_strike_bent_over"],
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
            "Remove": [["int"]],
        },
    },
    "TaleWorlds.InputSystem.dll": {
        "TaleWorlds.InputSystem.IInputContext": {
            "IsKeyPressed": [["TaleWorlds.InputSystem.InputKey"]],
            "IsKeyDown": [["TaleWorlds.InputSystem.InputKey"]],
        },
    },
}

def signatures(methods, name):
    return [[p["type"] for p in m["params"]] for m in methods if m["name"] == name]

def validate(root: Path):
    failures = []
    checks = 0
    for assembly_name, types in REQUIRED.items():
        path = root / assembly_name
        if not path.is_file():
            failures.append(f"missing assembly: {path}")
            continue
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
    if failures:
        for failure in failures:
            print("FAIL:", failure)
        raise SystemExit(1)
    print(f"Validated {checks} Bannerlord 1.3.15 API surface checks.")

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("reference_root", type=Path)
    args = parser.parse_args()
    validate(args.reference_root.resolve())
