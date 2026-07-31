#!/usr/bin/env python3
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
runtime = root / 'src' / 'Voidstep'
files = {p.name: p.read_text(encoding='utf-8') for p in runtime.glob('*.cs')}


def read(name):
    return files.get(name, '')


def mask_csharp_noncode(source):
    """Mask comments and literals while preserving token spacing and line structure."""
    chars = list(source)
    masked = list(source)
    length = len(source)
    index = 0

    def blank(start, end):
        for position in range(start, min(end, length)):
            if chars[position] not in ('\r', '\n'):
                masked[position] = ' '

    while index < length:
        if source.startswith('//', index):
            end = source.find('\n', index + 2)
            end = length if end < 0 else end
            blank(index, end)
            index = end
            continue
        if source.startswith('/*', index):
            end = source.find('*/', index + 2)
            end = length if end < 0 else end + 2
            blank(index, end)
            index = end
            continue

        prefix_length = 0
        verbatim = False
        if source.startswith('$@"', index) or source.startswith('@$"', index):
            prefix_length = 3
            verbatim = True
        elif source.startswith('@"', index):
            prefix_length = 2
            verbatim = True
        elif source.startswith('$"', index):
            prefix_length = 2
        elif source[index] == '"':
            prefix_length = 1

        if prefix_length:
            start = index
            index += prefix_length
            while index < length:
                if verbatim and source.startswith('""', index):
                    index += 2
                    continue
                if source[index] == '"':
                    index += 1
                    break
                if not verbatim and source[index] == '\\':
                    index += 2
                    continue
                index += 1
            blank(start, index)
            continue

        if source[index] == "'":
            start = index
            index += 1
            while index < length:
                if source[index] == '\\':
                    index += 2
                    continue
                if source[index] == "'":
                    index += 1
                    break
                index += 1
            blank(start, index)
            continue

        index += 1

    return ''.join(masked)


code_files = {name: mask_csharp_noncode(text) for name, text in files.items()}
all_text = '\n'.join(files.values())
all_code = '\n'.join(code_files.values())


def code_offenders(token):
    return sorted(name for name, text in code_files.items() if token in text)


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
        'CampaignBehaviorBase' not in all_code and 'CampaignEvents.' not in all_code,

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
        '[HarmonyPatch(typeof(Agent)' not in all_code and
        'nameof(Agent.SetActionChannel)' not in all_code and
        'BendTimeNativeEnforcement' not in all_code,

    'no global Agent singleton lookup exists':
        'Agent.Main' not in all_code,

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
        'SetInitialFrame' not in code_files.get('PreservedFrameTeleportRuntime.cs', '') and
        'TeleportToPosition' not in code_files.get('PreservedFrameTeleportRuntime.cs', '') and
        'IMBAgent' not in code_files.get('PreservedFrameTeleportRuntime.cs', '') and
        'SetPosition' not in code_files.get('PreservedFrameTeleportRuntime.cs', '') and
        'LookDirection =' not in code_files.get('PreservedFrameTeleportRuntime.cs', '') and
        'SetMovementDirection' not in code_files.get('PreservedFrameTeleportRuntime.cs', '') and
        'MovementInputVector' not in code_files.get('PreservedFrameTeleportRuntime.cs', '') and
        'SetActionChannel' not in code_files.get('PreservedFrameTeleportRuntime.cs', ''),

    'teleport is current-main-agent scoped':
        'ReferenceEquals(Mission.Current, mission)' in teleport and
        'ReferenceEquals(mission.MainAgent, actor)' in teleport,

    'Voidstep owns no Agent facing':
        'actor.LookDirection =' not in code_files.get('AnimationController.cs', '') and
        'actor.LookDirection =' not in code_files.get('CameraAuthoritativeCastPatches.cs', '') and
        'mount.LookDirection =' not in code_files.get('CameraAuthoritativeCastPatches.cs', '') and
        'SetMovementDirection' not in code_files.get('CameraAuthoritativeCastPatches.cs', '') and
        'LookDirection =' not in code_files.get('TorProxyCastStanceFix.cs', ''),

    'TOR integration is selection only':
        'selection-only' in tor_stance and
        'Agent.Main' not in code_files.get('TorProxyCastStanceFix.cs', '') and
        'SetActionChannel' not in code_files.get('TorProxyCastStanceFix.cs', '') and
        'ConditionalWeakTable<TorAbilityWheelAdapter, State>' in tor_latch,

    'Cleave remains camera oriented mathematically':
        'state.Facing = CameraAuthoritativeCastRuntime.GetCameraFacing' in camera_cast and
        'var facingAngle = AngleMath.NormalizeRadians' in camera_cast and
        'Math.Atan2(state.Facing.y, state.Facing.x)' in camera_cast,

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
        not re.search(
            r'\bstatic\s+readonly\s+.*(?:\bList<Agent>\b|\bHashSet<Agent>\b|\bDictionary<int,\s*Agent>\b)',
            all_code),

    'optional effects remain nonfatal':
        'Optional particle failed' in effects and 'return -1;' in effects,

    'dark vision reuses buffers':
        'List<int> _staleBuffer' in dark_vision,
}

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(('PASS' if passed else 'FAIL') + ': ' + name)

if 'no global Agent singleton lookup exists' in failed:
    print('Agent.Main executable offenders: ' + ', '.join(code_offenders('Agent.Main')), file=sys.stderr)
if 'no global Agent Harmony target exists' in failed:
    print('Agent Harmony executable offenders: ' + ', '.join(code_offenders('[HarmonyPatch(typeof(Agent)')), file=sys.stderr)
if 'Voidstep owns no Agent facing' in failed:
    print('LookDirection assignment offenders: ' + ', '.join(code_offenders('LookDirection =')), file=sys.stderr)
    print('MovementDirection offenders: ' + ', '.join(code_offenders('SetMovementDirection')), file=sys.stderr)

if failed:
    print('Failed invariants: ' + ', '.join(failed), file=sys.stderr)
    raise SystemExit(1)
print(f'Validated {len(checks)} mission-owned time and no-Agent-mutation invariants.')
