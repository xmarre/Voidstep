#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]


def read(relative):
    path = root / relative
    return path.read_text(encoding='utf-8') if path.exists() else ''


time_control = read('src/Voidstep/TimeControlService.cs')
mission = read('src/Voidstep/VoidstepMissionBehavior.cs')
post_cast = read('src/Voidstep/PostCastOrientationOwnershipFix.cs')
teleport = read('src/Voidstep/PreservedFrameTeleportRuntime.cs')
tor_latch = read('src/Voidstep/TorProxySelectionAttemptLatch.cs')
ground_aim = read('src/Voidstep/AuthoritativeGroundAimAndPlayerTimeFix.cs')
runtime_corrections = read('src/Voidstep/RuntimeGameplayCorrections.cs')
domino_repair = read('src/Voidstep/DominoPlayerSourceRepairPatch.cs')
domino = read('src/Voidstep/DominoLinkService.cs')
tor_weapon = read('src/Voidstep/TorCleaveWeaponStateRepair.cs')
tor_presentation = read('src/Voidstep/TorVoidstepPresentation.cs')
tor_radial = read('src/Voidstep/TorRadialMenuRefreshPatch.cs')
tor_stance = read('src/Voidstep/TorProxyCastStanceFix.cs')
animation = read('src/Voidstep/AnimationController.cs')
ability_manager = read('src/Voidstep/AbilityManager.cs')
cleave = read('src/Voidstep/CleaveSweepController.cs')
cast_animation = read('src/Voidstep/AbilityCastAnimationPatch.cs')
blink = read('src/Voidstep/BlinkController.cs')
facing_guard = read('src/Voidstep/CleaveFacingGuardPatches.cs')
camera_cast = read('src/Voidstep/CameraAuthoritativeCastPatches.cs')
all_runtime = '\n'.join(path.read_text(encoding='utf-8') for path in (root / 'src' / 'Voidstep').glob('*.cs'))

