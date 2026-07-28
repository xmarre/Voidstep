#!/usr/bin/env python3
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
runtime = root / 'src' / 'Voidstep'
files = {p.name: p.read_text(encoding='utf-8') for p in runtime.glob('*.cs')}
all_text = '\n'.join(files.values())
checks = {
    'mission scoped behavior': 'VoidstepMissionBehavior : MissionLogic' in all_text,
    'per-cast hit registry': 'HitRegistry<int> _hits' in files.get('CleaveSweepController.cs',''),
    'cleave deterministic cleanup': '_hits.Clear();' in files.get('CleaveSweepController.cs','') and '_snapshotProgress.Clear();' in files.get('CleaveSweepController.cs',''),
    'time request ownership': 'OwnershipLedger<int>' in files.get('TimeControlService.cs','') and 'RemoveTimeSpeedRequest(requestId)' in files.get('TimeControlService.cs',''),
    'domino index storage': 'HashSet<int> _linked' in files.get('DominoLinkService.cs','') and 'FindAgentWithIndex' in files.get('DominoLinkService.cs',''),
    'domino recursion guard': 'RecursionGuard<int>' in files.get('DominoLinkService.cs',''),
    'dark vision throttled': 'DarkVisionRefreshInterval' in files.get('DarkVisionService.cs','') and '_refreshRemaining' in files.get('DarkVisionService.cs',''),
    'no campaign behavior': 'CampaignBehaviorBase' not in all_text and 'CampaignEvents.' not in all_text,
    'no global agent collection': not re.search(r'static\s+readonly\s+.*(?:Agent|List<Agent>|HashSet<Agent>)', all_text),
    'no full all-agent scan': 'AllAgents' not in files.get('VoidstepMissionBehavior.cs','') and 'Agents)' not in files.get('VoidstepMissionBehavior.cs',''),
    'mission end cleanup': 'Cleanup(CancelReason.MissionEnded)' in files.get('VoidstepMissionBehavior.cs',''),
    'missing effects are nonfatal': 'Optional particle failed' in files.get('EffectController.cs','') and 'return -1;' in files.get('EffectController.cs',''),
    'verified animation field only': 'ActionIndexCache.act_strike_bent_over' in files.get('AnimationController.cs','') and 'ActionIndexCache.Create(' not in files.get('AnimationController.cs',''),
    'whole-cast target cap': 'TryRegister(target.Index, _maximumTargets)' in files.get('CleaveSweepController.cs',''),
    'dark vision reuses stale buffer': 'List<int> _staleBuffer' in files.get('DarkVisionService.cs','') and 'new List<int>' not in files.get('DarkVisionService.cs','').split('private void Refresh()',1)[-1],
    'domino reuses snapshot buffer': 'List<int> _snapshotBuffer' in files.get('DominoLinkService.cs','') and 'new List<int>' not in files.get('DominoLinkService.cs','').split('public void Tick()',1)[-1],
}
failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ': ' + name)
if failed:
    print('Failed invariants: ' + ', '.join(failed), file=sys.stderr)
    raise SystemExit(1)
print(f'Validated {len(checks)} source invariants.')
