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
ability_manager = files.get('AbilityManager.cs', '')
cleave = files.get('CleaveSweepController.cs', '')
submodule = files.get('VoidstepSubModule.cs', '')
mission_behavior = files.get('VoidstepMissionBehavior.cs', '')
input_router = files.get('InputRouter.cs', '')
input_bindings = files.get('VoidstepInputBindings.cs', '')
mission_order_suppression = files.get('MissionOrderInputSuppression.cs', '')
hotkey_context = files.get('VoidstepHotKeyContext.cs', '')
settings = files.get('VoidstepSettings.cs', '')
weapon_validation = files.get('WeaponValidation.cs', '')
dark_vision = files.get('DarkVisionService.cs', '')
blow_factory = files.get('BlowFactory.cs', '')
effects = files.get('EffectController.cs', '')
targeting = files.get('TargetingService.cs', '')
teleport_validator = files.get('TeleportValidator.cs', '')
windblast = files.get('WindblastController.cs', '')
domino = files.get('DominoLinkService.cs', '')
mirror_tests = (root / 'scripts' / 'run_logic_mirror_tests.py').read_text(encoding='utf-8')

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

uncapped_cleave_schedule = re.search(
    r'SweepPlanner\.BuildSchedule\(\s*_candidates,\s*_startAngle,\s*_sweepRadians,\s*_direction,\s*_radius,\s*0,\s*_schedule\);',
    cleave,
    re.DOTALL)

duplicate_passthrough = re.search(
    r'internal static bool IsChordActiveForKey\(InputKey inputKey\).*?'
    r'AmbiguousChords\.Contains\(ChordCode\(entry\.Modifiers, inputKey\)\)\)\s*continue;',
    input_bindings,
    re.DOTALL)

hot_path_order = re.search(
    r'internal static bool ShouldSuppress\(InputKey inputKey\).*?'
    r'LatchedKeys\.ContainsKey\(inputKey\).*?'
    r'IsBoundPrimaryKey\(inputKey\).*?'
    r'RuntimeCanSuppress\(\)',
    input_bindings,
    re.DOTALL)

