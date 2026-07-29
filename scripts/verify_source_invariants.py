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


time_control = text('TimeControlService.cs')
blink = text('BlinkController.cs')
ability_manager = text('AbilityManager.cs')
cleave = text('CleaveSweepController.cs')
submodule = text('VoidstepSubModule.cs')
mission_behavior = text('VoidstepMissionBehavior.cs')
input_router = text('InputRouter.cs')
input_bindings = text('VoidstepInputBindings.cs')
mission_order_suppression = text('MissionOrderInputSuppression.cs')
hotkey_context = text('VoidstepHotKeyContext.cs')
settings = text('VoidstepSettings.cs')
weapon_validation = text('WeaponValidation.cs')
dark_vision = text('DarkVisionService.cs')
blow_factory = text('BlowFactory.cs')
effects = text('EffectController.cs')
targeting = text('TargetingService.cs')
teleport_validator = text('TeleportValidator.cs')
windblast = text('WindblastController.cs')
domino = text('DominoLinkService.cs')
animation = text('AnimationController.cs')
cast_animation_patch = text('AbilityCastAnimationPatch.cs')
wheel_coordinator = text('AbilityWheelCoordinator.cs')
selection = text('AbilitySelectionController.cs')
standalone_wheel = text('StandaloneAbilityWheel.cs')
tor_wheel = text('TorAbilityWheelAdapter.cs')
wheel_runtime = text('VoidstepWheelRuntime.cs')
wheel_suppression = text('AbilityWheelInputSuppressionPatch.cs')
wheel_vm = text('VoidstepAbilityWheelVM.cs')
mirror_tests = (root / 'scripts' / 'run_logic_mirror_tests.py').read_text(encoding='utf-8')
wheel_prefab_path = root / 'module' / 'Voidstep' / 'GUI' / 'Prefabs' / 'VoidstepAbilityWheel.xml'
wheel_prefab = wheel_prefab_path.read_text(encoding='utf-8') if wheel_prefab_path.exists() else ''


def mask_csharp_noncode(source):
    """Mask comments and literals while preserving offsets, braces and newlines."""
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
            if end < 0:
                end = length
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


def extract_braced_body(source, opening_brace):
    if opening_brace < 0 or opening_brace >= len(source) or source[opening_brace] != '{':
        return None
    depth = 0
    for index in range(opening_brace, len(source)):
        if source[index] == '{':
            depth += 1
        elif source[index] == '}':
            depth -= 1
            if depth == 0:
                return source[opening_brace + 1:index]
    return None


def extract_method(masked_source, declaration_pattern):
    matches = list(re.finditer(declaration_pattern, masked_source))
    if len(matches) != 1:
        return None
    opening = masked_source.find('{', matches[0].start(), matches[0].end())
    if opening < 0:
        return None
    return extract_braced_body(masked_source, opening)


def validate_native_action_channel_method(masked_source, method_name, expected_speed):
    body = extract_method(
        masked_source,
        r'\bprivate\s+void\s+' + re.escape(method_name) + r'\s*\([^)]*\)\s*\{')
    if body is None:
        return False
    loop_pattern = re.compile(
        r'\bfor\s*\(\s*var\s+(?P<channel>[A-Za-z_]\w*)\s*=\s*0\s*;\s*'
        r'(?P=channel)\s*<\s*NativeActionChannelCount\s*;\s*'
        r'(?P=channel)\s*\+\+\s*\)\s*\{')
    loops = list(loop_pattern.finditer(body))
    if len(loops) != 1 or len(re.findall(r'\bfor\s*\(', body)) != 1:
        return False
    loop = loops[0]
    loop_body = extract_braced_body(body, body.find('{', loop.start(), loop.end()))
    if loop_body is None:
        return False
    call_pattern = r'\bSetCurrentActionSpeed\s*\(\s*([A-Za-z_]\w*)\s*,\s*([^,\)]+?)\s*\)'
    method_calls = re.findall(call_pattern, body)
    loop_calls = re.findall(call_pattern, loop_body)
    return (
        len(method_calls) == 1
        and len(loop_calls) == 1
        and loop_calls[0][0] == loop.group('channel')
        and loop_calls[0][1].strip() == expected_speed)


