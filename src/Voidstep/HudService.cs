using System;
using TaleWorlds.Library;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class HudService
    {
        private float _statusTimer;
        private string _lastStatus;

        public void Tick(float dt, VoidEnergyPool energy, CooldownBook cooldowns, bool darkVisionActive, bool bendTimeActive)
        {
            _statusTimer -= Math.Max(0f, dt);
            if (_statusTimer > 0f) return;
            _statusTimer = 1.25f;
            var settings = VoidstepSettings.Current;
            if (!settings.EnergyEnabled || settings.CooldownOnlyMode) return;
            var status = $"Void Energy {energy.Current:0}/{energy.Maximum:0}";
            if (darkVisionActive) status += " | Dark Vision";
            if (bendTimeActive) status += " | Bend Time";
            if (status == _lastStatus) return;
            _lastStatus = status;
            InformationManager.DisplayMessage(new InformationMessage(status));
        }

        public void Show(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            InformationManager.DisplayMessage(new InformationMessage(message));
        }

        public void ShowAbilityResult(AbilityId ability, VoidEnergyPool energy, CooldownBook cooldowns)
        {
            var cooldown = cooldowns.GetRemaining(ability);
            Show($"{DisplayName(ability)} — Void Energy {energy.Current:0}/{energy.Maximum:0}, cooldown {cooldown:0.0}s");
        }

        public void Reset()
        {
            _statusTimer = 0f;
            _lastStatus = null;
        }

        private static string DisplayName(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.VoidstepCleave: return "Voidstep Cleave";
                case AbilityId.Blink: return "Blink";
                case AbilityId.Windblast: return "Windblast";
                case AbilityId.BendTime: return "Bend Time";
                case AbilityId.Domino: return "Domino";
                case AbilityId.DarkVision: return "Dark Vision";
                default: return id.ToString();
            }
        }
    }
}
