#!/usr/bin/env python3
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
runtime = root / 'src' / 'Voidstep'
files = {p.name: p.read_text(encoding='utf-8') for p in runtime.glob('*.cs')}
all_text = '\n'.join(files.values())


def read(name):
    return files.get(name, '')


mission = read('VoidstepMissionBehavior.cs')
time_control = read('TimeControlService.cs')
post_cast = read('PostCastOrientationOwnershipFix.cs')
teleport = read('PreservedFrameTeleportRuntime.cs')
tor_stance = read('TorProxyCastStanceFix.cs')
tor_latch = read('TorProxySelectionAttemptLatch.cs')
animation = read('AnimationController.cs')
ground_aim = read('AuthoritativeGroundAimAndPlayerTimeFix.cs')
runtime_corrections = read('RuntimeGameplayCorrections.cs')
domino = read('DominoLinkService.cs')
input_bindings = read('VoidstepInputBindings.cs')
selection = read('AbilitySelectionController.cs')
wheel = read('AbilityWheelCoordinator.cs')
submodule = read('VoidstepSubModule.cs')
effects = read('EffectController.cs')
dark_vision = read('DarkVisionService.cs')
cleave = read('CleaveSweepController.cs')
ability_manager = read('AbilityManager.cs')
blink = read('BlinkController.cs')
camera_cast = read('CameraAuthoritativeCastPatches.cs')

checks = {
    'mission scoped behavior':
        'VoidstepMissionBehavior : MissionLogic' in mission and
        'CampaignBehaviorBase' not in all_text and 'CampaignEvents.' not in all_text,

    'mission lifecycle owns registered agents':
        'public override void OnAgentBuild(Agent agent, Banner banner)' in mission and
        'TimeControl?.RegisterAgent(agent);' in mission and
        mission.count('TimeControl?.UnregisterAgent(affectedAgent);') >= 2,

    'Bend Time late enforcement is mission owned':
        'public override void OnPreDisplayMissionTick(float dt)' in mission and
        'TimeControl?.LateTick();' in mission and
        'internal void LateTick()' in time_control and
        'ReferenceEquals(Mission.Current, _mission)' in time_control and
        'globalAgentPatches=0' in time_control,

    'no global Agent Harmony target exists':
        '[HarmonyPatch(typeof(Agent)' not in all_text and
        'nameof(Agent.SetActionChannel)' not in all_text and
        'BendTimeNativeEnforcement' not in all_text,

    'no global Agent singleton lookup exists':
        'Agent.Main' not in all_text,

    'Bend Time remains registered and bounded':
        'Dictionary<int, SlowState> _states' in time_control and
        'List<int> _refreshOrder' in time_control and
        'RefreshBudgetPerTick = 192' in time_control and
        'IsExempt(state.Agent)' in time_control,

    'Bend Time player and mount remain native':
        'AddTimeSpeedRequest' not in time_control and
        'RemoveTimeSpeedRequest' not in time_control and
        'scene, player and controlled mount remain native 1.00x' in time_control,

    'Bend Time uses owned public native writes only':
        'agent.UpdateAgentProperties();' in time_control and
        'agent.UpdateCustomDrivenProperties();' in time_control and
        'agent.SetMaximumSpeedLimit(' in time_control and
        'agent.SetCurrentActionSpeed(channel, _factor);' in time_control,

    'Bend Time missiles remain mission patched':
        '[HarmonyPatch(typeof(Mission), "AddMissileAux")]' in time_control and
        '[HarmonyPatch(typeof(Mission), "AddMissileSingleUsageAux")]' in time_control and
        'service?.ScaleMissile(shooterAgent, ref speed);' in time_control,

    'teleport displacement is explicitly suppressed':
        '[HarmonyPatch(typeof(AbilityManager), "TeleportActor")]' in post_cast and
        'PreservedFrameTeleportRuntime.Teleport(' in post_cast and
        'displacement suppressed to protect Agent orientation' in teleport and
        'return true;' in teleport,

    'teleport performs no Agent mutation':
        'SetInitialFrame' not in teleport and
        'TeleportToPosition' not in teleport and
        'IMBAgent' not in teleport and
        'SetPosition' not in teleport and
        'LookDirection =' not in teleport and
        'SetMovementDirection' not in teleport and
        'MovementInputVector' not in teleport and
        'SetActionChannel' not in teleport,

    'teleport is current-main-agent scoped':
        'ReferenceEquals(Mission.Current, mission)' in teleport and
        'ReferenceEquals(mission.MainAgent, actor)' in teleport,

    'Voidstep owns no Agent facing':
        'actor.LookDirection =' not in animation and
        'Voidstep never owns Agent body or look direction' in animation and
        'LookDirection =' not in tor_stance and
        'SetMovementDirection' not in tor_stance,

    'TOR integration is selection only':
        'selection-only' in tor_stance and
        'Agent.Main' not in tor_stance and
        'SetActionChannel' not in tor_stance and
        'ConditionalWeakTable<TorAbilityWheelAdapter, State>' in tor_latch,

    'Cleave remains camera oriented mathematically':
        'state.Facing = CameraAuthoritativeCastRuntime.GetCameraFacing' in camera_cast and
        'var facingAngle = AngleMath.NormalizeRadians(Math.Atan2(state.Facing.y, state.Facing.x));' in camera_cast,

    'native projected reticle remains authoritative':
        'GetProjectedMousePositionOnGround' in ground_aim and
        'ClampToCastCircle' in ground_aim,

    'Domino remains deferred and recursion safe':
        '_pending.Add(new PendingPropagation' in domino and
        'DispatchPendingPropagations();' in domino and
        '_propagatedHitSuppression' in domino and
        'Domino accepted authoritative damage callback' in runtime_corrections,

    'Blink frozen targeting cleanup remains':
        'AimTimeRequestId' in blink and
        '_timeCleanupPending' in blink and
        'RemoveTimeSpeedRequest' in blink,

    'Cleave retains captured weapon and bounded hits':
        'MissionWeapon _cleaveWeapon' in ability_manager and
        'MissionWeapon _weapon' in cleave and
        '_successfulHits >= _maximumTargets' in cleave,

    'selection and wheel ownership remain explicit':
        'var player = _mission.MainAgent;' in selection and
        'internal bool Confirm()' in selection and
        '_selection.Select(directAbility.Value, "configured direct selector")' in wheel and
        'Input.IsKeyPressed(InputKey.RightMouseButton)' in wheel and
        'internal static readonly AbilityId[] Abilities' in input_bindings,

    'hotkey and Harmony teardown remain explicit':
        'VoidstepHotKeyContext.Clear();' in submodule and
        'InputConflictSuppression.Reset();' in submodule and
        'UnpatchAll' in submodule,

    'no static Agent collection exists':
        not re.search(r'\bstatic\s+readonly\s+.*(?:\bList<Agent>\b|\bHashSet<Agent>\b|\bDictionary<int,\s*Agent>\b)', all_text),

    'optional effects remain nonfatal':
        'Optional particle failed' in effects and 'return -1;' in effects,

    'dark vision reuses buffers':
        'List<int> _staleBuffer' in dark_vision,
}

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(('PASS' if passed else 'FAIL') + ': ' + name)
if failed:
    print('Failed invariants: ' + ', '.join(failed), file=sys.stderr)
    raise SystemExit(1)
print(f'Validated {len(checks)} mission-owned time and no-Agent-mutation invariants.')
