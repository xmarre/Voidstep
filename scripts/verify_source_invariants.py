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
teleport = read('PreservedFrameTeleportRuntime.cs')
tor_latch = read('TorProxySelectionAttemptLatch.cs')
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

    'Bend Time leaves scene and controlled actor native':
        'AddTimeSpeedRequest' not in time_control and
        'RemoveTimeSpeedRequest' not in time_control and
        'TimeSpeedRequest' not in time_control and
        'scene, player and controlled mount remain native 1.00x' in time_control,

    'Bend Time remains registered-agent bounded':
        'Dictionary<int, SlowState> _states' in time_control and
        'List<int> _refreshOrder' in time_control and
        'RefreshBudgetPerTick = 192' in time_control and
        'ReferenceEquals(agent, _player) || ReferenceEquals(agent, _mount)' in time_control,

    'native Bend Time exact-matches live mission agents':
        'states.Contains(agent.Index)' in native_time and
        'ReferenceEquals(SlowStateAgentField.GetValue(slowState), agent)' in native_time and
        'ReferenceEquals(agent, player) || ReferenceEquals(agent, mount)' in native_time and
        'ReferenceEquals(Mission.Current, mission)' in native_time,

    'native Bend Time has bounded recursion-safe enforcement':
        '[ThreadStatic]' in native_time and
        'private static int _bypassDepth;' in native_time and
        'ConditionalWeakTable<TimeControlService, RuntimeState>' in native_time and
        'WeakReference<TimeControlService>' in native_time and
        'WeakReference<Mission>' in native_time,

    'native Bend Time follows property speed and action resets':
        '[HarmonyPatch(typeof(Agent), nameof(Agent.UpdateAgentProperties))]' in native_time and
        '[HarmonyPatch(typeof(Agent), nameof(Agent.SetMaximumSpeedLimit))]' in native_time and
        '[HarmonyPatch(typeof(Agent), nameof(Agent.SetCurrentActionSpeed))]' in native_time and
        'nameof(Agent.SetActionChannel)' in native_time,

    'no broad driven-property patch exists':
        '[HarmonyPatch(typeof(AgentDrivenProperties)' not in all_text and
        'BendTimePostCalculatedDrivenPropertiesPatch' not in all_text,

    'Bend Time missile and real-duration ownership remains':
        '[HarmonyPatch(typeof(Mission), "AddMissileAux")]' in time_control and
        '[HarmonyPatch(typeof(Mission), "AddMissileSingleUsageAux")]' in time_control and
        'MBCommon.GetApplicationTime()' in time_control and
        '_remaining -= realDt;' in time_control,

    'native reticle projection remains authoritative':
        'GetProjectedMousePositionOnGround' in ground_aim and
        'ClampToCastCircle' in ground_aim and
        'MissionScreen projected reticle ground' in ground_aim,

    'Blink and Cleave share one position translator':
        '[HarmonyPatch(typeof(AbilityManager), "TeleportActor")]' in post_cast and
        'PreservedFrameTeleportRuntime.Teleport(' in post_cast and
        'nameof(BodyAlignedCleaveRuntime.TeleportPositionOnly)' in teleport and
        'PreservedFrameTeleportRuntime.Teleport(' in teleport and
        'return false;' in teleport,

    'teleport uses Bannerlord native position core':
        'AccessTools.Field(typeof(MBAPI), "IMBAgent")' in teleport and
        '"SetPosition"' in teleport and
        'typeof(UIntPtr)' in teleport and
        'typeof(Vec3).MakeByRefType()' in teleport and
        'NativeSetPositionMethod.Invoke(api, arguments);' in teleport,

    'teleport never uses initialization or convenience wrappers':
        'SetInitialFrame' not in teleport and
        '.TeleportToPosition(' not in teleport and
        'SetScriptedPosition' not in teleport,

    'mounted teleport preserves rigid rider offset':
        'riderOffset = actorPosition - mountPosition;' in teleport and
        'riderTarget = destination + riderOffset;' in teleport and
        'SetNativePosition(mount, mountTarget)' in teleport and
        'SetNativePosition(actor, riderTarget)' in teleport and
        'riderOffsetError=' in teleport,

    'teleport submits no orientation state':
        'LookDirection =' not in teleport and
        'SetMovementDirection' not in teleport and
        'SetEventControlFlags' not in teleport and
        'GetCameraFacing' not in teleport and
        'GetAimDirection' not in teleport and
        'destination - actor.Position' not in teleport,

    'teleport preserves native callbacks and bounded momentum cleanup':
        'NotifyTeleported(actor);' in teleport and
        'components[i]?.OnAgentTeleported();' in teleport and
        'actor.MovementInputVector = Vec2.Zero;' in teleport,

    'teleport is one-shot and current-main-agent scoped':
        'ReferenceEquals(Mission.Current, mission)' in teleport and
        'ReferenceEquals(mission.MainAgent, actor)' in teleport and
        'PreservedFrameTeleportRuntime.Teleport(' not in post_cast.split('internal static void Tick(Mission mission)', 1)[1].split('internal static void Clear', 1)[0],

    'legacy camera-derived alignment is suppressed':
        'CameraAlignmentUsesExactNativeFramePatch' in post_cast and
        'Suppress every legacy post-teleport camera-derived orientation write.' in post_cast and
        'internal static void AlignCurrent' in post_cast and
        'Deliberately empty.' in post_cast,

    'TOR proxy selection attempts once per targeting session':
        'ConditionalWeakTable<TorAbilityWheelAdapter, State>' in tor_latch and
        'torState != 2' in tor_latch and
        'state.Attempted && state.Ability == ability' in tor_latch and
        'ReferenceEquals(state.Proxy, proxy)' in tor_latch and
        'return false;' in tor_latch,

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
print(f'Validated {len(checks)} mission-scope, selective-time and native position-only teleport invariants.')
