using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
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

        private static readonly object CacheSync = new object();
        private static BindingCache _cache = BindingCache.Empty;
        private static int _cacheDirty = 1;
        private static bool _keybindEventsAttached;

        internal static bool IsCacheDirty => Volatile.Read(ref _cacheDirty) != 0;

        internal static void AttachKeybindEvents()
        {
            lock (CacheSync)
            {
                if (!_keybindEventsAttached)
                {
                    HotKeyManager.OnKeybindsChanged += Invalidate;
                    _keybindEventsAttached = true;
                }
                Volatile.Write(ref _cacheDirty, 1);
            }
        }

        internal static void DetachKeybindEvents()
        {
            lock (CacheSync)
            {
                if (_keybindEventsAttached)
                {
                    HotKeyManager.OnKeybindsChanged -= Invalidate;
                    _keybindEventsAttached = false;
                }
                Volatile.Write(ref _cache, BindingCache.Empty);
                Volatile.Write(ref _cacheDirty, 1);
            }
        }

        internal static bool RefreshCacheIfChanged()
        {
            lock (CacheSync)
            {
                var current = Volatile.Read(ref _cache);
                var dirty = Interlocked.Exchange(ref _cacheDirty, 0) != 0;
                if (!dirty && CacheMatchesCurrent(current))
                    return false;

                Volatile.Write(ref _cache, BuildCache());
                return true;
            }
        }

        internal static bool TryGetPressedKey(AbilityId ability, out InputKey pressedKey)
        {
            pressedKey = InputKey.Invalid;
            var cache = Volatile.Read(ref _cache);
            var entry = cache.Get(ability);
            if (entry == null)
                return false;

            var currentModifiers = InputConflictSuppression.CurrentModifiers;
            for (var i = 0; i < entry.Keys.Length; i++)
            {
                var inputKey = entry.Keys[i];
                if (!ModifiersMatch(entry.Modifiers, currentModifiers, inputKey))
                    continue;
                if (cache.AmbiguousChords.Contains(ChordCode(entry.Modifiers, inputKey)))
                    continue;

                using (InputConflictSuppression.EnterBypass())
                {
                    if (!Input.IsKeyPressed(inputKey))
                        continue;
                }

                pressedKey = inputKey;
                return true;
            }
            return false;
        }

        internal static bool IsChordActiveForKey(InputKey inputKey)
        {
            if (inputKey == InputKey.Invalid)
                return false;

            var cache = Volatile.Read(ref _cache);
            if (!cache.BoundPrimaryKeys.Contains(inputKey))
                return false;

            var currentModifiers = InputConflictSuppression.CurrentModifiers;
            for (var i = 0; i < cache.Entries.Length; i++)
            {
                var entry = cache.Entries[i];
                if (!ContainsKey(entry.Keys, inputKey))
                    continue;
                if (!ModifiersMatch(entry.Modifiers, currentModifiers, inputKey))
                    continue;
                if (cache.AmbiguousChords.Contains(ChordCode(entry.Modifiers, inputKey)))
                    continue;
                return true;
            }
            return false;
        }

        internal static bool IsBoundPrimaryKey(InputKey inputKey)
        {
            return inputKey != InputKey.Invalid &&
                   Volatile.Read(ref _cache).BoundPrimaryKeys.Contains(inputKey);
        }

        internal static string GetSummary()
        {
            return Volatile.Read(ref _cache).Summary;
        }

        internal static string GetConflictWarning()
        {
            return Volatile.Read(ref _cache).ConflictWarning;
        }

        internal static string FormatBinding(AbilityId ability)
        {
            var entry = Volatile.Read(ref _cache).Get(ability);
            return entry?.Display ?? "<unbound>";
        }

        private static void Invalidate()
        {
            Volatile.Write(ref _cacheDirty, 1);
        }

        private static bool CacheMatchesCurrent(BindingCache cache)
        {
            if (cache == null || cache.Entries.Length != Abilities.Length)
                return false;

            for (var i = 0; i < Abilities.Length; i++)
            {
                var entry = cache.Entries[i];
                if (entry.Ability != Abilities[i] || entry.Modifiers != ReadConfiguredModifiers(entry.Ability))
                    return false;
                if (!KeysMatchCurrent(entry.Ability, entry.Keys))
                    return false;
            }
            return true;
        }

        private static bool KeysMatchCurrent(AbilityId ability, InputKey[] cachedKeys)
        {
            var hotKey = VoidstepHotKeyContext.Get(ability);
            if (hotKey == null)
                return cachedKeys.Length == 0;

            var cachedIndex = 0;
            for (var i = 0; i < hotKey.Keys.Count; i++)
            {
                var key = hotKey.Keys[i];
                if (key == null || key.InputKey == InputKey.Invalid)
                    continue;
                if (cachedIndex >= cachedKeys.Length || cachedKeys[cachedIndex] != key.InputKey)
                    return false;
                cachedIndex++;
            }
            return cachedIndex == cachedKeys.Length;
        }

        private static BindingCache BuildCache()
        {
            var entries = new BindingEntry[Abilities.Length];
            var boundPrimaryKeys = new HashSet<InputKey>();
            for (var i = 0; i < Abilities.Length; i++)
            {
                var ability = Abilities[i];
                var keys = ReadCurrentKeys(ability);
                for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                    boundPrimaryKeys.Add(keys[keyIndex]);

                entries[i] = new BindingEntry(
                    ability,
                    ReadConfiguredModifiers(ability),
                    keys);
            }

            var ambiguousChords = new HashSet<long>();
            string conflictWarning = null;
            for (var i = 0; i < entries.Length; i++)
            {
                var first = entries[i];
                for (var j = i + 1; j < entries.Length; j++)
                {
                    var second = entries[j];
                    if (first.Modifiers != second.Modifiers)
                        continue;

                    for (var keyIndex = 0; keyIndex < first.Keys.Length; keyIndex++)
                    {
                        var inputKey = first.Keys[keyIndex];
                        if (!ContainsKey(second.Keys, inputKey))
                            continue;

                        ambiguousChords.Add(ChordCode(first.Modifiers, inputKey));
                        if (conflictWarning == null)
                        {
                            conflictWarning =
                                $"Duplicate Voidstep chord {FormatModifiers(first.Modifiers)}{KeyName(inputKey)}: " +
                                $"{AbilityName(first.Ability)} and {AbilityName(second.Ability)}. " +
                                "Rebind one primary key in Options > Keybindings > Voidstep or change one modifier in MCM. " +
                                "The duplicate ability chord is disabled and the native game action remains available.";
                        }
                    }
                }
            }

            var summaryParts = new string[entries.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                entries[i].Display = FormatModifiers(entries[i].Modifiers) +
                                     (entries[i].Keys.Length > 0 ? KeyName(entries[i].Keys[0]) : "<unbound>");
                summaryParts[i] = AbilityName(entries[i].Ability) + "=" + entries[i].Display;
            }

            return new BindingCache(
                entries,
                boundPrimaryKeys,
                ambiguousChords,
                string.Join(", ", summaryParts),
                conflictWarning);
        }

        private static InputKey[] ReadCurrentKeys(AbilityId ability)
        {
            var hotKey = VoidstepHotKeyContext.Get(ability);
            if (hotKey == null)
                return Array.Empty<InputKey>();

            var keys = new List<InputKey>(hotKey.Keys.Count);
            for (var i = 0; i < hotKey.Keys.Count; i++)
            {
                var key = hotKey.Keys[i];
                if (key != null && key.InputKey != InputKey.Invalid)
                    keys.Add(key.InputKey);
            }
            return keys.ToArray();
        }

        private static VoidstepModifiers ReadConfiguredModifiers(AbilityId ability)
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

        private static bool ModifiersMatch(VoidstepModifiers required, VoidstepModifiers current, InputKey primaryKey)
        {
            return (current & ~ModifierForPrimaryKey(primaryKey)) == required;
        }

        private static VoidstepModifiers ModifierForPrimaryKey(InputKey inputKey)
        {
            if (inputKey == InputKey.LeftControl || inputKey == InputKey.RightControl)
                return VoidstepModifiers.Control;
            if (inputKey == InputKey.LeftAlt || inputKey == InputKey.RightAlt)
                return VoidstepModifiers.Alt;
            if (inputKey == InputKey.LeftShift || inputKey == InputKey.RightShift)
                return VoidstepModifiers.Shift;
            return VoidstepModifiers.None;
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

        private static bool ContainsKey(InputKey[] keys, InputKey inputKey)
        {
            for (var i = 0; i < keys.Length; i++)
            {
                if (keys[i] == inputKey)
                    return true;
            }
            return false;
        }

        private static long ChordCode(VoidstepModifiers modifiers, InputKey inputKey)
        {
            return ((long)(int)modifiers << 32) | (uint)(int)inputKey;
        }

        private static string KeyName(InputKey inputKey)
        {
            return new Key(inputKey).ToString();
        }

        private static string FormatModifiers(VoidstepModifiers modifiers)
        {
            var text = string.Empty;
            if (modifiers.HasFlag(VoidstepModifiers.Control)) text += "Ctrl+";
            if (modifiers.HasFlag(VoidstepModifiers.Alt)) text += "Alt+";
            if (modifiers.HasFlag(VoidstepModifiers.Shift)) text += "Shift+";
            return text;
        }

        private static string AbilityName(AbilityId ability)
        {
            switch (ability)
            {
                case AbilityId.VoidstepCleave: return "Voidstep Cleave";
                case AbilityId.Blink: return "Blink";
                case AbilityId.Windblast: return "Windblast";
                case AbilityId.BendTime: return "Bend Time";
                case AbilityId.Domino: return "Domino";
                case AbilityId.DarkVision: return "Dark Vision";
                default: return ability.ToString();
            }
        }

        private sealed class BindingEntry
        {
            internal BindingEntry(AbilityId ability, VoidstepModifiers modifiers, InputKey[] keys)
            {
                Ability = ability;
                Modifiers = modifiers;
                Keys = keys;
                Display = "<unbound>";
            }

            internal AbilityId Ability { get; }
            internal VoidstepModifiers Modifiers { get; }
            internal InputKey[] Keys { get; }
            internal string Display { get; set; }
        }

        private sealed class BindingCache
        {
            internal static readonly BindingCache Empty = new BindingCache(
                Array.Empty<BindingEntry>(),
                new HashSet<InputKey>(),
                new HashSet<long>(),
                "<bindings unavailable>",
                null);

            internal BindingCache(
                BindingEntry[] entries,
                HashSet<InputKey> boundPrimaryKeys,
                HashSet<long> ambiguousChords,
                string summary,
                string conflictWarning)
            {
                Entries = entries;
                BoundPrimaryKeys = boundPrimaryKeys;
                AmbiguousChords = ambiguousChords;
                Summary = summary;
                ConflictWarning = conflictWarning;
            }

            internal BindingEntry[] Entries { get; }
            internal HashSet<InputKey> BoundPrimaryKeys { get; }
            internal HashSet<long> AmbiguousChords { get; }
            internal string Summary { get; }
            internal string ConflictWarning { get; }

            internal BindingEntry Get(AbilityId ability)
            {
                for (var i = 0; i < Entries.Length; i++)
                {
                    if (Entries[i].Ability == ability)
                        return Entries[i];
                }
                return null;
            }
        }
    }

    internal static class InputConflictSuppression
    {
        [ThreadStatic]
        private static int _bypassDepth;

        private static readonly ConcurrentDictionary<InputKey, byte> LatchedKeys =
            new ConcurrentDictionary<InputKey, byte>();

        private static int _currentModifiers;
        private static int _modifierSnapshotReady;

        internal static bool IsBypassed => _bypassDepth > 0;

        internal static VoidstepModifiers CurrentModifiers
        {
            get
            {
                if (Volatile.Read(ref _modifierSnapshotReady) == 0)
                    CaptureCurrentModifiers();
                return (VoidstepModifiers)Volatile.Read(ref _currentModifiers);
            }
        }

        internal static BypassScope EnterBypass()
        {
            _bypassDepth++;
            return new BypassScope();
        }

        internal static void CaptureCurrentModifiers()
        {
            var result = VoidstepModifiers.None;
            using (EnterBypass())
            {
                if (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl))
                    result |= VoidstepModifiers.Control;
                if (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt))
                    result |= VoidstepModifiers.Alt;
                if (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift))
                    result |= VoidstepModifiers.Shift;
            }

            Volatile.Write(ref _currentModifiers, (int)result);
            Volatile.Write(ref _modifierSnapshotReady, 1);
        }

        internal static void Latch(InputKey inputKey)
        {
            if (inputKey != InputKey.Invalid)
                LatchedKeys.TryAdd(inputKey, 0);
        }

        internal static void RefreshLatches()
        {
            if (LatchedKeys.IsEmpty)
                return;

            using (EnterBypass())
            {
                foreach (var inputKey in LatchedKeys.Keys)
                {
                    if (!Input.IsKeyPressed(inputKey) && !Input.IsKeyDown(inputKey) &&
                        !Input.IsKeyDownImmediate(inputKey) && !Input.IsKeyReleased(inputKey))
                    {
                        byte ignored;
                        LatchedKeys.TryRemove(inputKey, out ignored);
                    }
                }
            }
        }

        internal static void Reset()
        {
            LatchedKeys.Clear();
            Volatile.Write(ref _currentModifiers, 0);
            Volatile.Write(ref _modifierSnapshotReady, 0);
        }

        internal static bool ShouldSuppress(InputKey inputKey)
        {
            if (IsBypassed || inputKey == InputKey.Invalid)
                return false;
            if (LatchedKeys.ContainsKey(inputKey))
                return true;
            if (!VoidstepInputBindings.IsBoundPrimaryKey(inputKey))
                return false;
            if (!RuntimeCanSuppress())
                return false;
            if (!VoidstepInputBindings.IsChordActiveForKey(inputKey))
                return false;

            LatchedKeys.TryAdd(inputKey, 0);
            return true;
        }

        private static bool RuntimeCanSuppress()
        {
            if (!VoidstepSubModule.InputSuppressionReady || !VoidstepSubModule.NativeHotkeysReady ||
                Input.IsOnScreenKeyboardActive || !VoidstepSettings.Current.Enabled)
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

    [HarmonyPatch(typeof(Input), nameof(Input.UpdateKeyData), typeof(byte[]))]
    internal static class RawInputFrameSnapshotPatch
    {
        private static void Postfix()
        {
            InputConflictSuppression.CaptureCurrentModifiers();
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
            if ((__result.x != 0f || __result.y != 0f) &&
                InputConflictSuppression.ShouldSuppress(__0))
            {
                __result = Vec2.Zero;
            }
        }
    }
}
