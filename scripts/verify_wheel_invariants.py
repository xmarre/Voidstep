#!/usr/bin/env python3
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]


def read(relative):
    return (root / relative).read_text(encoding='utf-8')


def compact(value):
    return ' '.join(value.split())


coordinator = read('src/Voidstep/AbilityWheelCoordinator.cs')
standalone = read('src/Voidstep/StandaloneAbilityWheel.cs')
wheel_suppression = read('src/Voidstep/AbilityWheelInputSuppressionPatch.cs')
tor = read('src/Voidstep/TorAbilityWheelAdapter.cs')
selection = read('src/Voidstep/AbilitySelectionController.cs')
mission = read('src/Voidstep/VoidstepMissionBehavior.cs')
mission_input = read('src/Voidstep/MissionOrderInputSuppression.cs')
cast_animation = read('src/Voidstep/AbilityCastAnimationPatch.cs')
bindings = read('src/Voidstep/VoidstepInputBindings.cs')
project = read('src/Voidstep/Voidstep.csproj')
prefab = read('module/Voidstep/GUI/Prefabs/VoidstepAbilityWheel.xml')
tor_compact = compact(tor)

ability_order = (
    'AbilityId.VoidstepCleave',
    'AbilityId.Blink',
    'AbilityId.Windblast',
    'AbilityId.BendTime',
    'AbilityId.Domino',
    'AbilityId.DarkVision',
)
prefab_order = (
    '@CleaveText',
    '@BlinkText',
    '@WindblastText',
    '@BendTimeText',
    '@DominoText',
    '@DarkVisionText',
)

ability_block = re.search(
    r'internal\s+static\s+readonly\s+AbilityId\[\]\s+Abilities\s*=\s*\{(?P<body>.*?)\};',
    bindings,
    re.DOTALL)
registered_abilities = tuple(re.findall(
    r'\bAbilityId\.[A-Za-z_]\w*\b',
    ability_block.group('body') if ability_block else ''))
prefab_bindings = tuple(re.findall(
    r'Text="(@(?:Cleave|Blink|Windblast|BendTime|Domino|DarkVision)Text)"',
    prefab))

checks = {
    'coordinator passes mission delta time to TOR adapter': '_tor.Tick(dt);' in coordinator and '_tor.Tick();' not in coordinator,
    'TOR attachment retries are throttled': all(token in tor for token in (
        'private const float AttachRetryInterval = 0.5f;',
        'internal void Tick(float dt)',
        '_attachRetryRemaining -= Math.Max(0f, dt);',
        '_attachRetryRemaining = AttachRetryInterval;')),
    'TOR API readiness is separate from live wheel availability': all(token in tor for token in (
        'private bool _apiReady;',
        'private bool _available;',
        'internal bool IsAvailable => _available;',
        '_apiReady = true;',
        'InjectProxies(agent);',
        '_available = true;')),
    'standalone remains active until TOR injection succeeds': all(token in coordinator for token in (
        'if (!_tor.IsAvailable)',
        '_standalone.Tick();',
        'HandleWheelAvailabilityTransition();')) and 'standalone wheel remains active' in tor,
    'standalone Gauntlet overlay never owns mission input':
        'display-only Gauntlet layer' in standalone and
        all(token not in standalone for token in (
            'IsFocusLayer',
            'ConfigureInputRestrictions',
            'SetInputRestrictions',
            'TrySetFocus',
            'TryLoseFocus',
            'InputUsageMask')),
    'wheel suppression never intercepts mouse-wheel keys':
        all(token not in coordinator + wheel_suppression for token in (
            'MouseScrollUp',
            'MouseScrollDown',
            'MouseScrollAxis')),
    'TOR opening cancels stale Voidstep selection': 'state == 1 && _lastState != 1 && _selection.HasSelection' in tor and '_selection.Cancel(true);' in tor,
    'TOR entries receive stable donor icons': all(token in tor for token in (
        'ResolveDonorSprites();',
        '_donorSprites.Contains(sprite)',
        '_donorSprites[i % _donorSprites.Count]',
        'while (_donorSprites.Count < VoidstepInputBindings.Abilities.Length)')),
    'TOR proxy cast path is guarded': all(token in tor for token in (
        'PatchProxyGuards();',
        'nameof(TryCastPrefix)',
        'nameof(DoCastPrefix)',
        'GetMethod("TryCast"',
        'GetMethod("DoCast"',
        '_harmony.Patch(tryCast, prefix: tryCastPrefix);',
        '_harmony.Patch(baseDoCast, prefix: doCastPrefix);')),
    'TOR guard affects only owned proxies':
        tor_compact.count('runtime.IsTorProxy(__instance)') >= 3 and
        'if (runtime == null || !runtime.IsTorProxy(__instance)) return true;' in tor_compact and
        'return runtime == null || !runtime.IsTorProxy(__instance);' in tor_compact,
    'TOR spell override is separately guarded': 'spellDoCast' in tor and '_harmony.Patch(spellDoCast, prefix: doCastPrefix);' in tor,
    'TOR patches and proxies are removed on cleanup': '_harmony?.UnpatchAll(HarmonyId)' in tor and 'DeactivateLiveAttachment(true);' in tor and 'RemoveInjectedProxies();' in tor,
    'TOR remains an optional reflection boundary': 'using TOR_Core' not in tor and '<Reference Include="TOR_Core"' not in project and 'AppDomain.CurrentDomain.GetAssemblies()' in tor,
    'TOR template enums are semantic': all(token in tor for token in (
        'ParseEnumValue("TOR_Core.AbilitySystem.AbilityType", "Spell")',
        'ParseEnumValue("TOR_Core.AbilitySystem.AbilityTargetType", "WorldPosition")',
        'ParseEnumValue("TOR_Core.AbilitySystem.Crosshairs.CrosshairType", "Pointer")',
        'ParseEnumValue("TOR_Core.AbilitySystem.CastType", "Instant")')) and 'Enum.ToObject' not in tor,
    'right mouse confirmation is read through bypass': 'Input.IsKeyPressed(InputKey.RightMouseButton)' in coordinator and 'InputConflictSuppression.EnterBypass()' in coordinator,
    'right mouse is suppressed through release': all(token in coordinator for token in (
        '_suppressRightMouseUntilRelease = true;',
        '_selection.HasSelection || _suppressRightMouseUntilRelease',
        'Input.IsKeyReleased(InputKey.RightMouseButton)')),
    'native attack and defend are suppressed only during Mouse2 ownership': all(token in mission_input for token in (
        'private const int Attack = 9;',
        'private const int Defend = 10;',
        'gameKeyId == Attack || gameKeyId == Defend',
        'VoidstepWheelRuntime.ShouldSuppress(InputKey.RightMouseButton)',
        'nameof(InputContext.IsGameKeyPressed)',
        'nameof(InputContext.GetGameKeyState)')),
    'selection casts only after confirmation': '_manager.TryActivate(ability);' in selection and 'internal bool Confirm()' in selection and '_manager.TryActivate(ability.Value)' not in mission,
    'Blink targeting begins on selection but animation waits for confirmation': '_manager.TryActivate(AbilityId.Blink)' in selection and 'enteringBlinkTargeting' in cast_animation and 'confirmingBlink' in cast_animation,
    'registry and standalone prefab share exact six-slot order':
        registered_abilities == ability_order and prefab_bindings == prefab_order,
}

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(('PASS' if passed else 'FAIL') + ': ' + name)
if failed:
    print('Failed wheel invariants: ' + ', '.join(failed), file=sys.stderr)
    raise SystemExit(1)
print(f'Validated {len(checks)} focused wheel and TOR integration invariants.')
