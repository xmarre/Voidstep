#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]


def read(relative):
    return (root / relative).read_text(encoding='utf-8')


coordinator = read('src/Voidstep/AbilityWheelCoordinator.cs')
tor = read('src/Voidstep/TorAbilityWheelAdapter.cs')
selection = read('src/Voidstep/AbilitySelectionController.cs')
mission = read('src/Voidstep/VoidstepMissionBehavior.cs')
suppression = read('src/Voidstep/AbilityWheelInputSuppressionPatch.cs')
mission_input = read('src/Voidstep/MissionOrderInputSuppression.cs')
cast_animation = read('src/Voidstep/AbilityCastAnimationPatch.cs')
standalone = read('src/Voidstep/StandaloneAbilityWheel.cs')
runtime_bridge = read('src/Voidstep/VoidstepWheelRuntime.cs')
project = read('src/Voidstep/Voidstep.csproj')
prefab = read('module/Voidstep/GUI/Prefabs/VoidstepAbilityWheel.xml')

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
        '_available = false;',
        'InjectProxies(agent);',
        '_available = true;')),
    'standalone wheel remains active until TOR injection succeeds': all(token in coordinator for token in (
        'if (!_tor.IsAvailable)',
        '_standalone.Tick();',
        'HandleWheelAvailabilityTransition();',
        '_standalone.Cleanup();')) and 'standalone wheel remains active' in tor,
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
    'TOR guard affects only owned proxies': tor.count('runtime.IsTorProxy(__instance)') >= 3 and
        'if (runtime == null || !runtime.IsTorProxy(__instance))\n                return true;' in tor and
        'return runtime == null || !runtime.IsTorProxy(__instance);' in tor,
    'TOR spell override is separately guarded': 'spellDoCast' in tor and '_harmony.Patch(spellDoCast, prefix: doCastPrefix);' in tor,
    'TOR patches are removed on mission cleanup': '_harmony?.UnpatchAll(HarmonyId)' in tor and 'RemoveInjectedProxies();' in tor,
    'TOR remains an optional reflection boundary': 'using TOR_Core' not in tor and '<Reference Include="TOR_Core"' not in project and 'AppDomain.CurrentDomain.GetAssemblies()' in tor,
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
    'wheel suppression does not suppress Voidstep own polling': 'InputConflictSuppression.IsBypassed' in suppression and 'VoidstepWheelRuntime.ShouldSuppress(__0)' in suppression,
    'selection casts only after confirmation': '_manager.TryActivate(ability);' in selection and 'internal bool Confirm()' in selection and '_manager.TryActivate(ability.Value)' not in mission,
    'Blink targeting begins on selection but animation waits for confirmation': '_manager.TryActivate(AbilityId.Blink)' in selection and 'enteringBlinkTargeting' in cast_animation and 'confirmingBlink' in cast_animation,
    'standalone wheel owns six radial segments': 'Math.PI / 3.0' in standalone and 'VoidstepInputBindings.Abilities[selected]' in standalone,
    'standalone prefab exposes all six entries': all(token in prefab for token in (
        '@CleaveText', '@BlinkText', '@WindblastText', '@BendTimeText', '@DominoText', '@DarkVisionText')),
    'wheel bridge is attached and detached per mission': 'VoidstepWheelRuntime.Attach(this);' in coordinator and 'VoidstepWheelRuntime.Detach(this);' in coordinator and 'private static AbilityWheelCoordinator _current;' in runtime_bridge,
}

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(('PASS' if passed else 'FAIL') + ': ' + name)
if failed:
    print('Failed wheel invariants: ' + ', '.join(failed), file=sys.stderr)
    raise SystemExit(1)
print(f'Validated {len(checks)} wheel and TOR integration invariants.')
