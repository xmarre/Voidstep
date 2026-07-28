using System;
using System.Collections.Generic;

namespace Voidstep.Core
{
    public sealed class VoidEnergyPool
    {
        private float _current;
        private float _maximum;

        public VoidEnergyPool(float maximum)
        {
            ConfigureMaximum(maximum, true);
        }

        public float Current => _current;
        public float Maximum => _maximum;
        public float Fraction => _maximum <= 0f ? 1f : _current / _maximum;

        public void ConfigureMaximum(float maximum, bool refill)
        {
            if (maximum <= 0f) throw new ArgumentOutOfRangeException(nameof(maximum));
            _maximum = maximum;
            _current = refill ? maximum : Math.Min(_current, maximum);
        }

        public bool CanSpend(float amount, bool unlimited, bool disabled)
        {
            if (amount < 0f) throw new ArgumentOutOfRangeException(nameof(amount));
            return disabled || unlimited || _current + 0.0001f >= amount;
        }

        public bool TrySpend(float amount, bool unlimited, bool disabled)
        {
            if (!CanSpend(amount, unlimited, disabled))
                return false;
            if (!disabled && !unlimited)
                _current = Math.Max(0f, _current - amount);
            return true;
        }

        public void Regenerate(float amount)
        {
            if (amount < 0f) throw new ArgumentOutOfRangeException(nameof(amount));
            _current = Math.Min(_maximum, _current + amount);
        }

        public void Reset() => _current = _maximum;
    }

    public sealed class CooldownBook
    {
        private readonly Dictionary<AbilityId, float> _remaining = new Dictionary<AbilityId, float>();
        private readonly List<AbilityId> _tickKeys = new List<AbilityId>(8);

        public float GetRemaining(AbilityId id) => _remaining.TryGetValue(id, out var value) ? value : 0f;
        public bool IsReady(AbilityId id) => GetRemaining(id) <= 0f;

        public void Start(AbilityId id, float duration)
        {
            if (duration < 0f) throw new ArgumentOutOfRangeException(nameof(duration));
            _remaining[id] = duration;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (_remaining.Count == 0) return;
            _tickKeys.Clear();
            foreach (var key in _remaining.Keys) _tickKeys.Add(key);
            for (var i = 0; i < _tickKeys.Count; i++)
            {
                var id = _tickKeys[i];
                var next = _remaining[id] - deltaTime;
                if (next <= 0f) _remaining.Remove(id);
                else _remaining[id] = next;
            }
        }

        public void Clear()
        {
            _remaining.Clear();
            _tickKeys.Clear();
        }
    }
}