def validate_selection_player_guard(masked_source):
    body = extract_method(
        masked_source,
        r'\binternal\s+bool\s+Select\s*\(\s*AbilityId\s+[A-Za-z_]\w*\s*,\s*string\s+[A-Za-z_]\w*\s*\)\s*\{')
    if body is None:
        return False

    guard = re.search(
        r'\bvar\s+player\s*=\s*_mission\.MainAgent\s*;\s*'
        r'if\s*\(\s*player\s*==\s*null\s*\|\|\s*!player\.IsActive\(\)\s*\|\|\s*'
        r'player\.Health\s*<=\s*0f\s*\)\s*'
        r'\{\s*Show\s*\([^;]*\)\s*;\s*return\s+false\s*;\s*\}',
        body,
        re.DOTALL)
    busy = re.search(r'\bif\s*\(\s*_manager\.IsBusy\s*\)', body)
    mutation = re.search(
        r'\bCancel\s*\(|\b_selected\s*=|\b_previewRefreshRemaining\s*=|'
        r'\b_blinkTargetingOwned\s*=|\b_previewCreationFailures\s*=|'
        r'\b_previewCreationDisabled\s*=|\bClearSelectionVisuals\s*\(|'
        r'\bRefreshPreview\s*\(',
        body)
    return (
        guard is not None
        and busy is not None
        and mutation is not None
        and guard.end() <= busy.start() < mutation.start())


time_code = mask_csharp_noncode(time_control)
bend_time_channel_safety = (
    re.search(r'\bprivate\s+const\s+int\s+NativeActionChannelCount\s*=\s*2\s*;', time_code) is not None
    and validate_native_action_channel_method(time_code, 'SetActionSpeeds', 'speed')
    and validate_native_action_channel_method(time_code, 'RestoreActionSpeeds', '1f'))
selection_player_guard = validate_selection_player_guard(mask_csharp_noncode(selection))

time_release_guard = re.search(
    r'private bool TryCompleteRelease\(\)\s*\{.*?'
    r'if \(_mission\.GetRequestedTimeSpeed\(requestId, out requestedFactor\)\)\s*\{\s*'
    r'_mission\.RemoveTimeSpeedRequest\(requestId\);\s*'
    r'if \(_mission\.GetRequestedTimeSpeed\(requestId, out requestedFactor\)\)\s*return false;\s*\}.*?'
    r'if \(!_ownership\.Release\(token, out releasedRequestId\)\)\s*return false;.*?'
    r'_token = 0;', time_control, re.DOTALL)
blink_release_guard = re.search(
    r'private bool ReleaseAimTimeRequest\(\)\s*\{.*?'
    r'if \(_mission\.GetRequestedTimeSpeed\(AimTimeRequestId, out requestedFactor\)\)\s*\{\s*'
    r'_mission\.RemoveTimeSpeedRequest\(AimTimeRequestId\);\s*'
    r'if \(_mission\.GetRequestedTimeSpeed\(AimTimeRequestId, out requestedFactor\)\)\s*return false;\s*\}.*?'
    r'_ownsTimeRequest = false;\s*_timeCleanupPending = false;', blink, re.DOTALL)
uncapped_cleave_schedule = re.search(
    r'SweepPlanner\.BuildSchedule\(\s*_candidates,\s*_startAngle,\s*_sweepRadians,\s*_direction,\s*_radius,\s*0,\s*_schedule\);',
    cleave, re.DOTALL)
duplicate_passthrough = re.search(
    r'internal static bool IsChordActiveForKey\(InputKey inputKey\).*?'
    r'AmbiguousChords\.Contains\(ChordCode\(entry\.Modifiers, inputKey\)\)\)\s*continue;',
    input_bindings, re.DOTALL)
