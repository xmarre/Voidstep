using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;

namespace Voidstep
{
    internal sealed class VoidstepSettings : AttributeGlobalSettings<VoidstepSettings>
    {
        public override string Id => "Voidstep_v1";
        public override string DisplayName => "Voidstep — Arcane Melee Abilities";
        public override string FolderName => "Voidstep";
        public override string FormatType => "json2";

        private static readonly string[] KeyOptions =
        {
            "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D0",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
            "Z", "X", "C", "V", "B", "N", "M", "Q", "E",
            "Numpad1", "Numpad2", "Numpad3", "Numpad4", "Numpad5", "Numpad6"
        };

        internal static VoidstepSettings Current => Instance ?? Fallback;
        private static readonly VoidstepSettings Fallback = new VoidstepSettings();

        internal bool MigrateLegacyDefaultControls()
        {
            if (!RequireControlModifier ||
                Selected(VoidstepKey) != "D1" || Selected(BlinkKey) != "D2" ||
                Selected(WindblastKey) != "D3" || Selected(BendTimeKey) != "D4" ||
                Selected(DominoKey) != "D5" || Selected(DarkVisionKey) != "D6")
                return false;

            VoidstepKey = new Dropdown<string>(KeyOptions, 31);
            BlinkKey = new Dropdown<string>(KeyOptions, 32);
            WindblastKey = new Dropdown<string>(KeyOptions, 33);
            BendTimeKey = new Dropdown<string>(KeyOptions, 34);
            DominoKey = new Dropdown<string>(KeyOptions, 35);
            DarkVisionKey = new Dropdown<string>(KeyOptions, 36);
            RequireControlModifier = false;
            return true;
        }

        internal bool HasNumberRowConflict() =>
            IsNumberRow(VoidstepKey) || IsNumberRow(BlinkKey) || IsNumberRow(WindblastKey) ||
            IsNumberRow(BendTimeKey) || IsNumberRow(DominoKey) || IsNumberRow(DarkVisionKey);

        internal string GetControlSummary()
        {
            var prefix = RequireControlModifier ? "Ctrl+" : string.Empty;
            return $"{prefix}{Selected(VoidstepKey)}, {prefix}{Selected(BlinkKey)}, {prefix}{Selected(WindblastKey)}, " +
                   $"{prefix}{Selected(BendTimeKey)}, {prefix}{Selected(DominoKey)}, {prefix}{Selected(DarkVisionKey)}";
        }

        private static string Selected(Dropdown<string> setting) =>
            setting != null && setting.Count > 0 ? setting.SelectedValue : "<unset>";

        private static bool IsNumberRow(Dropdown<string> setting)
        {
            var value = Selected(setting);
            return value.Length == 2 && value[0] == 'D' && value[1] >= '0' && value[1] <= '9';
        }

        [SettingPropertyBool("Enable Voidstep", Order = 0, RequireRestart = false, HintText = "Master switch for all mission abilities.")]
        [SettingPropertyGroup("General", GroupOrder = 0)]
        public bool Enabled { get; set; } = true;

