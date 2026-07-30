#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]


def read(relative):
    return (root / relative).read_text(encoding='utf-8')


def mask_csharp_noncode(source):
    """Mask C# comments and literals while preserving token spacing and newlines."""
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


bend_time = read('src/Voidstep/BendTimePlayerCompensationPatch.cs')
max_speed = read('src/Voidstep/BendTimeMaximumSpeedOwnership.cs')
domino_repair = read('src/Voidstep/DominoPlayerSourceRepairPatch.cs')
domino = read('src/Voidstep/DominoLinkService.cs')
tor_weapon = read('src/Voidstep/TorCleaveWeaponStateRepair.cs')
tor_presentation = read('src/Voidstep/TorVoidstepPresentation.cs')
tor_radial = read('src/Voidstep/TorRadialMenuRefreshPatch.cs')

bend_time_code = mask_csharp_noncode(bend_time)
max_speed_code = mask_csharp_noncode(max_speed)
domino_repair_code = mask_csharp_noncode(domino_repair)
tor_weapon_code = mask_csharp_noncode(tor_weapon)

checks = {
    'every TOR Voidstep proxy releases native targeting ownership':
        'var selectedAbility = selection?.SelectedAbility;' in tor_weapon and
        'state.TargetingReleased = true;' in tor_weapon and
        '__instance.CloseTargetingMode();' in tor_weapon and
        'selectedAbility.Value != AbilityId.VoidstepCleave' not in tor_weapon_code and
        'selectedAbility.Value == AbilityId.VoidstepCleave' not in tor_weapon_code,
    'TOR weapon restoration retries independently from targeting release':
        'if (!state.WeaponStateRestored)' in tor_weapon and
        'state.WeaponStateRestored = RestoreTorWeaponState(' in tor_weapon and
        'return false;' in tor_weapon and
        'retrying on a later tick' in tor_weapon,
    'Bend Time compensation runs after game-model stat calculation':
        'typeof(AgentDrivenProperties)' in bend_time and
        '"UpdateDrivenProperties"' in bend_time and
        '[HarmonyPriority(Priority.Last)]' in bend_time and
        'Agent __0' in bend_time and
        'private static bool Prepare()' in bend_time and
        'BendTimeDrivenPropertyCompensation.Apply(time, agent, __instance);' in bend_time,
    'Bend Time driven-property lookup is weakly mission cached':
        'WeakReference<Mission>' in bend_time and
        'WeakReference<TimeControlService>' in bend_time and
        'ResolveTime(mission)' in bend_time and
        'GetMissionBehavior<VoidstepMissionBehavior>()' in bend_time,
    'Bend Time republishes normal stats after local cleanup':
        '[HarmonyPatch(typeof(TimeControlService), "CompleteLocalState")]' in bend_time and
        'RefreshNative(__state.Player)' in bend_time and
        'RefreshNative(__state.Mount)' in bend_time and
        'ResetDiagnostics();' in bend_time,
    'Bend Time native maximum speed restores synchronously in multiplier mode':
        '[HarmonyPatch(typeof(TimeControlService), "CompleteLocalState")]' in max_speed and
        max_speed.count('SetMaximumSpeedLimit(state.Original') == 2 and
        max_speed.count('SetMaximumSpeedLimit(state.OriginalPlayerLimit, true)') == 1 and
        max_speed.count('SetMaximumSpeedLimit(state.OriginalMountLimit, true)') == 1 and
        'SetMaximumSpeedLimit(state.OriginalPlayerLimit, false)' not in max_speed_code and
        'SetMaximumSpeedLimit(state.OriginalMountLimit, false)' not in max_speed_code,
    'Bend Time covers movement combat ranged and mount properties':
        all(token in bend_time for token in (
            'MaxSpeedMultiplier',
            'CombatMaxSpeedMultiplier',
            'TopSpeedReachDuration',
            'SwingSpeedMultiplier',
            'ThrustOrRangedReadySpeedMultiplier',
            'ReloadSpeed',
            'BipedalRangedReadySpeedMultiplier',
            'BipedalRangedReloadSpeedMultiplier',
            'MountSpeed',
            'MountManeuver',
            'MountDashAccelerationMultiplier')),
    'Domino repairs only missing or inactive affectors':
        'if (__1 != null && __1.IsActive())' in domino_repair and
        '__2.OwnerId < 0' in domino_repair and
        'mission.FindAgentWithIndex(__2.OwnerId)' in domino_repair and
        'ReferenceEquals(owner, player)' in domino_repair and
        'ReferenceEquals(owner, mount)' in domino_repair and
        '__1 = owner;' in domino_repair and
        'affectorAgent = owner;' not in domino_repair_code,
    'Domino source repair preserves explicit recursion suppression':
        '_propagatedHitSuppression' in domino and
        'ConsumePropagatedHitSuppression' in domino and
        'RemoveUnconsumedPropagatedHitSuppression' in domino,
    'TOR presentation and radial diagnostics do not root finished missions':
        'WeakReference<Mission>' in tor_presentation and
        'WeakReference<Mission>' in tor_radial and
        'private static Mission _lastLoggedMission' not in tor_presentation and
        'private static Mission _lastMission' not in tor_radial,
    'TOR radial not-ready states remain non-exceptional':
        'private static bool ForceAdapterReattach' in tor_radial and
        'coordinator is not live yet' in tor_radial and
        'adapter is not live yet' in tor_radial and
        'return false;' in tor_radial,
}

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(('PASS' if passed else 'FAIL') + ': ' + name)
if failed:
    print('Failed runtime regression invariants: ' + ', '.join(failed), file=sys.stderr)
    raise SystemExit(1)
print(f'Validated {len(checks)} Blink, Bend Time, Domino and TOR regression invariants.')
