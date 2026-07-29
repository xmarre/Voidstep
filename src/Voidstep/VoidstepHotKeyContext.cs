using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class VoidstepHotKeyContext : GameKeyContext
    {
        internal const string CategoryId = "VoidstepHotKeyCategory";
        internal const string VoidstepCleave = "VoidstepCleave";
        internal const string Blink = "Blink";
        internal const string Windblast = "Windblast";
        internal const string BendTime = "BendTime";
        internal const string Domino = "Domino";
        internal const string DarkVision = "DarkVision";

        internal static VoidstepHotKeyContext Current { get; private set; }

        private VoidstepHotKeyContext()
            : base(CategoryId, 0, GameKeyContextType.AuxiliarySerializedAndShownInOptions)
        {
            RegisterHotKey(new HotKey(VoidstepCleave, CategoryId, InputKey.D1));
            RegisterHotKey(new HotKey(Blink, CategoryId, InputKey.D2));
            RegisterHotKey(new HotKey(Windblast, CategoryId, InputKey.D3));
            RegisterHotKey(new HotKey(BendTime, CategoryId, InputKey.D4));
            RegisterHotKey(new HotKey(Domino, CategoryId, InputKey.D5));
            RegisterHotKey(new HotKey(DarkVision, CategoryId, InputKey.D6));
        }

        internal static bool TryRegister(VoidstepLogger logger)
        {
            if (Current != null)
                return true;

            try
            {
                foreach (var category in HotKeyManager.GetAllCategories())
                {
                    if (!string.Equals(category.GameKeyCategoryId, CategoryId, StringComparison.Ordinal))
                        continue;

                    Current = category as VoidstepHotKeyContext;
                    if (Current == null)
                        throw new InvalidOperationException("Another hotkey category already uses the Voidstep category ID.");
                    return true;
                }

                RegisterLocalizedText();
                var context = new VoidstepHotKeyContext();
                HotKeyManager.RegisterContext(context, false, true);
                Current = context;
                logger?.Info("Native configurable Voidstep hotkeys registered under Options > Keybindings > Voidstep.");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Error("Native Voidstep hotkey registration failed.", ex);
                Current = null;
                return false;
            }
        }

        internal static HotKey Get(AbilityId ability)
        {
            var context = Current;
            return context?.GetHotKey(GetId(ability));
        }

        internal static string GetId(AbilityId ability)
        {
            switch (ability)
            {
                case AbilityId.VoidstepCleave: return VoidstepCleave;
                case AbilityId.Blink: return Blink;
                case AbilityId.Windblast: return Windblast;
                case AbilityId.BendTime: return BendTime;
                case AbilityId.Domino: return Domino;
                case AbilityId.DarkVision: return DarkVision;
                default: throw new ArgumentOutOfRangeException(nameof(ability), ability, null);
            }
        }

        private static void RegisterLocalizedText()
        {
            var module = Module.CurrentModule;
            if (module == null)
                throw new InvalidOperationException("Bannerlord module text manager is not available yet.");

            var tags = new List<GameTextManager.ChoiceTag>();
            var textManager = module.GlobalTextManager;
            textManager.AddGameText("str_hotkey_category_name")
                .AddVariationWithId(CategoryId, new TextObject("Voidstep"), tags);

            RegisterText(textManager, tags, VoidstepCleave, "Voidstep Cleave", "Primary key for Voidstep Cleave. Configure its modifier in MCM.");
            RegisterText(textManager, tags, Blink, "Blink", "Primary key for Blink. Configure its modifier in MCM.");
            RegisterText(textManager, tags, Windblast, "Windblast", "Primary key for Windblast. Configure its modifier in MCM.");
            RegisterText(textManager, tags, BendTime, "Bend Time", "Primary key for Bend Time. Configure its modifier in MCM.");
            RegisterText(textManager, tags, Domino, "Domino", "Primary key for Domino. Configure its modifier in MCM.");
            RegisterText(textManager, tags, DarkVision, "Dark Vision", "Primary key for Dark Vision. Configure its modifier in MCM.");
        }

        private static void RegisterText(GameTextManager textManager, List<GameTextManager.ChoiceTag> tags, string id, string name, string description)
        {
            var variationId = CategoryId + "_" + id;
            textManager.AddGameText("str_hotkey_name")
                .AddVariationWithId(variationId, new TextObject(name), tags);
            textManager.AddGameText("str_hotkey_description")
                .AddVariationWithId(variationId, new TextObject(description), tags);
        }
    }
}
