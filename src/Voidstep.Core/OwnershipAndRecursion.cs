using System;
using System.Collections.Generic;

namespace Voidstep.Core
{
    public sealed class OwnershipLedger<TValue>
    {
        private readonly Dictionary<long, TValue> _owned = new Dictionary<long, TValue>();
        private long _nextToken;

        public long Acquire(TValue value)
        {
            var token = ++_nextToken;
            _owned.Add(token, value);
            return token;
        }

        public bool Owns(long token) => token != 0 && _owned.ContainsKey(token);
        public bool TryGet(long token, out TValue value) => _owned.TryGetValue(token, out value);
        public bool Release(long token, out TValue value)
        {
            if (!_owned.TryGetValue(token, out value))
                return false;
            _owned.Remove(token);
            return true;
        }
        public void Clear() => _owned.Clear();
    }

    public sealed class RecursionGuard<TKey>
    {
        private readonly HashSet<TKey> _active = new HashSet<TKey>();

        public IDisposable Enter(TKey key)
        {
            if (!_active.Add(key))
                return null;
            return new Lease(this, key);
        }

        public bool IsActive(TKey key) => _active.Contains(key);

        private sealed class Lease : IDisposable
        {
            private RecursionGuard<TKey> _owner;
            private readonly TKey _key;

            public Lease(RecursionGuard<TKey> owner, TKey key)
            {
                _owner = owner;
                _key = key;
            }

            public void Dispose()
            {
                var owner = _owner;
                if (owner == null) return;
                _owner = null;
                owner._active.Remove(_key);
            }
        }
    }
}
