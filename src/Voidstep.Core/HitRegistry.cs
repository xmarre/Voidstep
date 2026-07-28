using System;
using System.Collections.Generic;

namespace Voidstep.Core
{
    public sealed class HitRegistry<TKey>
    {
        private readonly HashSet<TKey> _hits;

        public HitRegistry(IEqualityComparer<TKey> comparer = null)
        {
            _hits = new HashSet<TKey>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public int Count => _hits.Count;
        public bool TryRegister(TKey key) => _hits.Add(key);

        public bool TryRegister(TKey key, int maximumCount)
        {
            if (maximumCount < 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
            if (_hits.Contains(key)) return false;
            if (maximumCount > 0 && _hits.Count >= maximumCount) return false;
            return _hits.Add(key);
        }

        public bool Contains(TKey key) => _hits.Contains(key);
        public void Clear() => _hits.Clear();
    }
}