checks = {
    'Bend Time leaves scene and player at native time':
        'AddTimeSpeedRequest' not in time_control and
        'RemoveTimeSpeedRequest' not in time_control and
        'scene, player and controlled mount remain native 1.00x' in time_control,

    'Bend Time mission state remains registered and bounded':
        'Dictionary<int, SlowState> _states' in time_control and
        'RefreshBudgetPerTick = 192' in time_control and
        'public override void OnAgentBuild(Agent agent, Banner banner)' in mission and
        'TimeControl?.RegisterAgent(agent);' in mission,

    'Bend Time late reset handling is mission owned':
        'public override void OnPreDisplayMissionTick(float dt)' in mission and
        'TimeControl?.LateTick();' in mission and
        'internal void LateTick()' in time_control and
        'ReferenceEquals(Mission.Current, _mission)' in time_control and
        'globalAgentPatches=0' in time_control,

    'no global Agent Harmony patches remain':
        '[HarmonyPatch(typeof(Agent)' not in all_runtime and
        'BendTimeNativeEnforcement' not in all_runtime and
        'nameof(Agent.SetActionChannel)' not in all_runtime,

    'Bend Time excludes controlled player and mount':
        'ReferenceEquals(agent, _player) || ReferenceEquals(agent, _mount)' in time_control,

    'Bend Time cleanup restores owned state':
        'agent.UpdateAgentProperties();' in time_control and
        'agent.SetCurrentActionSpeed(channel, 1f);' in time_control and
        'agent.SetMaximumSpeedLimit(state.OriginalMaximumSpeedLimit, false);' in time_control,

    'Bend Time still slows non-player missiles':
        '[HarmonyPatch(typeof(Mission), "AddMissileAux")]' in time_control and
        '[HarmonyPatch(typeof(Mission), "AddMissileSingleUsageAux")]' in time_control and
        'service?.ScaleMissile(shooterAgent, ref speed);' in time_control,

    'Blink and Cleave share the no-displacement boundary':
        'PreservedFrameTeleportRuntime.Teleport(' in post_cast and
        '[HarmonyPatch(typeof(AbilityManager), "TeleportActor")]' in post_cast and
        'nameof(BodyAlignedCleaveRuntime.TeleportPositionOnly)' in teleport and
        'PreservedFrameTeleportRuntime.Teleport(' in teleport and
        'return false;' in teleport,

    'teleport displacement is explicitly suppressed':
        'displacement suppressed to protect Agent orientation' in teleport and
        'requestedDestination=' in teleport and
        'livePosition=' in teleport,

    'teleport performs no native Agent mutation':
        'SetInitialFrame' not in teleport and
        'TeleportToPosition' not in teleport and
        'IMBAgent' not in teleport and
        'SetPosition' not in teleport and
        'LookDirection =' not in teleport and
        'SetMovementDirection' not in teleport and
        'SetEventControlFlags' not in teleport and
        'MovementInputVector' not in teleport,

    'teleport is one-shot and current-main-agent scoped':
        'ReferenceEquals(Mission.Current, mission)' in teleport and
        'ReferenceEquals(mission.MainAgent, actor)' in teleport,

    'no global Agent singleton path remains':
        'Agent.Main' not in all_runtime,

    'TOR proxy integration is selection only':
        'selection-only' in tor_stance and
        'SetActionChannel' not in tor_stance and
        'SetCurrentActionSpeed' not in tor_stance,

    'Voidstep direct facing methods are inert':
        'actor.LookDirection =' not in animation and
        'Voidstep never owns Agent body or look direction' in animation,

    'Cleave origin and orientation remain explicit':
        'state.Facing = CameraAuthoritativeCastRuntime.GetCameraFacing' in camera_cast and
        'var facingAngle = AngleMath.NormalizeRadians(Math.Atan2(state.Facing.y, state.Facing.x));' in camera_cast and
        'actor.Position' in camera_cast,

    'TOR failed proxy selection cannot retry every tick':
        'ConditionalWeakTable<TorAbilityWheelAdapter, State>' in tor_latch and
        'torState != 2' in tor_latch and
        'state.Attempted && state.Ability == ability' in tor_latch and
        'ReferenceEquals(state.Proxy, proxy)' in tor_latch and
        'return false;' in tor_latch,

    'native projected cast marker remains authoritative':
        'GetProjectedMousePositionOnGround' in ground_aim and
        'ClampToCastCircle' in ground_aim,

    'TOR targeting and weapon ownership still release':
        'var selectedAbility = selection?.SelectedAbility;' in tor_weapon and
        'state.TargetingReleased = true;' in tor_weapon and
        '__instance.CloseTargetingMode();' in tor_weapon and
        'if (!state.WeaponStateRestored)' in tor_weapon,

    'TOR diagnostics retain weak mission lifetime':
        'WeakReference<Mission>' in tor_presentation and
        'WeakReference<Mission>' in tor_radial,

    'Domino repair remains authoritative and recursion safe':
        'mission.FindAgentWithIndex(__2.OwnerId)' in domino_repair and
        '__1 = owner;' in domino_repair and
        '_propagatedHitSuppression' in domino and
        'ConsumePropagatedHitSuppression' in domino and
        'Domino accepted authoritative damage callback' in runtime_corrections,

    'Blink and Cleave bypass generic turning animations':
        'var blinkOwnsItsPresentation = ability == AbilityId.Blink;' in cast_animation and
        'var cleaveOwnsExecutionAction = ability == AbilityId.VoidstepCleave;' in cast_animation,

    'Cleave mechanics remain snapshot based':
        'var snapshot = CleaveExecutionSnapshot.Capture(player, settings);' in ability_manager and
        '_cleaveSnapshot = snapshot;' in ability_manager and
        'public bool Begin(Agent actor, MissionWeapon weapon, CleaveExecutionSnapshot snapshot, out string failure)' in cleave,

    'Cleave control-facing writes remain suppressed':
        'SetEventControlFlags' not in facing_guard and
        'BodyAlignedCleaveActionSuppressionPatch' in facing_guard and
        'BodyAlignedCleaveVectorFacingSuppressionPatch' in facing_guard,

    'Blink frozen targeting request remains separately owned':
        '_timeCleanupPending' in blink and
        'RemoveTimeSpeedRequest' in blink and
        '_hud.Show(_ownsTimeRequest' in blink,
}

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(('PASS' if passed else 'FAIL') + ': ' + name)
if failed:
    print('Failed runtime regression invariants: ' + ', '.join(failed), file=sys.stderr)
    raise SystemExit(1)
print(f'Validated {len(checks)} mission-only time, no-displacement teleport, Domino, TOR and Cleave regressions.')
