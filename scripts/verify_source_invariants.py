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
native_time = read('BendTimeNativeEnforcement.cs')
post_cast = read('PostCastOrientationOwnershipFix.cs')
ground_aim = read('AuthoritativeGroundAimAndPlayerTimeFix.cs')
runtime_corrections = read('RuntimeGameplayCorrections.cs')
tor_stance = read('TorProxyCastStanceFix.cs')
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

checks = {
    'mission scoped behavior':
        'VoidstepMissionBehavior : MissionLogic' in mission and
        'CampaignBehaviorBase' not in all_text and 'CampaignEvents.' not in all_text,

    'mission lifecycle owns registered agents':
        'public override void OnAgentBuild(Agent agent, Banner banner)' in mission and
        'TimeControl?.RegisterAgent(agent);' in mission and
        mission.count('TimeControl?.UnregisterAgent(affectedAgent);') >= 2,

    'mission cleanup is explicit':
        'Cleanup(CancelReason.MissionEnded)' in mission and
        '_manager?.Cleanup(reason)' in mission and
        'public void Cleanup()' in time_control,

    'Bend Time leaves mission scene and controlled actor native':
        'AddTimeSpeedRequest' not in time_control and
        'RemoveTimeSpeedRequest' not in time_control and
        'TimeSpeedRequest' not in time_control and
        'scene, player and controlled mount remain native 1.00x' in time_control,

    'Bend Time base service is registered-agent bounded':
        'Dictionary<int, SlowState> _states' in time_control and
        'List<int> _refreshOrder' in time_control and
        'RefreshBudgetPerTick = 192' in time_control and
        'ReferenceEquals(agent, _player) || ReferenceEquals(agent, _mount)' in time_control,

    'native enforcement exact-matches the registered Agent instance':
        'states.Contains(agent.Index)' in native_time and
        'ReferenceEquals(SlowStateAgentField.GetValue(slowState), agent)' in native_time and
        'ReferenceEquals(agent, player) || ReferenceEquals(agent, mount)' in native_time and
        'ReferenceEquals(Mission.Current, mission)' in native_time and
        'ReferenceEquals(MissionField?.GetValue(service), mission)' in native_time,

    'native enforcement has a recursion bypass':
        '[ThreadStatic]' in native_time and
        'private static int _bypassDepth;' in native_time and
        'if (IsBypassed || agent == null || !agent.IsActive()' in native_time and
        'EnterBypass()' in native_time and 'ExitOneBypassLevel()' in native_time,

    'native enforcement follows agent property recalculation':
        '[HarmonyPatch(typeof(Agent), nameof(Agent.UpdateAgentProperties))]' in native_time and
        'EnforceAfterPropertyUpdate(__instance);' in native_time and
        'agent.UpdateCustomDrivenProperties();' in native_time,

    'native enforcement owns an absolute movement cap':
        '[HarmonyPatch(typeof(Agent), nameof(Agent.SetMaximumSpeedLimit))]' in native_time and
        'MaximumForwardUnlimitedSpeed' in native_time and
        'Math.Max(MinimumAbsoluteSpeed, baseline * factor)' in native_time and
        'agent.SetMaximumSpeedLimit(original, false);' in native_time,

    'native enforcement guards existing and new actions':
        '[HarmonyPatch(typeof(Agent), nameof(Agent.SetCurrentActionSpeed))]' in native_time and
        'nameof(Agent.SetActionChannel)' in native_time and
        'channel >= NativeActionChannelCount' in native_time and
        'speed = Math.Max(0.001f, speed * factor);' in native_time,

    'native enforcement restores and releases ownership first':
        'RestoreOriginalMaximumSpeedLimits(service, state);' in native_time and
        'RestoreAndUntrack(__instance);' in native_time and
        'OriginalMaximumSpeedLimits' in native_time and
        'RuntimeStates.Remove(service);' in native_time,

    'native enforcement does not root missions or agents':
        'WeakReference<TimeControlService>' in native_time and
        'WeakReference<Mission>' in native_time and
        'ConditionalWeakTable<TimeControlService, RuntimeState>' in native_time and
        not re.search(r'\bstatic\s+(?:readonly\s+)?Agent\s+\w+', native_time) and
        not re.search(r'\bstatic\s+readonly\s+.*(?:List<Agent>|HashSet<Agent>|Dictionary<int,\s*Agent>)', native_time),

    'no broad driven-property Harmony patch exists':
        '[HarmonyPatch(typeof(AgentDrivenProperties)' not in all_text and
        'BendTimePostCalculatedDrivenPropertiesPatch' not in all_text,

    'public native property push is used':
        'agent.UpdateAgentProperties();' in time_control + native_time and
        'agent.UpdateCustomDrivenProperties();' in time_control + native_time and
        'AgentDrivenProperties.Values' not in time_control + native_time,

    'Bend Time missile and real-duration ownership remains':
        '[HarmonyPatch(typeof(Mission), "AddMissileAux")]' in time_control and
        '[HarmonyPatch(typeof(Mission), "AddMissileSingleUsageAux")]' in time_control and
        'MBCommon.GetApplicationTime()' in time_control and '_remaining -= realDt;' in time_control,

    'native reticle projection remains authoritative':
        'GetProjectedMousePositionOnGround' in ground_aim and
        'ClampToCastCircle' in ground_aim and
        'MissionScreen projected reticle ground' in ground_aim,

    'Blink teleport owns position only':
        '[HarmonyPatch(typeof(AbilityManager), "TeleportActor")]' in post_cast and
        'actor.TeleportToPosition(position)' in post_cast and
        'mount.TeleportToPosition(position)' in post_cast and
        'SetInitialFrame' not in post_cast,

    'teleport code writes no orientation':
        'LookDirection =' not in post_cast and
        'SetMovementDirection' not in post_cast and
        'SetEventControlFlags' not in post_cast and
        'IsLookDirectionLocked' not in post_cast,

    'legacy post-teleport alignment is fully suppressed':
        'CameraAlignmentUsesExactNativeFramePatch' in post_cast and
        'Suppress every legacy post-teleport orientation write.' in post_cast and
        'internal static void AlignCurrent' in post_cast and
        'Deliberately empty.' in post_cast and
        'internal static void Tick(Mission mission)' in post_cast,

    'teleport is limited to the current main agent':
        'ReferenceEquals(mission.MainAgent, actor)' in post_cast and
        'ReferenceEquals(Mission.Current, mission)' in post_cast,

    'TOR proxy cleanup cannot mutate direction':
        'IsLookDirectionLocked' not in tor_stance and
        'LookDirection =' not in tor_stance and
        'SetMovementDirection' not in tor_stance and
        'if (requireLiveTargeting && state != 2)' in tor_stance,

    'Domino remains deferred and recursion safe':
        '_pending.Add(new PendingPropagation' in domino and
        'DispatchPendingPropagations();' in domino and
        '_propagatedHitSuppression' in domino and
        'Domino accepted authoritative damage callback' in runtime_corrections,

    'Blink frozen targeting cleanup remains':
        'AimTimeRequestId' in blink and '_timeCleanupPending' in blink and
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
        'InputConflictSuppression.Reset();' in submodule and 'UnpatchAll' in submodule,

    'no static Agent collection exists':
        not re.search(
            r'\bstatic\s+readonly\s+.*(?:\bList<Agent>\b|\bHashSet<Agent>\b|\bDictionary<int,\s*Agent>\b)',
            all_text),

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
print(f'Validated {len(checks)} mission-scope, native selective-time and position-only teleport invariants.')
