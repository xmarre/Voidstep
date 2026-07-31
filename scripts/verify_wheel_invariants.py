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
tor_latch = read('src/Voidstep/TorProxySelectionAttemptLatch.cs')
tor_stance = read('src/Voidstep/TorProxyCastStanceFix.cs')
post_cast = read('src/Voidstep/PostCastOrientationOwnershipFix.cs')
teleport = read('src/Voidstep/PreservedFrameTeleportRuntime.cs')
selection = read('src/Voidstep/AbilitySelectionController.cs')
mission = read('src/Voidstep/VoidstepMissionBehavior.cs')
mission_input = read('src/Voidstep/MissionOrderInputSuppression.cs')
cast_animation = read('src/Voidstep/AbilityCastAnimationPatch.cs')
bindings = read('src/Voidstep/VoidstepInputBindings.cs')
project = read('src/Voidstep/Voidstep.csproj')
prefab = read('module/Voidstep/GUI/Prefabs/VoidstepAbilityWheel.xml')
tor_compact = compact(tor)
all_runtime = '\n'.join(path.read_text(encoding='utf-8') for path in (root / 'src' / 'Voidstep').glob('*.cs'))

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
    'coordinator passes mission delta time to TOR adapter':
        '_tor.Tick(dt);' in coordinator and '_tor.Tick();' not in coordinator,

    'TOR attachment retries are throttled':
        'private const float AttachRetryInterval = 0.5f;' in tor and
        '_attachRetryRemaining -= Math.Max(0f, dt);' in tor and
        '_attachRetryRemaining = AttachRetryInterval;' in tor,

    'standalone remains active until TOR injection succeeds':
        'if (!_tor.IsAvailable)' in coordinator and
        '_standalone.Tick();' in coordinator and
        'HandleWheelAvailabilityTransition();' in coordinator,

    'standalone overlay owns no mission input':
        'display-only Gauntlet layer' in standalone and
        all(token not in standalone for token in (
            'IsFocusLayer', 'ConfigureInputRestrictions', 'SetInputRestrictions',
            'TrySetFocus', 'TryLoseFocus', 'InputUsageMask')),

    'wheel suppression never intercepts mouse wheel':
        all(token not in coordinator + wheel_suppression for token in (
            'MouseScrollUp', 'MouseScrollDown', 'MouseScrollAxis')),

    'TOR opening cancels stale selection':
        'state == 1 && _lastState != 1 && _selection.HasSelection' in tor and
        '_selection.Cancel(true);' in tor,

    'TOR failed selection attempts once per targeting session':
        '[HarmonyPatch(typeof(TorAbilityWheelAdapter), nameof(TorAbilityWheelAdapter.Tick))]' in tor_latch and
        'torState != 2' in tor_latch and
        '!__instance.TryGetProxyAbility(proxy, out ability)' in tor_latch and
        'state.Attempted && state.Ability == ability' in tor_latch and
        'ReferenceEquals(state.Proxy, proxy)' in tor_latch and
        'States.Remove(adapter);' in tor_latch,

    'TOR proxies receive stable donor icons':
        'ResolveDonorSprites();' in tor and
        '_donorSprites[i % _donorSprites.Count]' in tor and
        'while (_donorSprites.Count < VoidstepInputBindings.Abilities.Length)' in tor,

    'TOR proxy cast path is guarded':
        'PatchProxyGuards();' in tor and
        'GetMethod("TryCast"' in tor and
        'GetMethod("DoCast"' in tor and
        '_harmony.Patch(tryCast, prefix: tryCastPrefix);' in tor and
        '_harmony.Patch(baseDoCast, prefix: doCastPrefix);' in tor,

    'TOR guard affects only owned proxies':
        tor_compact.count('runtime.IsTorProxy(__instance)') >= 3 and
        'if (runtime == null || !runtime.IsTorProxy(__instance)) return true;' in tor_compact,

    'TOR cleanup removes patches and proxies':
        '_harmony?.UnpatchAll(HarmonyId)' in tor and
        'DeactivateLiveAttachment(true);' in tor and
        'RemoveInjectedProxies();' in tor,

    'TOR remains an optional reflection boundary':
        'using TOR_Core' not in tor + tor_stance and
        '<Reference Include="TOR_Core"' not in project and
        'AppDomain.CurrentDomain.GetAssemblies()' in tor,

    'TOR template enums are semantic':
        'ParseEnumValue("TOR_Core.AbilitySystem.AbilityType", "Spell")' in tor and
        'ParseEnumValue("TOR_Core.AbilitySystem.AbilityTargetType", "WorldPosition")' in tor and
        'ParseEnumValue("TOR_Core.AbilitySystem.CastType", "Instant")' in tor and
        'Enum.ToObject' not in tor,

    'TOR presentation integration is selection only':
        'selection-only' in tor_stance and
        'Agent.Main' not in tor_stance and
        'SetActionChannel' not in tor_stance and
        'SetCurrentActionSpeed' not in tor_stance and
        'LookDirection =' not in tor_stance,

    'Blink and Cleave use one scoped native teleport boundary':
        '[HarmonyPatch(typeof(AbilityManager), "TeleportActor")]' in post_cast and
        'PreservedFrameTeleportRuntime.Teleport(' in post_cast and
        'AccessTools.Field(typeof(MBAPI), "IMBAgent")' in teleport and
        'NativeSetPositionMethod.Invoke(api, arguments);' in teleport and
        'nameof(BodyAlignedCleaveRuntime.TeleportPositionOnly)' in teleport,

    'teleport avoids frame and orientation writes':
        'SetInitialFrame' not in teleport and
        'TeleportToPosition' not in teleport and
        'SetScriptedPosition' not in teleport and
        'LookDirection =' not in teleport and
        'SetMovementDirection' not in teleport and
        'SetEventControlFlags' not in teleport and
        'SetActionChannel' not in teleport,

    'mounted teleport preserves rider offset':
        'riderOffset = actorPosition - mountPosition;' in teleport and
        'riderTarget = destination + riderOffset;' in teleport and
        'SetNativePosition(mount, mountTarget)' in teleport and
        'SetNativePosition(actor, riderTarget)' in teleport,

    'no global Agent Harmony target exists':
        '[HarmonyPatch(typeof(Agent)' not in all_runtime and
        'Agent.Main' not in all_runtime,

    'right mouse confirmation uses input bypass':
        'Input.IsKeyPressed(InputKey.RightMouseButton)' in coordinator and
        'InputConflictSuppression.EnterBypass()' in coordinator,

    'right mouse attempts only once per physical press':
        'if (!confirm || _suppressRightMouseUntilRelease)' in coordinator and
        '_suppressRightMouseUntilRelease = true;' in coordinator and
        'same physical press can call TryActivate every frame' in coordinator,

    'right mouse remains suppressed through release':
        '_selection.HasSelection || _suppressRightMouseUntilRelease' in coordinator and
        'Input.IsKeyReleased(InputKey.RightMouseButton)' in coordinator and
        '!Input.IsKeyDownImmediate(InputKey.RightMouseButton)' in coordinator,

    'native attack and defend are suppressed only during Mouse2 ownership':
        'private const int Attack = 9;' in mission_input and
        'private const int Defend = 10;' in mission_input and
        'VoidstepWheelRuntime.ShouldSuppress(InputKey.RightMouseButton)' in mission_input,

    'selection casts only after confirmation':
        'internal bool Confirm()' in selection and
        '_manager.TryActivate(ability);' in selection and
        '_manager.TryActivate(ability.Value)' not in mission,

    'Blink owns targeting without generic cast animation':
        '_manager.TryActivate(AbilityId.Blink)' in selection and
        'var blinkOwnsItsPresentation = ability == AbilityId.Blink;' in cast_animation,

    'registry and prefab share exact six-slot order':
        registered_abilities == ability_order and prefab_bindings == prefab_order,
}

failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(('PASS' if passed else 'FAIL') + ': ' + name)
if failed:
    print('Failed wheel invariants: ' + ', '.join(failed), file=sys.stderr)
    raise SystemExit(1)
print(f'Validated {len(checks)} focused wheel, TOR session and native-teleport invariants.')