        [SettingPropertyBool("Debug logging", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("General")]
        public bool DebugLogging { get; set; } = false;

        [SettingPropertyBool("Camera shake", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("General")]
        public bool CameraShake { get; set; } = true;

        [SettingPropertyFloatingInteger("Effect intensity", 0f, 2f, "0.00", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("General")]
        public float EffectIntensity { get; set; } = 1f;

        [SettingPropertyDropdown("Voidstep Cleave key", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Controls", GroupOrder = 1)]
        public Dropdown<string> VoidstepKey { get; set; } = new Dropdown<string>(KeyOptions, 31);

        [SettingPropertyDropdown("Blink key", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Controls")]
        public Dropdown<string> BlinkKey { get; set; } = new Dropdown<string>(KeyOptions, 32);

        [SettingPropertyDropdown("Windblast key", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Controls")]
        public Dropdown<string> WindblastKey { get; set; } = new Dropdown<string>(KeyOptions, 33);

        [SettingPropertyDropdown("Bend Time key", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("Controls")]
        public Dropdown<string> BendTimeKey { get; set; } = new Dropdown<string>(KeyOptions, 34);

        [SettingPropertyDropdown("Domino key", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("Controls")]
        public Dropdown<string> DominoKey { get; set; } = new Dropdown<string>(KeyOptions, 35);

        [SettingPropertyDropdown("Dark Vision key", Order = 5, RequireRestart = false)]
        [SettingPropertyGroup("Controls")]
        public Dropdown<string> DarkVisionKey { get; set; } = new Dropdown<string>(KeyOptions, 36);

        [SettingPropertyBool("Require Ctrl modifier", Order = 6, RequireRestart = false, HintText = "Applies Ctrl to all selected ability keys. Number-row keys still trigger Bannerlord formation selection; use numpad or another unused key.")]
        [SettingPropertyGroup("Controls")]
        public bool RequireControlModifier { get; set; } = false;

        [SettingPropertyBool("Enable Void Energy", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Void Energy", GroupOrder = 2)]
        public bool EnergyEnabled { get; set; } = true;

        [SettingPropertyBool("Unlimited Void Energy", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Void Energy")]
        public bool UnlimitedEnergy { get; set; } = false;

        [SettingPropertyBool("Cooldown-only mode", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Void Energy")]
        public bool CooldownOnlyMode { get; set; } = false;

        [SettingPropertyFloatingInteger("Maximum energy", 10f, 500f, "0", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("Void Energy")]
        public float MaximumEnergy { get; set; } = 100f;

        [SettingPropertyFloatingInteger("Regeneration per second", 0f, 50f, "0.0", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("Void Energy")]
        public float EnergyRegeneration { get; set; } = 8f;

        [SettingPropertyFloatingInteger("Voidstep cost", 0f, 100f, "0", Order = 5, RequireRestart = false)]
        [SettingPropertyGroup("Void Energy")]
        public float VoidstepCost { get; set; } = 35f;

        [SettingPropertyFloatingInteger("Blink cost", 0f, 100f, "0", Order = 6, RequireRestart = false)]
        [SettingPropertyGroup("Void Energy")]
        public float BlinkCost { get; set; } = 18f;

        [SettingPropertyFloatingInteger("Windblast cost", 0f, 100f, "0", Order = 7, RequireRestart = false)]
        [SettingPropertyGroup("Void Energy")]
        public float WindblastCost { get; set; } = 24f;

        [SettingPropertyFloatingInteger("Bend Time cost", 0f, 100f, "0", Order = 8, RequireRestart = false)]
        [SettingPropertyGroup("Void Energy")]
        public float BendTimeCost { get; set; } = 40f;

        [SettingPropertyFloatingInteger("Domino cost", 0f, 100f, "0", Order = 9, RequireRestart = false)]
        [SettingPropertyGroup("Void Energy")]
        public float DominoCost { get; set; } = 30f;

        [SettingPropertyFloatingInteger("Dark Vision cost", 0f, 100f, "0", Order = 10, RequireRestart = false)]
        [SettingPropertyGroup("Void Energy")]
        public float DarkVisionCost { get; set; } = 8f;

        [SettingPropertyFloatingInteger("Cleave radius", 1f, 12f, "0.0 m", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Voidstep Cleave", GroupOrder = 3)]
        public float CleaveRadius { get; set; } = 4.8f;

        [SettingPropertyFloatingInteger("Cleave sweep", 180f, 360f, "0°", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Voidstep Cleave")]
        public float CleaveSweepDegrees { get; set; } = 340f;

        [SettingPropertyFloatingInteger("Damage multiplier", 0.1f, 5f, "0.00x", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Voidstep Cleave")]
        public float CleaveDamageMultiplier { get; set; } = 1.35f;

        [SettingPropertyFloatingInteger("Knockback", 0f, 20f, "0.0", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("Voidstep Cleave")]
        public float CleaveKnockback { get; set; } = 4f;

        [SettingPropertyFloatingInteger("Knockdown threshold", 0f, 100f, "0", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("Voidstep Cleave")]
        public float CleaveKnockdownThreshold { get; set; } = 45f;

        [SettingPropertyInteger("Maximum targets (0 = unlimited)", 0, 200, "0", Order = 5, RequireRestart = false)]
        [SettingPropertyGroup("Voidstep Cleave")]
        public int MaximumCleaveTargets { get; set; } = 0;

        [SettingPropertyBool("Friendly fire", Order = 6, RequireRestart = false)]
        [SettingPropertyGroup("Voidstep Cleave")]
        public bool CleaveFriendlyFire { get; set; } = false;

        [SettingPropertyBool("Target mounts", Order = 7, RequireRestart = false)]
        [SettingPropertyGroup("Voidstep Cleave")]
        public bool CleaveMounts { get; set; } = true;

        [SettingPropertyBool("Clockwise sweep", Order = 8, RequireRestart = false)]
        [SettingPropertyGroup("Voidstep Cleave")]
        public bool CleaveClockwise { get; set; } = true;

        [SettingPropertyBool("Snapshot targets at swing start", Order = 9, RequireRestart = false)]
        [SettingPropertyGroup("Voidstep Cleave")]
        public bool CleaveSnapshotTargets { get; set; } = false;

        [SettingPropertyFloatingInteger("Teleport range", 1f, 30f, "0.0 m", Order = 10, RequireRestart = false)]
        [SettingPropertyGroup("Voidstep Cleave")]
        public float VoidstepRange { get; set; } = 12f;

        [SettingPropertyFloatingInteger("Cooldown", 0f, 60f, "0.0 s", Order = 11, RequireRestart = false)]
        [SettingPropertyGroup("Voidstep Cleave")]
        public float VoidstepCooldown { get; set; } = 7f;

        [SettingPropertyFloatingInteger("Blink range", 1f, 30f, "0.0 m", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Blink", GroupOrder = 4)]
        public float BlinkRange { get; set; } = 9f;

        [SettingPropertyBool("Allow sealed-wall traversal", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Blink")]
        public bool BlinkThroughWalls { get; set; } = false;

        [SettingPropertyBool("Preserve momentum", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Blink")]
        public bool BlinkPreserveMomentum { get; set; } = false;

        [SettingPropertyBool("Slow time while aiming", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("Blink")]
        public bool BlinkAimSlowdown { get; set; } = true;

        [SettingPropertyFloatingInteger("Cooldown", 0f, 60f, "0.0 s", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("Blink")]
        public float BlinkCooldown { get; set; } = 3.5f;

        [SettingPropertyFloatingInteger("Cone angle", 10f, 160f, "0°", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Windblast", GroupOrder = 5)]
        public float WindblastAngle { get; set; } = 75f;

        [SettingPropertyFloatingInteger("Range", 1f, 30f, "0.0 m", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Windblast")]
        public float WindblastRange { get; set; } = 9f;

        [SettingPropertyFloatingInteger("Force", 0f, 30f, "0.0", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Windblast")]
        public float WindblastForce { get; set; } = 10f;

        [SettingPropertyFloatingInteger("Damage", 0f, 200f, "0", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("Windblast")]
        public float WindblastDamage { get; set; } = 15f;

        [SettingPropertyBool("Affect mounts", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("Windblast")]
        public bool WindblastMounts { get; set; } = true;

        [SettingPropertyBool("Affect projectiles", Order = 5, RequireRestart = false)]
        [SettingPropertyGroup("Windblast")]
        public bool WindblastProjectiles { get; set; } = false;

        [SettingPropertyFloatingInteger("Cooldown", 0f, 60f, "0.0 s", Order = 6, RequireRestart = false)]
        [SettingPropertyGroup("Windblast")]
        public float WindblastCooldown { get; set; } = 6f;

        [SettingPropertyFloatingInteger("Time factor", 0.02f, 1f, "0.00x", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Bend Time", GroupOrder = 6)]
        public float BendTimeFactor { get; set; } = 0.25f;

        [SettingPropertyFloatingInteger("Duration", 0.25f, 30f, "0.0 s", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Bend Time")]
        public float BendTimeDuration { get; set; } = 5f;

        [SettingPropertyBool("Preserve player action speed", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Bend Time")]
        public bool PreservePlayerSpeed { get; set; } = true;

        [SettingPropertyBool("Allow complete suspension", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("Bend Time")]
        public bool AllowCompleteSuspension { get; set; } = false;

        [SettingPropertyFloatingInteger("Cooldown", 0f, 90f, "0.0 s", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("Bend Time")]
        public float BendTimeCooldown { get; set; } = 18f;

        [SettingPropertyInteger("Maximum links", 2, 30, "0", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Domino", GroupOrder = 7)]
        public int DominoMaximumLinks { get; set; } = 6;

        [SettingPropertyBool("Propagate damage", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Domino")]
        public bool DominoPropagateDamage { get; set; } = true;

        [SettingPropertyBool("Propagate knockdown", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Domino")]
        public bool DominoPropagateKnockdown { get; set; } = true;

        [SettingPropertyBool("Propagate death", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("Domino")]
        public bool DominoPropagateDeath { get; set; } = false;

        [SettingPropertyFloatingInteger("Damage propagation", 0f, 1f, "#0%", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("Domino")]
        public float DominoDamageFactor { get; set; } = 0.45f;

        [SettingPropertyFloatingInteger("Marking range", 1f, 30f, "0.0 m", Order = 5, RequireRestart = false)]
        [SettingPropertyGroup("Domino")]
        public float DominoRange { get; set; } = 14f;

        [SettingPropertyFloatingInteger("Cooldown", 0f, 90f, "0.0 s", Order = 6, RequireRestart = false)]
        [SettingPropertyGroup("Domino")]
        public float DominoCooldown { get; set; } = 14f;

        [SettingPropertyFloatingInteger("Range", 5f, 100f, "0 m", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Dark Vision", GroupOrder = 8)]
        public float DarkVisionRange { get; set; } = 35f;

        [SettingPropertyFloatingInteger("Refresh interval", 0.1f, 3f, "0.0 s", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Dark Vision")]
        public float DarkVisionRefreshInterval { get; set; } = 0.5f;

        [SettingPropertyBool("Highlight interactables", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Dark Vision")]
        public bool DarkVisionInteractables { get; set; } = false;

        [SettingPropertyFloatingInteger("Cooldown", 0f, 30f, "0.0 s", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("Dark Vision")]
        public float DarkVisionCooldown { get; set; } = 1f;

        internal float Cost(Voidstep.Core.AbilityId id)
        {
            switch (id)
            {
                case Voidstep.Core.AbilityId.VoidstepCleave: return VoidstepCost;
                case Voidstep.Core.AbilityId.Blink: return BlinkCost;
                case Voidstep.Core.AbilityId.Windblast: return WindblastCost;
                case Voidstep.Core.AbilityId.BendTime: return BendTimeCost;
                case Voidstep.Core.AbilityId.Domino: return DominoCost;
                case Voidstep.Core.AbilityId.DarkVision: return DarkVisionCost;
                default: return 0f;
            }
        }

        internal float Cooldown(Voidstep.Core.AbilityId id)
        {
            switch (id)
            {
                case Voidstep.Core.AbilityId.VoidstepCleave: return VoidstepCooldown;
                case Voidstep.Core.AbilityId.Blink: return BlinkCooldown;
                case Voidstep.Core.AbilityId.Windblast: return WindblastCooldown;
                case Voidstep.Core.AbilityId.BendTime: return BendTimeCooldown;
                case Voidstep.Core.AbilityId.Domino: return DominoCooldown;
                case Voidstep.Core.AbilityId.DarkVision: return DarkVisionCooldown;
                default: return 0f;
            }
        }
    }
}
