#!/usr/bin/env python3
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
runtime = root / 'src' / 'Voidstep'
files = {p.name: p.read_text(encoding='utf-8') for p in runtime.glob('*.cs')}
all_text = '\n'.join(files.values())
time_control = files.get('TimeControlService.cs', '')
blink = files.get('BlinkController.cs', '')

time_release_guard = re.search(
    r'private bool TryCompleteRelease\(\)\s*\{.*?'
    r'if \(_mission\.GetRequestedTimeSpeed\(requestId, out requestedFactor\)\)\s*\{\s*'
    r'_mission\.RemoveTimeSpeedRequest\(requestId\);\s*'
    r'if \(_mission\.GetRequestedTimeSpeed\(requestId, out requestedFactor\)\)\s*return false;\s*\}.*?'
    r'if \(!_ownership\.Release\(token, out releasedRequestId\)\)\s*return false;.*?'
    r'_token = 0;',
    time_control,
    re.DOTALL)

blink_release_guard = re.search(
    r'private bool ReleaseAimTimeRequest\(\)\s*\{.*?'
    r'if \(_mission\.GetRequestedTimeSpeed\(AimTimeRequestId, out requestedFactor\)\)\s*\{\s*'
    r'_mission\.RemoveTimeSpeedRequest\(AimTimeRequestId\);\s*'
    r'if \(_mission\.GetRequestedTimeSpeed\(AimTimeRequestId, out requestedFactor\)\)\s*return false;\s*\}.*?'
    r'_ownsTimeRequest = false;\s*_timeCleanupPending = false;',
    blink,
    re.DOTALL)

checks = {
    'mission scoped behavior': 'VoidstepMissionBehavior : MissionLogic' in all_text,
    'late-added behavior initializes in EarlyStart': 'public override void EarlyStart()' in files.get('VoidstepMissionBehavior.cs','') and 'EnsureInitialized("EarlyStart")' in files.get('VoidstepMissionBehavior.cs','') and '_initializationAttempted' in files.get('VoidstepMissionBehavior.cs',''),
    'safe default controls': all(key in files.get('VoidstepSettings.cs','') for key in ('"Numpad1"', '"Numpad2"', '"Numpad3"', '"Numpad4"', '"Numpad5"', '"Numpad6"')) and 'RequireControlModifier { get; set; } = false;' in files.get('VoidstepSettings.cs',''),
    'legacy formation controls migrate': 'MigrateLegacyDefaultControls' in files.get('VoidstepSettings.cs','') and 'HasNumberRowConflict' in files.get('VoidstepSettings.cs',''),
    'per-cast hit registry': 'HitRegistry<int> _hits' in files.get('CleaveSweepController.cs',''),
    'cleave deterministic cleanup': '_hits.Clear();' in files.get('CleaveSweepController.cs','') and '_snapshotSchedule.Clear();' in files.get('CleaveSweepController.cs',''),
    'time request ownership retained through cleanup': '_cleanupPending' in time_control and '_ownership.TryGet(_token, out requestId)' in time_control and time_release_guard is not None and time_control.count('RemoveTimeSpeedRequest(') == 1,
    'blink request ownership retained through cleanup': '_timeCleanupPending' in blink and blink_release_guard is not None and blink.count('RemoveTimeSpeedRequest(') == 1,
    'domino index storage': 'Dictionary<int, Agent> _linked' in files.get('DominoLinkService.cs','') and 'FindAgentWithIndex' in files.get('DominoLinkService.cs',''),
    'domino recursion guard': 'RecursionGuard<int>' in files.get('DominoLinkService.cs',''),
    'dark vision throttled': 'DarkVisionRefreshInterval' in files.get('DarkVisionService.cs','') and '_refreshRemaining' in files.get('DarkVisionService.cs',''),
    'no campaign behavior': 'CampaignBehaviorBase' not in all_text and 'CampaignEvents.' not in all_text,
    'no global agent collection': not re.search(r'\bstatic\s+readonly\s+.*(?:\bAgent\b|\bList<Agent>\b|\bHashSet<Agent>\b)', all_text),
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
