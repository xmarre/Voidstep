#!/usr/bin/env python3
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
runtime = root / 'src' / 'Voidstep'
files = {p.name: p.read_text(encoding='utf-8') for p in runtime.glob('*.cs')}
all_text = '\n'.join(files.values())


def text(name):
    return files.get(name, '')


def method_body(source, signature):
    start = source.find(signature)
    if start < 0:
        return ''
    opening = source.find('{', start)
    if opening < 0:
        return ''
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == '{':
            depth += 1
        elif source[index] == '}':
            depth -= 1
            if depth == 0:
                return source[opening + 1:index]
    return ''


mission = text('VoidstepMissionBehavior.cs')
time_control = text('TimeControlService.cs')
post_cast = text('PostCastOrientationOwnershipFix.cs')
ground_aim = text('AuthoritativeGroundAimAndPlayerTimeFix.cs')
camera = text('CameraAuthoritativeCastPatches.cs')
runtime_corrections = text('RuntimeGameplayCorrections.cs')
tor_stance = text('TorProxyCastStanceFix.cs')
domino = text('DominoLinkService.cs')
input_bindings = text('VoidstepInputBindings.cs')
selection = text('AbilitySelectionController.cs')
wheel = text('AbilityWheelCoordinator.cs')
submodule = text('VoidstepSubModule.cs')
effects = text('EffectController.cs')
dark_vision = text('DarkVisionService.cs')
cleave = text('CleaveSweepController.cs')
ability_manager = text('AbilityManager.cs')
blink = text('BlinkController.cs')

time_tick = method_body(time_control, 'public void Tick(float dt)')
time_begin = method_body(time_control, 'public bool Begin(')
time_release = method_body(time_control, 'public void Release()')
teleport_owner_tick = method_body(post_cast, 'internal static void Tick(Mission mission)')

