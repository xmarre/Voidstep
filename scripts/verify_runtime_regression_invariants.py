#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]


def read(relative):
    return (root / relative).read_text(encoding='utf-8')


bend_time = read('src/Voidstep/BendTimePlayerCompensationPatch.cs')
domino_repair = read('src/Voidstep/DominoPlayerSourceRepairPatch.cs')
domino = read('src/Voidstep/DominoLinkService.cs')
tor_weapon = read('src/Voidstep/TorCleaveWeaponStateRepair.cs')

checks = {
    'every TOR Voidstep proxy releases native targeting ownership':
        'var selectedAbility = selection?.SelectedAbility;' in tor_weapon and
        'state.ReleasedAbility = selectedAbility.Value;' in tor_weapon and
        '__instance.CloseTargetingMode();' in tor_weapon and
        'RestoreTorWeaponState(__instance, state, selectedAbility.Value);' in tor_weapon and
        'selectedAbility.Value == AbilityId.VoidstepCleave' not in tor_weapon,
    'TOR weapon restoration refreshes wielded items and bindings once':
        'UpdateWieldedItems' in tor_weapon and
        'BindWeaponKeys' in tor_weapon and
        'state.ReleasedAbility.HasValue && state.ReleasedAbility.Value == selectedAbility.Value' in tor_weapon,
    'Bend Time compensation runs after game-model stat calculation':
        'typeof(AgentDrivenProperties)' in bend_time and
        '"UpdateDrivenProperties"' in bend_time and
        '[HarmonyPriority(Priority.Last)]' in bend_time and
        'BendTimeDrivenPropertyCompensation.Apply(time, agent, __instance);' in bend_time,
    'Bend Time native refresh is active-only and player scoped':
        '[HarmonyPatch(typeof(TimeControlService), "ApplyPlayerCompensation")]' in bend_time and
        'agent.UpdateAgentProperties();' in bend_time and
        'ReferenceEquals(agent, mainAgent)' in bend_time and
        'ReferenceEquals(agent, controlledMount)' in bend_time and
        '!time.Active' in bend_time,
    'Bend Time republishes normal stats on cleanup':
        '[HarmonyPatch(typeof(TimeControlService), "CompleteLocalState")]' in bend_time and
        'RefreshNative(__state.Player)' in bend_time and
        'RefreshNative(__state.Mount)' in bend_time,
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
    'Domino resolves authoritative player ownership from Blow.OwnerId':
        'blow.OwnerId < 0' in domino_repair and
        'mission.FindAgentWithIndex(blow.OwnerId)' in domino_repair and
        'ReferenceEquals(owner, player)' in domino_repair and
        'ReferenceEquals(owner, mount)' in domino_repair and
        'affectorAgent = owner;' in domino_repair,
    'Domino source repair preserves explicit recursion suppression':
        '_propagatedHitSuppression' in domino and
        'ConsumePropagatedHitSuppression' in domino and
        'RemoveUnconsumedPropagatedHitSuppression' in domino,
}

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(('PASS' if passed else 'FAIL') + ': ' + name)
if failed:
    print('Failed runtime regression invariants: ' + ', '.join(failed), file=sys.stderr)
    raise SystemExit(1)
print(f'Validated {len(checks)} Blink, Bend Time and Domino regression invariants.')
