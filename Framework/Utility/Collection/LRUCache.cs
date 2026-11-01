using System;
using System.Collections.Generic;

namespace ObFramework.Utility.Collection
{
    public sealed class LRUCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
        private readonly LinkedList<(TKey Key, TValue Value)> _list;
        private readonly Action<TKey, TValue>? _onRemoved;
        public int Count => _map.Count;
        public int Capacity => _capacity;

        public LRUCache(int capacity, Action<TKey, TValue>? onRemoved = null)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _capacity = capacity;
            _onRemoved = onRemoved;

            _map = new Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>>();
            _list = new LinkedList<(TKey Key, TValue Value)>();
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddFirst(node);

                value = node.Value.Value;
                return true;
            }

            value = default!;
            return false;
        }
        public bool TryTake(TKey key,out TValue val)
        {
            val = default!;
            if (!_map.TryGetValue(key,out var node))
            {
                return false;
            }
            val = node.Value.Value;
            return Remove(key,false);
        }

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
        {
            if (TryGet(key, out var value))
                return value;

            value = factory(key);
            Add(key, value);

            return value;
        }

        public void Add(TKey key, TValue value)
        {
            if (_map.ContainsKey(key))
                throw new InvalidOperationException("Key has already been added.");

            if (_map.Count >= _capacity)
                RemoveNode(_list.Last!);

            var node = _list.AddFirst((key, value));
            _map.Add(key, node);
        }

        public bool Remove(TKey key,bool trigger = true)
        {
            if (!_map.TryGetValue(key, out var node))
                return false;

            RemoveNode(node,trigger);
            return true;
        }

        public void Clear()
        {
            while (_list.Last != null)
                RemoveNode(_list.Last);
        }

        private void RemoveNode(LinkedListNode<(TKey Key, TValue Value)> node, bool trigger = true)
        {
            var (key, value) = node.Value;

            _list.Remove(node);
            _map.Remove(key);

            if (trigger) _onRemoved?.Invoke(key, value);
        }
    }
}