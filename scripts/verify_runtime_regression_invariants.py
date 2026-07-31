#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]


def read(relative):
    path = root / relative
    return path.read_text(encoding='utf-8') if path.exists() else ''


def exists(relative):
    return (root / relative).exists()


def mask_csharp_noncode(source):
    chars = list(source)
    masked = list(source)
    length = len(source)
    index = 0

    def blank(start, end):
        for position in range(start, end):
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
            blank(start, min(index, length))
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
            blank(start, min(index, length))
            continue

        index += 1

    return ''.join(masked)


time_control = read('src/Voidstep/TimeControlService.cs')
mission = read('src/Voidstep/VoidstepMissionBehavior.cs')
post_cast = read('src/Voidstep/PostCastOrientationOwnershipFix.cs')
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
all_runtime = '\n'.join(
    path.read_text(encoding='utf-8')
    for path in (root / 'src' / 'Voidstep').glob('*.cs'))
facing_guard_code = mask_csharp_noncode(facing_guard)

checks = {
    'obsolete global Bend Time hook files are removed':
        not exists('src/Voidstep/BendTimePlayerCompensationPatch.cs') and
        not exists('src/Voidstep/BendTimeMaximumSpeedOwnership.cs') and
        not exists('src/Voidstep/BendTimeMainAgentControllerInstaller.cs'),

    'Bend Time leaves scene and player at native time':
        'AddTimeSpeedRequest' not in time_control and
        'RemoveTimeSpeedRequest' not in time_control and
        'TimeSpeedRequest' not in time_control and
        'scene, player and controlled mount remain native 1.00x' in time_control,

    'Bend Time player and current mount are never slowed':
        'ReferenceEquals(agent, _player) || ReferenceEquals(agent, _mount)' in time_control and
        'if (_active && !IsExempt(agent))' in time_control and
        'if (!_active || speed <= 0f || IsExempt(shooter))' in time_control,

    'Bend Time non-player state is mission owned':
        'Dictionary<int, SlowState> _states' in time_control and
        'List<int> _refreshOrder' in time_control and
        'public override void OnAgentBuild(Agent agent, Banner banner)' in mission and
        'TimeControl?.RegisterAgent(agent);' in mission and
        mission.count('TimeControl?.UnregisterAgent(affectedAgent);') >= 2,

    'Bend Time avoids per-tick full mission scans':
        'RefreshBudgetPerTick = 192' in time_control and
        'RefreshBudgetedAgents();' in time_control and
        'AllAgents' not in time_control.split('public void Tick(float dt)', 1)[1].split('public void RegisterAgent', 1)[0],

    'Bend Time uses public driven-property refresh and push':
        'agent.UpdateAgentProperties();' in time_control and
        'agent.UpdateCustomDrivenProperties();' in time_control and
        'AgentDrivenProperties.Values' not in time_control and
        'PushDrivenPropertiesMethod' not in time_control,

    'Bend Time owns native movement and action slowdown':
        'agent.SetMaximumSpeedLimit(_factor, true);' in time_control and
        'NativeActionChannelCount = 2' in time_control and
        'agent.SetCurrentActionSpeed(channel, _factor);' in time_control and
        'agent.SetCurrentActionSpeed(channel, 1f);' in time_control,

    'Bend Time restores from current native agent models':
        'agent.UpdateAgentProperties();' in time_control.split('private void Restore(SlowState state)', 1)[1].split('private static void CaptureBaseline', 1)[0] and
        'Approximately(current, state.AppliedMaximumSpeedLimit)' in time_control and
        'state.PropertiesOwned = false;' in time_control,

    'Bend Time slows non-player missiles only at mission launch points':
        '[HarmonyPatch(typeof(Mission), "AddMissileAux")]' in time_control and
        '[HarmonyPatch(typeof(Mission), "AddMissileSingleUsageAux")]' in time_control and
        'service?.ScaleMissile(shooterAgent, ref speed);' in time_control,

    'no broad Agent presentation or action Harmony patch remains':
        '[HarmonyPatch(typeof(Agent), nameof(Agent.SetCurrentActionSpeed))]' not in all_runtime and
        '[HarmonyPatch(typeof(Agent), nameof(Agent.SetActionChannel))]' not in all_runtime and
        '[HarmonyPatch(typeof(AgentDrivenProperties)' not in all_runtime and
        'BendTimePostCalculatedDrivenPropertiesPatch' not in all_runtime,

    'teleport position and camera facing are submitted atomically':
        'CameraFacingTeleportOwnership.Teleport(' in post_cast and
        'SetInitialFrame(in mountPosition, in direction, true)' in post_cast and
        'SetInitialFrame(in riderPosition, in direction, true)' in post_cast and
        'SetInitialFrame(in actorPosition, in direction, true)' in post_cast and
        'TeleportToPosition' not in post_cast,

    'post-teleport frame is one-shot and duplicate guarded':
        'ConditionalWeakTable<Mission, State>' in post_cast and
        'DuplicateWindowSeconds = 0.08f' in post_cast and
        'IsImmediateDuplicate' in post_cast and
        'SetExactFrame(' not in post_cast.split('internal static void Tick(Mission mission)', 1)[1].split('internal static void Clear', 1)[0] and
        'repeated 360-degree turns' in post_cast,

    'post-teleport correction never injects movement controls':
        '.SetMovementDirection(' not in post_cast and
        '.MovementInputVector' not in post_cast and
        '.SetEventControlFlags(' not in post_cast,

    'post-teleport ownership cannot affect character preview agents':
        'ReferenceEquals(mission.MainAgent, actor)' in post_cast and
        'ReferenceEquals(Mission.Current, mission)' in post_cast and
        'CameraFacingTeleportOwnership.Clear' in post_cast and
        'ConditionalWeakTable<Mission, State>' in post_cast,

    'native projected cast marker remains authoritative':
        'GetProjectedMousePositionOnGround' in ground_aim and
        'ClampToCastCircle' in ground_aim and
        'MissionScreen projected reticle ground' in ground_aim,

    'every TOR Voidstep proxy releases native targeting ownership':
        'var selectedAbility = selection?.SelectedAbility;' in tor_weapon and
        'state.TargetingReleased = true;' in tor_weapon and
        '__instance.CloseTargetingMode();' in tor_weapon,

    'TOR weapon restoration retries independently from targeting release':
        'if (!state.WeaponStateRestored)' in tor_weapon and
        'state.WeaponStateRestored = RestoreTorWeaponState(' in tor_weapon and
        'retrying on a later tick' in tor_weapon,

    'TOR presentation diagnostics do not root finished missions':
        'WeakReference<Mission>' in tor_presentation and
        'WeakReference<Mission>' in tor_radial,

    'TOR proxy presentation cannot mutate facing':
        'IsLookDirectionLocked' not in tor_stance and
        'LookDirection =' not in tor_stance and
        'SetMovementDirection' not in tor_stance,

    'Domino repairs missing player source without weakening recursion guards':
        'mission.FindAgentWithIndex(__2.OwnerId)' in domino_repair and
        '__1 = owner;' in domino_repair and
        '_propagatedHitSuppression' in domino and
        'ConsumePropagatedHitSuppression' in domino and
        'Domino accepted authoritative damage callback' in runtime_corrections,

    'Blink and Cleave own presentation without generic cast turning':
        'var blinkOwnsItsPresentation = ability == AbilityId.Blink;' in cast_animation and
        'var cleaveOwnsExecutionAction = ability == AbilityId.VoidstepCleave;' in cast_animation and
        '__state = disablingDarkVision || blinkOwnsItsPresentation || cleaveOwnsExecutionAction;' in cast_animation,

    'Cleave snapshots mechanics before wind-up':
        'var snapshot = CleaveExecutionSnapshot.Capture(player, settings);' in ability_manager and
        '_cleaveSnapshot = snapshot;' in ability_manager and
        'public bool Begin(Agent actor, MissionWeapon weapon, CleaveExecutionSnapshot snapshot, out string failure)' in cleave,

    'Cleave native turn and attack controls remain suppressed':
        'SetEventControlFlags' not in facing_guard_code and
        'SetMovementDirection' not in facing_guard_code and
        'LookDirection =' not in facing_guard_code and
        'BodyAlignedCleaveActionSuppressionPatch' in facing_guard,

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
print(f'Validated {len(checks)} selective-time, teleport, Domino, TOR and Cleave regressions.')