checks = {
    'mission scoped behavior': 'VoidstepMissionBehavior : MissionLogic' in all_text,
    'late-added behavior initializes in EarlyStart': 'public override void EarlyStart()' in mission_behavior and 'EnsureInitialized("EarlyStart")' in mission_behavior,
    'native serialized hotkeys shown in options': 'AuxiliarySerializedAndShownInOptions' in hotkey_context and hotkey_context.count('RegisterHotKey(new HotKey(') == 6 and 'HotKeyManager.RegisterContext(context, false, true)' in hotkey_context,
    'native hotkey localization is retry safe': '_localizedTextRegistered' in hotkey_context and 'EnsureLocalizedText();' in hotkey_context and hotkey_context.count('TryGetText(') >= 3,
    'hotkey context survives reload identity and clears on unload': 'internal static GameKeyContext Current' in hotkey_context and 'Current = category;' in hotkey_context and 'internal static void Clear()' in hotkey_context and 'VoidstepHotKeyContext.Clear();' in submodule,
    'arbitrary primary keys replace hardcoded key list': 'KeyOptions' not in settings and 'VoidstepKey' not in settings and 'RequireControlModifier' not in settings and 'ModifierOptions' in settings,
    'per ability modifiers configurable': all(name in settings for name in ('VoidstepModifier', 'BlinkModifier', 'WindblastModifier', 'BendTimeModifier', 'DominoModifier', 'DarkVisionModifier')),
    'input router polls cached native bindings': 'VoidstepInputBindings.TryGetPressedKey' in input_router and 'InputConflictSuppression.Latch' in input_router and 'Enum.TryParse' not in input_router,
    'input router refreshes live modifiers before polling': re.search(r'PollAbility\(\)\s*\{\s*InputConflictSuppression\.CaptureCurrentModifiers\(\);\s*InputConflictSuppression\.RefreshLatches\(\);', input_router) is not None,
    'immutable binding cache covers hot paths': 'private sealed class BindingCache' in input_bindings and 'BoundPrimaryKeys' in input_bindings and 'RefreshCacheIfChanged()' in input_bindings and 'Volatile.Read(ref _cache)' in input_bindings,
    'binding cache invalidates on native changes': 'HotKeyManager.OnKeybindsChanged += Invalidate' in input_bindings and 'HotKeyManager.OnKeybindsChanged -= Invalidate' in input_bindings and 'IsCacheDirty' in input_bindings,
    'modifier strings are parsed only during cache refresh': input_bindings.count('ParseModifiers(') == 7 and 'ReadConfiguredModifiers' in input_bindings,
    'exact modifier combinations preserve modifier primary keys': 'current & ~ModifierForPrimaryKey(primaryKey)' in input_bindings and 'GetCurrentModifiers() == modifiers' not in input_bindings,
    'duplicate chords rejected while native action passes through': duplicate_passthrough is not None and 'native game action remains available' in input_bindings,
    'generic raw input boolean suppression': all(name in input_bindings for name in ('nameof(Input.IsKeyPressed)', 'nameof(Input.IsKeyDown)', 'nameof(Input.IsKeyDownImmediate)', 'nameof(Input.IsKeyReleased)')),
    'generic raw input axis suppression': 'nameof(Input.GetKeyState)' in input_bindings and '__result = Vec2.Zero;' in input_bindings,
    'bound raw queries refresh live modifiers': 'RawInputModifierRefreshPatch' in mission_order_suppression and 'VoidstepInputBindings.IsBoundPrimaryKey(__0)' in mission_order_suppression and 'InputConflictSuppression.CaptureCurrentModifiers();' in mission_order_suppression,
    'integer mission order gamekeys are suppressed': all(name in mission_order_suppression for name in ('nameof(InputContext.IsGameKeyPressed)', 'nameof(InputContext.IsGameKeyDown)', 'nameof(InputContext.IsGameKeyDownImmediate)', 'nameof(InputContext.IsGameKeyReleased)', 'nameof(InputContext.GetGameKeyState)')) and 'new[] { typeof(int) }' in mission_order_suppression and 'SelectOrder1 = 69' in mission_order_suppression and 'SelectOrder6 = 74' in mission_order_suppression,
    'mission order suppression preserves plain number keys': 'InputConflictSuppression.ShouldSuppress(inputKey)' in mission_order_suppression and 'TryGetNumberRowKey' in mission_order_suppression and 'def should_suppress_mapped_order' in mirror_tests and 'def test_plain_number_key_remains_native_without_modifier' in mirror_tests,
    'suppression preserves own polling through bypass': '[ThreadStatic]' in input_bindings and 'EnterBypass()' in input_bindings and 'IsBypassed' in input_bindings,
    'suppression latches are thread safe': 'ConcurrentDictionary<InputKey, byte> LatchedKeys' in input_bindings and 'LatchedKeys.TryAdd' in input_bindings and 'LatchedKeys.TryRemove' in input_bindings,
    'suppression latches complete chord lifecycle': 'RefreshLatches()' in input_bindings and 'Input.IsKeyReleased(inputKey)' in input_bindings,
    'unbound raw keys exit before mission checks': hot_path_order is not None,
    'binding conflict checks are throttled and change driven': 'BindingRefreshInterval' in mission_behavior and 'RefreshBindings(dt);' in mission_behavior and mission_behavior.count('CheckBindingConflict();') == 2,
    'ability input fails closed with both ownership gates': 'InputSuppressionReady { get; private set; }' in submodule and 'NativeHotkeysReady { get; private set; }' in submodule and 'if (!InputSuppressionReady || _harmony == null || !NativeHotkeysReady)' in submodule and '!VoidstepSubModule.NativeHotkeysReady' in input_router,
    'hotkey event and context teardown are explicit': 'DetachKeybindEvents();' in submodule and 'VoidstepHotKeyContext.Clear();' in submodule and 'InputConflictSuppression.Reset();' in submodule,
    'harmony cleanup retains ownership on failure': 'for (var attempt = 1; attempt <= 2; attempt++)' in submodule and 'submodule unload was aborted' in submodule and re.search(r'if \(_harmony != null && !TryUnpatchOwnedPatches\(\)\)\s*\{.*?return;\s*\}', submodule, re.DOTALL) is not None,
    'camera aligned targeting': 'GetCameraFrame()' in targeting and 'GetCameraRayDirection' in targeting,
    'projectile entities are skipped during Blink ray targeting': 'IsTransientProjectileEntity' in targeting and 'BodyFlags.MissileOnly' in targeting and 'BodyFlags.DroppedItem' in targeting and 'MaximumIgnoredRayHits' in targeting,
    'projectile name filtering is allocation free': 'ProjectileNameFragments' in targeting and 'StringComparison.OrdinalIgnoreCase' in targeting and 'ToLowerInvariant()' not in targeting,
    'procedural cast sigil replaces arrow geometry': 'Mesh.CreateMeshWithMaterial' in effects and 'private static void AddRing' in effects and 'entity.AddMesh(mesh, false)' in effects and 'entity.AddMesh(donor' not in effects and 'entity.AddMesh(source' not in effects,
    'failed cast sigil donors release local ownership': 'Mesh mesh = null;' in effects and 'mesh = null;' in effects,
    'cast sigil color updates own mesh and contour': '_markerMeshes.TryGetValue(marker, out var mesh)' in effects and 'mesh.Color = color;' in effects and 'marker.SetContourColor(color, true)' in effects,
    'marker particle count respects effect intensity': 'var intensity = VoidstepSettings.Current.EffectIntensity;' in effects and 'var offsetCount = intensity >= 1f ? MarkerOffsets.Length : 1;' in effects,
    'all six abilities expose cast feedback': 'CreateWorldMarker' in ability_manager and 'CreateWorldMarker' in blink and 'CreateWorldMarker' in domino and '_effects.Windblast' in windblast and '_effects.BendTime' in ability_manager and 'SetContourColor' in dark_vision,
    'blink targeting freezes mission time': 'new Mission.TimeSpeedRequest(0f, AimTimeRequestId)' in blink and 'MBCommon.GetApplicationTime()' in blink and 'realDt' in blink,
    'blink preview bounds fallback work': 'PreviewFallbackCandidateBudget = 24' in blink and 'fallbackCandidateBudget' in teleport_validator and 'fallbackLimit' in teleport_validator,
    'cleave fallback remains exhaustive and range bounded': 'fallbackCandidateBudget = 0' in teleport_validator and ': _fallback.Count;' in teleport_validator and 'candidateDelta.Length > maximumRange + 0.05f' in teleport_validator and '3.2f' in teleport_validator,
    'bend time duration uses application time': 'MBCommon.GetApplicationTime()' in time_control and '_remaining -= realDt;' in time_control,
    'bend time compensates player and mount systems': all(name in time_control for name in ('MaxSpeedMultiplier', 'CombatMaxSpeedMultiplier', 'SwingSpeedMultiplier', 'ReloadSpeed', 'BipedalRangedReadySpeedMultiplier', 'BipedalRangedReloadSpeedMultiplier', 'MountSpeed', 'MountManeuver', 'MountDashAccelerationMultiplier')) and 'for (var channel = 0; channel < 4; channel++)' in time_control,
    'bend time separates mutation ownership': all(name in time_control for name in ('_playerPropertiesApplied', '_mountPropertiesApplied', '_actionSpeedsApplied')) and 'RestoreCompensation();' in time_control,
    'bend time refreshes externally recalculated baselines': 'RefreshPlayerBaselinesAfterExternalUpdate' in time_control and 'RefreshMountBaselinesAfterExternalUpdate' in time_control and '!Approximately(driven.MaxSpeedMultiplier, _appliedMaxSpeedMultiplier)' in time_control,
    'bend time restores only owned values': 'Approximately(driven.MaxSpeedMultiplier, _appliedMaxSpeedMultiplier)' in time_control and 'Approximately(driven.MountSpeed, _appliedMountSpeed)' in time_control,
    'bend time handles mount replacement': 'RefreshControlledMount();' in time_control and 'ReferenceEquals(current, _mount)' in time_control and 'TryCaptureMountSnapshot' in time_control,
    'action speed ownership requires a successful write': re.search(r'SetCurrentActionSpeed\(channel, speed\);\s*_actionSpeedsApplied = true;', time_control) is not None,
    'cleave preserves weapon snapshot': 'MissionWeapon _cleaveWeapon' in ability_manager and 'MissionWeapon _weapon' in cleave and 'attacker.WieldedWeapon' not in blow_factory,
    'cleave rejects non-melee weapons twice': ability_manager.count('WeaponValidation.IsUsableMeleeWeapon') >= 1 and cleave.count('WeaponValidation.IsUsableMeleeWeapon') >= 1 and 'CurrentUsageItem' in weapon_validation and 'IsMeleeWeapon' in weapon_validation,
    'cleave refunds paid pre-effect failures': ability_manager.count('RollbackPayment(AbilityId.VoidstepCleave)') >= 2,
    'cleave schedules all candidates before successful cap': uncapped_cleave_schedule is not None and '_successfulHits >= _maximumTargets' in cleave,
    'cleave uses public swing damage API': 'weapon.GetModifiedSwingDamageForCurrentUsage()' in blow_factory and 'GetSwingDamage' not in blow_factory,
    'cleave does not force victim action': 'act_strike_bent_over' not in files.get('AnimationController.cs','') and 'SetActionChannel' not in files.get('AnimationController.cs',''),
    'per-cast hit registry': 'HitRegistry<int> _hits' in cleave,
    'cleave deterministic cleanup': '_hits.Clear();' in cleave and '_snapshotSchedule.Clear();' in cleave,
    'time request ownership retained through cleanup': '_cleanupPending' in time_control and '_ownership.TryGet(_token, out requestId)' in time_control and time_release_guard is not None and time_control.count('RemoveTimeSpeedRequest(') == 1,
    'blink request ownership retained through cleanup': '_timeCleanupPending' in blink and blink_release_guard is not None and blink.count('RemoveTimeSpeedRequest(') == 1,
    'domino index storage': 'Dictionary<int, Agent> _linked' in domino and 'FindAgentWithIndex' in domino,
    'domino recursion guard': 'RecursionGuard<int>' in domino and '(blow.BlowFlag & BlowFlags.NoSound) != 0' in domino,
    'dark vision immediate and throttled': 'Refresh();' in dark_vision and 'DarkVisionRefreshInterval' in dark_vision,
    'dark vision counts successful contours only': 'if (TrySetContour(agent, color))' in dark_vision and 'private static bool ClearContour' in dark_vision and 'if (ClearContour(' in dark_vision,
    'no campaign behavior': 'CampaignBehaviorBase' not in all_text and 'CampaignEvents.' not in all_text,
    'no global agent collection': not re.search(r'\bstatic\s+readonly\s+.*(?:\bAgent\b|\bList<Agent>\b|\bHashSet<Agent>\b)', all_text),
    'no full all-agent scan': 'AllAgents' not in mission_behavior and 'Agents)' not in mission_behavior,
    'mission end cleanup': 'Cleanup(CancelReason.MissionEnded)' in mission_behavior,
    'missing effects are nonfatal': 'Optional particle failed' in effects and 'return -1;' in effects,
    'whole-cast target cap': '_successfulHits >= _maximumTargets' in cleave,
    'dark vision reuses stale buffer': 'List<int> _staleBuffer' in dark_vision and 'private void Refresh()' in dark_vision and 'new List<int>' not in dark_vision.split('private void Refresh()', 1)[-1],
    'domino reuses snapshot buffer': 'List<int> _snapshotBuffer' in domino and 'public void Tick()' in domino and 'new List<int>' not in domino.split('public void Tick()', 1)[-1],
}
failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ': ' + name)
if failed:
    print('Failed invariants: ' + ', '.join(failed), file=sys.stderr)
    raise SystemExit(1)
print(f'Validated {len(checks)} source invariants.')
