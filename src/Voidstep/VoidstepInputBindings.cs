using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MCM.Common;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    [Flags]
    internal enum VoidstepModifiers
    {
        None = 0,
        Control = 1,
        Alt = 2,
        Shift = 4
    }

    internal static class VoidstepInputBindings
    {
        internal static readonly AbilityId[] Abilities =
        {
            AbilityId.VoidstepCleave,
            AbilityId.Blink,
            AbilityId.Windblast,
            AbilityId.BendTime,
            AbilityId.Domino,
            AbilityId.DarkVision
        };

        internal static bool TryGetPressedKey(AbilityId ability, out InputKey pressedKey)
        {
            pressedKey = InputKey.Invalid;
            var hotKey = VoidstepHotKeyContext.Get(ability);
            if (hotKey == null)
                return false;

            using (InputConflictSuppression.EnterBypass())
            {
                if (!ModifiersSatisfied(GetModifiers(ability)))
                    return false;

                for (var i = 0; i < hotKey.Keys.Count; i++)
                {
                    var key = hotKey.Keys[i];
                    if (key == null || key.InputKey == InputKey.Invalid)
                        continue;
                    if (!Input.IsKeyPressed(key.InputKey))
                        continue;

                    pressedKey = key.InputKey;
                    return true;
                }
            }
            return false;
        }

        internal static bool IsChordActiveForKey(InputKey inputKey)
        {
            if (inputKey == InputKey.Invalid || IsModifierKey(inputKey))
                return false;

            for (var i = 0; i < Abilities.Length; i++)
            {
                var ability = Abilities[i];
                var hotKey = VoidstepHotKeyContext.Get(ability);
                if (hotKey == null || !ContainsKey(hotKey, inputKey))
                    continue;
                if (!ModifiersSatisfied(GetModifiers(ability)))
                    continue;
                if (Input.IsKeyPressed(inputKey) || Input.IsKeyDown(inputKey) || Input.IsKeyDownImmediate(inputKey) || Input.IsKeyReleased(inputKey))
                    return true;
            }
            return false;
        }

        internal static string GetSummary()
        {
            var parts = new string[Abilities.Length];
            for (var i = 0; i < Abilities.Length; i++)
                parts[i] = FormatBinding(Abilities[i]);
            return string.Join(", ", parts);
        }

        internal static string FormatBinding(AbilityId ability)
        {
            var hotKey = VoidstepHotKeyContext.Get(ability);
            var keyName = "<unbound>";
            if (hotKey != null)
            {
                for (var i = 0; i < hotKey.Keys.Count; i++)
                {
                    var key = hotKey.Keys[i];
                    if (key == null || key.InputKey == InputKey.Invalid || key.IsControllerInput)
                        continue;
                    keyName = key.ToString();
                    break;
                }
            }

            var modifier = GetModifiers(ability);
            return FormatModifiers(modifier) + keyName;
        }

        internal static VoidstepModifiers GetModifiers(AbilityId ability)
        {
            var settings = VoidstepSettings.Current;
            switch (ability)
            {
                case AbilityId.VoidstepCleave: return ParseModifiers(settings.VoidstepModifier);
                case AbilityId.Blink: return ParseModifiers(settings.BlinkModifier);
                case AbilityId.Windblast: return ParseModifiers(settings.WindblastModifier);
                case AbilityId.BendTime: return ParseModifiers(settings.BendTimeModifier);
                case AbilityId.Domino: return ParseModifiers(settings.DominoModifier);
                case AbilityId.DarkVision: return ParseModifiers(settings.DarkVisionModifier);
                default: return VoidstepModifiers.None;
            }
        }

        private static bool ContainsKey(HotKey hotKey, InputKey inputKey)
        {
            for (var i = 0; i < hotKey.Keys.Count; i++)
            {
                var key = hotKey.Keys[i];
                if (key != null && key.InputKey == inputKey)
                    return true;
            }
            return false;
        }

        private static bool ModifiersSatisfied(VoidstepModifiers modifiers)
        {
            var control = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            var alt = Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt);
            var shift = Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift);
            return (!modifiers.HasFlag(VoidstepModifiers.Control) || control) &&
                   (!modifiers.HasFlag(VoidstepModifiers.Alt) || alt) &&
                   (!modifiers.HasFlag(VoidstepModifiers.Shift) || shift);
        }

        private static VoidstepModifiers ParseModifiers(Dropdown<string> setting)
        {
            if (setting == null || setting.Count == 0)
                return VoidstepModifiers.Control;

            var value = setting.SelectedValue ?? "Control";
            var result = VoidstepModifiers.None;
            if (value.IndexOf("Control", StringComparison.OrdinalIgnoreCase) >= 0)
                result |= VoidstepModifiers.Control;
            if (value.IndexOf("Alt", StringComparison.OrdinalIgnoreCase) >= 0)
                result |= VoidstepModifiers.Alt;
            if (value.IndexOf("Shift", StringComparison.OrdinalIgnoreCase) >= 0)
                result |= VoidstepModifiers.Shift;
            return result;
        }

        private static string FormatModifiers(VoidstepModifiers modifiers)
        {
            var text = string.Empty;
            if (modifiers.HasFlag(VoidstepModifiers.Control)) text += "Ctrl+";
            if (modifiers.HasFlag(VoidstepModifiers.Alt)) text += "Alt+";
            if (modifiers.HasFlag(VoidstepModifiers.Shift)) text += "Shift+";
            return text;
        }

        private static bool IsModifierKey(InputKey inputKey) =>
            inputKey == InputKey.LeftControl || inputKey == InputKey.RightControl ||
            inputKey == InputKey.LeftAlt || inputKey == InputKey.RightAlt ||
            inputKey == InputKey.LeftShift || inputKey == InputKey.RightShift;
    }

    internal static class InputConflictSuppression
    {
        [ThreadStatic]
        private static int _bypassDepth;

        private static readonly HashSet<InputKey> LatchedKeys = new HashSet<InputKey>();
        private static readonly List<InputKey> ReleaseBuffer = new List<InputKey>(8);

        internal static bool IsBypassed => _bypassDepth > 0;

        internal static BypassScope EnterBypass()
        {
            _bypassDepth++;
            return new BypassScope();
        }

        internal static void Latch(InputKey inputKey)
        {
            if (inputKey != InputKey.Invalid)
                LatchedKeys.Add(inputKey);
        }

        internal static void RefreshLatches()
        {
            if (LatchedKeys.Count == 0)
                return;

            ReleaseBuffer.Clear();
            using (EnterBypass())
            {
                foreach (var inputKey in LatchedKeys)
                {
                    if (!Input.IsKeyPressed(inputKey) && !Input.IsKeyDown(inputKey) &&
                        !Input.IsKeyDownImmediate(inputKey) && !Input.IsKeyReleased(inputKey))
                        ReleaseBuffer.Add(inputKey);
                }
            }

            for (var i = 0; i < ReleaseBuffer.Count; i++)
                LatchedKeys.Remove(ReleaseBuffer[i]);
            ReleaseBuffer.Clear();
        }

        internal static void Reset()
        {
            LatchedKeys.Clear();
            ReleaseBuffer.Clear();
        }

        internal static bool ShouldSuppress(InputKey inputKey)
        {
            if (IsBypassed || !RuntimeCanSuppress() || inputKey == InputKey.Invalid)
                return false;
            if (LatchedKeys.Contains(inputKey))
                return true;

            using (EnterBypass())
            {
                if (!VoidstepInputBindings.IsChordActiveForKey(inputKey))
                    return false;
            }

            LatchedKeys.Add(inputKey);
            return true;
        }

        private static bool RuntimeCanSuppress()
        {
            if (!VoidstepSubModule.InputSuppressionReady || !VoidstepSubModule.NativeHotkeysReady || Input.IsOnScreenKeyboardActive)
                return false;
            if (!VoidstepSettings.Current.Enabled)
                return false;

            var mission = Mission.Current;
            return mission != null && mission.IsLoadingFinished && !mission.MissionEnded && !mission.MissionIsEnding &&
                   mission.MainAgent != null && mission.MainAgent.IsActive();
        }

        internal readonly struct BypassScope : IDisposable
        {
            public void Dispose()
            {
                if (_bypassDepth > 0)
                    _bypassDepth--;
            }
        }
    }

    [HarmonyPatch]
    internal static class RawInputBooleanSuppressionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Input), nameof(Input.IsKeyPressed), new[] { typeof(InputKey) });
            yield return AccessTools.Method(typeof(Input), nameof(Input.IsKeyDown), new[] { typeof(InputKey) });
            yield return AccessTools.Method(typeof(Input), nameof(Input.IsKeyDownImmediate), new[] { typeof(InputKey) });
            yield return AccessTools.Method(typeof(Input), nameof(Input.IsKeyReleased), new[] { typeof(InputKey) });
        }

        private static void Postfix(InputKey __0, ref bool __result)
        {
            if (__result && InputConflictSuppression.ShouldSuppress(__0))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKeyState), typeof(InputKey))]
    internal static class RawInputAxisSuppressionPatch
    {
        private static void Postfix(InputKey __0, ref Vec2 __result)
        {
            if (InputConflictSuppression.ShouldSuppress(__0))
                __result = Vec2.Zero;
        }
    }
}