hot_path_order = re.search(
    r'internal static bool ShouldSuppress\(InputKey inputKey\).*?'
    r'LatchedKeys\.ContainsKey\(inputKey\).*?'
    r'IsBoundPrimaryKey\(inputKey\).*?RuntimeCanSuppress\(\)', input_bindings, re.DOTALL)
domino_hit_callback = re.search(
    r'public void OnAgentHit\(.*?\n\s*\}\n\n\s*public void OnAgentRemoved', domino, re.DOTALL)
domino_removed_callback = re.search(
    r'public void OnAgentRemoved\(.*?\n\s*\}\n\n\s*public void OnAgentDeleted', domino, re.DOTALL)
domino_hit_text = domino_hit_callback.group(0) if domino_hit_callback else ''
domino_removed_text = domino_removed_callback.group(0) if domino_removed_callback else ''
cleave_partial_ownership = re.search(
    r'SetCurrentActionSpeed\(1, 0\.01f\);\s*'
    r'_cleaveActor = actor;\s*_cleaveActionOwned = true;\s*'
    r'actor\.SetCurrentActionProgress\(1, 0f\);', animation, re.DOTALL)

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
    'modifier strings parsed only during cache refresh': input_bindings.count('ParseModifiers(') == 7 and 'ReadConfiguredModifiers' in input_bindings,
    'exact modifier combinations preserve modifier primary keys': 'current & ~ModifierForPrimaryKey(primaryKey)' in input_bindings and 'GetCurrentModifiers() == modifiers' not in input_bindings,
    'duplicate chords rejected while native action passes through': duplicate_passthrough is not None and 'native game action remains available' in input_bindings,
    'generic raw input boolean suppression': all(name in input_bindings for name in ('nameof(Input.IsKeyPressed)', 'nameof(Input.IsKeyDown)', 'nameof(Input.IsKeyDownImmediate)', 'nameof(Input.IsKeyReleased)')),
    'generic raw input axis suppression': 'nameof(Input.GetKeyState)' in input_bindings and '__result = Vec2.Zero;' in input_bindings,
    'bound raw queries refresh live modifiers': 'RawInputModifierRefreshPatch' in mission_order_suppression and 'VoidstepInputBindings.IsBoundPrimaryKey(__0)' in mission_order_suppression and 'InputConflictSuppression.CaptureCurrentModifiers();' in mission_order_suppression,
    'integer mission order gamekeys are suppressed': all(name in mission_order_suppression for name in ('nameof(InputContext.IsGameKeyPressed)', 'nameof(InputContext.IsGameKeyDown)', 'nameof(InputContext.IsGameKeyDownImmediate)', 'nameof(InputContext.IsGameKeyReleased)', 'nameof(InputContext.GetGameKeyState)')) and 'new[] { typeof(int) }' in mission_order_suppression and 'SelectOrder1 = 69' in mission_order_suppression and 'SelectOrder6 = 74' in mission_order_suppression,
    'plain number keys remain native without modifier': 'InputConflictSuppression.ShouldSuppress(inputKey)' in mission_order_suppression and 'def test_plain_number_key_remains_native_without_modifier' in mirror_tests,
    'suppression preserves own polling through bypass': '[ThreadStatic]' in input_bindings and 'EnterBypass()' in input_bindings and 'IsBypassed' in input_bindings,
    'suppression latches are thread safe': 'ConcurrentDictionary<InputKey, byte> LatchedKeys' in input_bindings and 'LatchedKeys.TryAdd' in input_bindings and 'LatchedKeys.TryRemove' in input_bindings,
    'unbound raw keys exit before mission checks': hot_path_order is not None,
    'ability input fails closed with ownership gates': 'InputSuppressionReady { get; private set; }' in submodule and 'NativeHotkeysReady { get; private set; }' in submodule and '!VoidstepSubModule.NativeHotkeysReady' in input_router,
    'hotkey teardown is explicit': 'DetachKeybindEvents();' in submodule and 'VoidstepHotKeyContext.Clear();' in submodule and 'InputConflictSuppression.Reset();' in submodule,
    'harmony cleanup retains ownership on failure': 'for (var attempt = 1; attempt <= 2; attempt++)' in submodule and 'submodule unload was aborted' in submodule,
    'camera aligned targeting': 'GetCameraFrame()' in targeting and 'GetCameraRayDirection' in targeting,
    'projectiles skipped during Blink targeting': 'IsTransientProjectileEntity' in targeting and 'BodyFlags.MissileOnly' in targeting and 'BodyFlags.DroppedItem' in targeting,
    'projectile filtering is allocation free': 'ProjectileNameFragments' in targeting and 'StringComparison.OrdinalIgnoreCase' in targeting and 'ToLowerInvariant()' not in targeting,
    'cast reticle is generated without donor geometry': 'Mesh.CreateMeshWithMaterial' in effects and 'AddRadialSpikes' in effects and 'AddVerticalDiamond' in effects and 'entity.AddMesh(mesh, false)' in effects and 'entity.AddMesh(donor' not in effects,
    'cast reticle is forced visible': '1.02f, 0.82f, 36' in effects and 'MBMeshCullingMode.None' in effects and 'SetMeshRenderOrder(1000)' in effects and 'EntityVisibilityFlags.NoShadow' in effects,
    'cast reticle color is live': '_markerMeshes.TryGetValue(marker, out var mesh)' in effects and 'mesh.SetColorAndStroke(color, color, true)' in effects and 'marker.SetContourColor(color, true)' in effects,
    'successful activations play logged native actions': '[HarmonyPatch(typeof(AbilityManager), nameof(AbilityManager.TryActivate))]' in cast_animation_patch and 'AnimationController.PlayAbilityCast(actor, ability, __instance.Logger);' in cast_animation_patch and 'SetActionChannel(1, action)' in animation,
    'dark vision disable skips cast action': 'out bool __state' in cast_animation_patch and 'if (!__result || __state) return;' in cast_animation_patch,
    'cleave execution owns speed before progress': cleave_partial_ownership is not None and 'ResetActionSpeed(actor);' in animation,
    'cleave execution owns and restores progress': 'BeginCleave(actor);' in cleave and 'SetCleaveProgress(_actor, progress);' in cleave and 'ResetActionSpeed(_actor);' in cleave,
    'all six abilities expose cast feedback': 'CreateWorldMarker' in ability_manager and 'CreateWorldMarker' in blink and 'CreateWorldMarker' in domino and '_effects.Windblast' in windblast and '_effects.BendTime' in ability_manager and 'SetContourColor' in dark_vision,
    'blink targeting freezes mission time': 'new Mission.TimeSpeedRequest(0f, AimTimeRequestId)' in blink and 'MBCommon.GetApplicationTime()' in blink and 'realDt' in blink,
    'blink preview bounds fallback work': 'PreviewFallbackCandidateBudget = 24' in blink and 'fallbackCandidateBudget' in teleport_validator,
    'cleave fallback remains exhaustive and bounded': 'fallbackCandidateBudget = 0' in teleport_validator and ': _fallback.Count;' in teleport_validator and 'candidateDelta.Length > maximumRange + 0.05f' in teleport_validator,
    'bend time duration uses application time': 'MBCommon.GetApplicationTime()' in time_control and '_remaining -= realDt;' in time_control,
    'bend time compensates player and mount systems': all(name in time_control for name in ('MaxSpeedMultiplier', 'CombatMaxSpeedMultiplier', 'SwingSpeedMultiplier', 'ReloadSpeed', 'MountSpeed', 'MountManeuver')) and bend_time_channel_safety,
    'bend time never writes unverified action channels': bend_time_channel_safety,
    'bend time separates mutation ownership': all(name in time_control for name in ('_playerPropertiesApplied', '_mountPropertiesApplied', '_actionSpeedsApplied')) and 'RestoreCompensation();' in time_control,
    'cleave preserves weapon snapshot': 'MissionWeapon _cleaveWeapon' in ability_manager and 'MissionWeapon _weapon' in cleave and 'attacker.WieldedWeapon' not in blow_factory,
    'cleave rejects non-melee weapons twice': ability_manager.count('WeaponValidation.IsUsableMeleeWeapon') >= 1 and cleave.count('WeaponValidation.IsUsableMeleeWeapon') >= 1 and 'CurrentUsageItem' in weapon_validation,
    'cleave refunds paid pre-effect failures': ability_manager.count('RollbackPayment(AbilityId.VoidstepCleave)') >= 2,
    'cleave schedules all candidates': uncapped_cleave_schedule is not None and '_successfulHits >= _maximumTargets' in cleave,
    'cleave uses public swing damage': 'weapon.GetModifiedSwingDamageForCurrentUsage()' in blow_factory and 'GetSwingDamage' not in blow_factory,
    'time request ownership retained through cleanup': '_cleanupPending' in time_control and time_release_guard is not None and time_control.count('RemoveTimeSpeedRequest(') == 1,
    'blink request ownership retained through cleanup': '_timeCleanupPending' in blink and blink_release_guard is not None and blink.count('RemoveTimeSpeedRequest(') == 1,
    'domino index and identity storage': 'Dictionary<int, Agent> _linked' in domino and 'FindAgentWithIndex' in domino and 'ReferenceEquals(resolved, identity)' in domino,
    'domino hit callback only queues': domino_hit_callback is not None and '_pending.Add(new PendingPropagation' in domino_hit_text and 'ApplyDirectBlow' not in domino_hit_text,
    'domino removal callback only queues': domino_removed_callback is not None and '_pending.Add(new PendingPropagation' in domino_removed_text and 'ApplyDirectBlow' not in domino_removed_text,
    'domino dispatches after callback': 'DispatchPendingPropagations();' in domino and '_blows.ApplyDirectBlow' in domino and 'after the native hit callback completed' in domino,
    'domino propagated deaths cannot recurse': '_propagatedDeathSuppression' in domino and 'ConsumePropagatedDeathSuppression' in domino and 'PropagatedDeathSuppressionTicks' in domino,
    'domino propagation uses explicit hit ownership': '_propagatedHitSuppression' in domino and 'AddPropagatedHitSuppression' in domino and 'ConsumePropagatedHitSuppression' in domino and 'RemoveUnconsumedPropagatedHitSuppression' in domino,
    'domino does not treat all NoSound attacks as propagated': '(blow.BlowFlag & BlowFlags.NoSound) != 0' not in mask_csharp_noncode(domino),
    'domino accepts player and controlled mount sources': 'IsPlayerSource' in domino and '_player.MountAgent' in domino,
    'wheel coordinator replaces direct activation in mission tick': '_wheel.Tick(dt);' in mission_behavior and '_manager.TryActivate(ability.Value)' not in mission_behavior,
    'direct bindings select rather than cast': '_selection.Select(directAbility.Value, "configured direct selector")' in wheel_coordinator,
    'right mouse confirms selected ability': 'Input.IsKeyPressed(InputKey.RightMouseButton)' in wheel_coordinator and '_selection.Confirm()' in wheel_coordinator,
    'Q suppression is limited to owned standalone state': 'return !_tor.IsAvailable && (_standalone.IsOpen || _selection.HasSelection);' in wheel_coordinator,
    'selection validates player before mutation': selection_player_guard,
    'Blink cancellation fails closed': '_cancelCurrent == null' in selection and 'Blink targeting could not be cancelled safely.' in selection and 'return false;' in selection,
    'preview creation retries are bounded': 'MaximumPreviewCreationFailures = 3' in selection and '_previewCreationDisabled = true;' in selection,
    'selection is independent from persistent effects': 'AbilityId? _selected' in selection and '_manager.TryActivate(ability)' in selection and 'ClearSelectionVisuals' in selection,
    'all selected abilities receive area previews': all(token in selection for token in ('BuildCleavePreview', 'BuildWindblastPreview', 'BuildDominoPreview', 'BuildRadiusPreview')) and 'AbilityId.Blink' in selection,
    'standalone wheel derives radial sectors from registry': 'var count = VoidstepInputBindings.Abilities.Length;' in standalone_wheel and 'var sector = Math.PI * 2.0 / count;' in standalone_wheel and 'VoidstepInputBindings.Abilities[selected]' in standalone_wheel,
    'standalone wheel retains failed layer cleanup': 'ownership was retained for a later cleanup retry' in standalone_wheel and 'private bool _layerAdded;' in standalone_wheel,
    'standalone wheel uses semantic input restriction mask': 'Enum.GetNames(type)' in standalone_wheel and 'string.Equals(name, "All", StringComparison.Ordinal)' in standalone_wheel and 'Enum.ToObject(type, -1)' not in standalone_wheel,
    'standalone wheel has Gauntlet prefab': wheel_prefab_path.exists() and '<Prefab>' in wheel_prefab and all(name in wheel_prefab for name in ('@CleaveText', '@BlinkText', '@WindblastText', '@BendTimeText', '@DominoText', '@DarkVisionText')),
    'standalone wheel view model rejects invalid entries': 'internal bool SetSelected(int index)' in wheel_vm and 'return false;' in wheel_vm,
    'TOR integration is reflection isolated': 'TOR_Core' in tor_wheel and 'Assembly' in tor_wheel and 'GetType(' in tor_wheel and 'using TOR_Core' not in tor_wheel,
    'TOR wheel receives six proxy abilities': 'InjectProxies' in tor_wheel and 'VoidstepInputBindings.Abilities.Length' in tor_wheel and '_knownAbilities.Add(proxy)' in tor_wheel,
    'TOR proxy enums use semantic names': 'ParseEnumValue("TOR_Core.AbilitySystem.AbilityType", "Spell")' in tor_wheel and 'Enum.ToObject' not in tor_wheel,
    'TOR deactivation removes proxies before clearing references': 'DeactivateLiveAttachment' in tor_wheel and tor_wheel.find('RemoveInjectedProxies();', tor_wheel.find('private void DeactivateLiveAttachment')) < tor_wheel.find('_knownAbilities = null;', tor_wheel.find('private void DeactivateLiveAttachment')),
    'TOR proxy targeting is disabled only by ownership': 'IsDisabledPrefix' in tor_wheel and 'runtime.IsTorProxy(__instance)' in tor_wheel,
    'TOR targeting closes after Mouse2 cast': '_tor.CloseTargetingMode();' in wheel_coordinator and 'DisableAbilityMode' in tor_wheel,
    'wheel input suppression is bypass safe': 'InputConflictSuppression.IsBypassed' in wheel_suppression and 'VoidstepWheelRuntime.ShouldSuppress(__0)' in wheel_suppression and 'if (method != null)' in wheel_suppression,
    'wheel runtime is mission scoped and diagnosed': 'VoidstepWheelRuntime.Attach(this, logger);' in wheel_coordinator and 'VoidstepWheelRuntime.Detach(this);' in wheel_coordinator and 'Replacing a stale Voidstep ability-wheel coordinator' in wheel_runtime,
    'no campaign behavior': 'CampaignBehaviorBase' not in all_text and 'CampaignEvents.' not in all_text,
    'no static agent collection': not re.search(r'\bstatic\s+readonly\s+.*(?:\bAgent\b|\bList<Agent>\b|\bHashSet<Agent>\b)', all_text),
    'no full mission-agent scan': 'AllAgents' not in mission_behavior and 'Agents)' not in mission_behavior,
    'mission end cleanup': 'Cleanup(CancelReason.MissionEnded)' in mission_behavior,
    'missing effects are nonfatal': 'Optional particle failed' in effects and 'return -1;' in effects,
    'dark vision reuses stale buffer': 'List<int> _staleBuffer' in dark_vision and 'new List<int>' not in dark_vision.split('private void Refresh()', 1)[-1],
    'domino reuses snapshot buffer': 'List<int> _snapshotBuffer' in domino and 'new List<int>' not in domino.split('public void Tick()', 1)[-1],
}

failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ': ' + name)
if failed:
    print('Failed invariants: ' + ', '.join(failed), file=sys.stderr)
    raise SystemExit(1)
print(f'Validated {len(checks)} source invariants.')
