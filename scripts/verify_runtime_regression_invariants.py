#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]


def read(relative):
    path = root / relative
    return path.read_text(encoding='utf-8') if path.exists() else ''


time_control = read('src/Voidstep/TimeControlService.cs')
native_time = read('src/Voidstep/BendTimeNativeEnforcement.cs')
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
ability_manager = read('src/Voidstep/AbilityManager.cs')
cleave = read('src/Voidstep/CleaveSweepController.cs')
cast_animation = read('src/Voidstep/AbilityCastAnimationPatch.cs')
blink = read('src/Voidstep/BlinkController.cs')
facing_guard = read('src/Voidstep/CleaveFacingGuardPatches.cs')
camera_cast = read('src/Voidstep/CameraAuthoritativeCastPatches.cs')

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

    'Bend Time native guard excludes player and mount':
        'ReferenceEquals(agent, player) || ReferenceEquals(agent, mount)' in native_time and
        'ReferenceEquals(agent, _player) || ReferenceEquals(agent, _mount)' in time_control,

    'Bend Time native guard requires exact registered identity':
        'states.Contains(agent.Index)' in native_time and
        'ReferenceEquals(SlowStateAgentField.GetValue(slowState), agent)' in native_time and
        'ReferenceEquals(Mission.Current, mission)' in native_time,

    'Bend Time survives native property speed and action resets':
        '[HarmonyPatch(typeof(Agent), nameof(Agent.UpdateAgentProperties))]' in native_time and
        '[HarmonyPatch(typeof(Agent), nameof(Agent.SetMaximumSpeedLimit))]' in native_time and
        '[HarmonyPatch(typeof(Agent), nameof(Agent.SetCurrentActionSpeed))]' in native_time and
        'nameof(Agent.SetActionChannel)' in native_time,

    'Bend Time avoids presentation-wide mutation':
        '[ThreadStatic]' in native_time and
        'WeakReference<TimeControlService>' in native_time and
        'WeakReference<Mission>' in native_time and
        '[HarmonyPatch(typeof(AgentDrivenProperties)' not in native_time,

    'Bend Time cleanup restores owned movement caps':
        'OriginalMaximumSpeedLimits' in native_time and
        'RestoreOriginalMaximumSpeedLimits(service, state);' in native_time and
        'agent.SetMaximumSpeedLimit(original, false);' in native_time and
        'RestoreAndUntrack(__instance);' in native_time,

    'Bend Time still slows non-player missiles':
        '[HarmonyPatch(typeof(Mission), "AddMissileAux")]' in time_control and
        '[HarmonyPatch(typeof(Mission), "AddMissileSingleUsageAux")]' in time_control and
        'service?.ScaleMissile(shooterAgent, ref speed);' in time_control,

    'Blink and Cleave share one native position translator':
        'PreservedFrameTeleportRuntime.Teleport(' in post_cast and
        '[HarmonyPatch(typeof(AbilityManager), "TeleportActor")]' in post_cast and
        'nameof(BodyAlignedCleaveRuntime.TeleportPositionOnly)' in teleport and
        'PreservedFrameTeleportRuntime.Teleport(' in teleport and
        'return false;' in teleport,

    'teleport calls Bannerlord native IMBAgent.SetPosition':
        'AccessTools.Field(typeof(MBAPI), "IMBAgent")' in teleport and
        '"SetPosition"' in teleport and
        'NativeSetPositionMethod.Invoke(api, arguments);' in teleport,

    'teleport excludes initialization and convenience wrappers':
        'SetInitialFrame' not in teleport and
        '.TeleportToPosition(' not in teleport and
        'SetScriptedPosition' not in teleport,

    'mounted teleport keeps rider offset':
        'riderOffset = actorPosition - mountPosition;' in teleport and
        'riderTarget = destination + riderOffset;' in teleport and
        'SetNativePosition(mount, mountTarget)' in teleport and
        'SetNativePosition(actor, riderTarget)' in teleport and
        'riderOffsetError=' in teleport,

    'teleport never submits body or look orientation':
        'LookDirection =' not in teleport and
        'SetMovementDirection' not in teleport and
        'SetEventControlFlags' not in teleport and
        'GetCameraFacing' not in teleport and
        'GetAimDirection' not in teleport,

    'teleport preserves native callbacks':
        'NotifyTeleported(actor);' in teleport and
        'components[i]?.OnAgentTeleported();' in teleport,

    'teleport is one-shot and current-main-agent scoped':
        'ReferenceEquals(Mission.Current, mission)' in teleport and
        'ReferenceEquals(mission.MainAgent, actor)' in teleport and
        'PreservedFrameTeleportRuntime.Teleport(' not in post_cast.split('internal static void Tick(Mission mission)', 1)[1].split('internal static void Clear', 1)[0],

    'legacy camera-facing writes remain disabled':
        'CameraAlignmentUsesExactNativeFramePatch' in post_cast and
        'Suppress every legacy post-teleport camera-derived orientation write.' in post_cast and
        'Deliberately empty.' in post_cast,

    'Cleave origin and orientation are explicit':
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
        'ClampToCastCircle' in ground_aim and
        'MissionScreen projected reticle ground' in ground_aim,

    'TOR targeting and weapon ownership still release':
        'var selectedAbility = selection?.SelectedAbility;' in tor_weapon and
        'state.TargetingReleased = true;' in tor_weapon and
        '__instance.CloseTargetingMode();' in tor_weapon and
        'if (!state.WeaponStateRestored)' in tor_weapon,

    'TOR diagnostics retain weak mission lifetime':
        'WeakReference<Mission>' in tor_presentation and
        'WeakReference<Mission>' in tor_radial,

    'TOR proxy presentation cannot turn actors':
        'IsLookDirectionLocked' not in tor_stance and
        'LookDirection =' not in tor_stance and
        'SetMovementDirection' not in tor_stance,

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
print(f'Validated {len(checks)} native-time, position-only teleport, Domino, TOR and Cleave regressions.')