checks = {
    'mission scoped behavior':
        'VoidstepMissionBehavior : MissionLogic' in mission and
        'CampaignBehaviorBase' not in all_text and
        'CampaignEvents.' not in all_text,

    'mission lifecycle owns agent registration':
        'public override void OnAgentBuild(Agent agent, Banner banner)' in mission and
        'TimeControl?.RegisterAgent(agent);' in mission and
        'TimeControl?.UnregisterAgent(affectedAgent);' in mission,

    'mission cleanup is explicit':
        'Cleanup(CancelReason.MissionEnded)' in mission and
        '_manager?.Cleanup(reason)' in mission and
        'public void Cleanup()' in time_control,

    'no global player action interception':
        '[HarmonyPatch(typeof(Agent), nameof(Agent.SetCurrentActionSpeed))]' not in all_text and
        '[HarmonyPatch(typeof(Agent), nameof(Agent.SetActionChannel))]' not in all_text,

    'no global driven-property interception':
        'typeof(AgentDrivenProperties),\n                "UpdateDrivenProperties"' not in all_text and
        'BendTimePostCalculatedDrivenPropertiesPatch' not in all_text,

    'Bend Time never changes mission scene time':
        'AddTimeSpeedRequest' not in time_control and
        'RemoveTimeSpeedRequest' not in time_control and
        'TimeSpeedRequest' not in time_control and
        'scene and controlled player remain native 1.00x' in time_control,

    'Bend Time explicitly exempts player and mount':
        'ReferenceEquals(agent, _player) || ReferenceEquals(agent, _mount)' in time_control and
        'if (_active && !IsExempt(agent))' in time_control and
        'if (agent == null || !agent.IsActive() || IsExempt(agent))' in time_control,

    'Bend Time slows only registered non-player agents':
        'Dictionary<int, SlowState> _states' in time_control and
        'List<int> _refreshOrder' in time_control and
        'RefreshBudgetPerTick = 128' in time_control and
        'RefreshBudgetedAgents();' in time_tick and
        'AllAgents' not in time_tick,

    'Bend Time owns verified action channels only':
        'NativeActionChannelCount = 2' in time_control and
        'channel < NativeActionChannelCount' in time_control and
        'agent.SetCurrentActionSpeed(channel, _factor);' in time_control and
        'agent.SetCurrentActionSpeed(channel, 1f);' in time_control,

    'Bend Time restores only owned driven values':
        'OriginalValues' in time_control and
        'AppliedValues' in time_control and
        'Approximately(current[i], state.AppliedValues[i])' in time_control and
        'current[i] = state.OriginalValues[i];' in time_control and
        'PushDrivenProperties(agent, driven);' in time_release + time_control,

    'Bend Time slows non-player missiles at launch':
        '[HarmonyPatch(typeof(Mission), "AddMissileAux")]' in time_control and
        '[HarmonyPatch(typeof(Mission), "AddMissileSingleUsageAux")]' in time_control and
        'if (!_active || speed <= 0f || IsExempt(shooter))' in time_control and
        'speed * _factor' in time_control,

    'Bend Time duration uses real application time':
        'MBCommon.GetApplicationTime()' in time_control and
        '_remaining -= realDt;' in time_tick,

    'native reticle projection remains authoritative':
        'GetProjectedMousePositionOnGround' in ground_aim and
        'ClampToCastCircle' in ground_aim and
        'MissionScreen projected reticle ground' in ground_aim,

    'teleport position and direction are atomic':
        'SetInitialFrame(in mountPosition, in direction, true)' in post_cast and
        'SetInitialFrame(in riderPosition, in direction, true)' in post_cast and
        'SetInitialFrame(in actorPosition, in direction, true)' in post_cast and
        'TeleportToPosition' not in post_cast,

    'teleport ownership is bounded and mission scoped':
        'ConditionalWeakTable<Mission, State>' in post_cast and
        'HoldSeconds = 0.55f' in post_cast and
        'CameraReleaseDotThreshold = 0.82f' in post_cast and
        'MBCommon.GetApplicationTime() >= state.ExpiresAt' in teleport_owner_tick and
        'deliberate camera turn' in teleport_owner_tick,

    'teleport correction never submits movement controls':
        'SetMovementDirection' not in post_cast and
        'MovementInputVector' not in post_cast and
        'SetEventControlFlags' not in post_cast,

    'teleport ownership mutates only live mission main agent':
        'var actor = mission.MainAgent;' in teleport_owner_tick and
        'actor.Index != state.ActorIndex' in teleport_owner_tick and
        'CameraFacingTeleportOwnership.Clear' in post_cast,

    'TOR proxy cleanup cannot mutate direction':
        'IsLookDirectionLocked' not in tor_stance and
        'LookDirection =' not in tor_stance and
        'SetMovementDirection' not in tor_stance and
        'if (requireLiveTargeting && state != 2)' in tor_stance,

    'Domino callback remains deferred and recursion safe':
        '_pending.Add(new PendingPropagation' in domino and
        'DispatchPendingPropagations();' in domino and
        '_propagatedHitSuppression' in domino and
        '_propagatedDeathSuppression' in domino and
        'Domino accepted authoritative damage callback' in runtime_corrections,

    'Blink owns frozen targeting request cleanup':
        'AimTimeRequestId' in blink and
        '_timeCleanupPending' in blink and
        'RemoveTimeSpeedRequest' in blink,

    'Cleave uses captured weapon and bounded target scheduling':
        'MissionWeapon _cleaveWeapon' in ability_manager and
        'MissionWeapon _weapon' in cleave and
        '_successfulHits >= _maximumTargets' in cleave,

    'selection validates player before mutation':
        'var player = _mission.MainAgent;' in selection and
        'player.Health <= 0f' in selection and
        'internal bool Confirm()' in selection,

    'wheel and direct bindings remain separate':
        '_selection.Select(directAbility.Value, "configured direct selector")' in wheel and
        'Input.IsKeyPressed(InputKey.RightMouseButton)' in wheel and
        'VoidstepInputBindings.Abilities' in input_bindings,

    'hotkey and Harmony teardown remain explicit':
        'VoidstepHotKeyContext.Clear();' in submodule and
        'InputConflictSuppression.Reset();' in submodule and
        'UnpatchAll' in submodule,

    'no static Agent collection':
        not re.search(
            r'\bstatic\s+readonly\s+.*(?:\bList<Agent>\b|\bHashSet<Agent>\b|\bDictionary<int,\s*Agent>\b)',
            all_text),

    'missing optional effects remain nonfatal':
        'Optional particle failed' in effects and
        'return -1;' in effects,

    'dark vision reuses buffers':
        'List<int> _staleBuffer' in dark_vision and
        'new List<int>' not in method_body(dark_vision, 'private void Refresh()'),
}

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(('PASS' if passed else 'FAIL') + ': ' + name)

if failed:
    print('Failed invariants: ' + ', '.join(failed), file=sys.stderr)
    raise SystemExit(1)

print(f'Validated {len(checks)} mission-scope, selective-time and teleport invariants.')
