using System.Collections.Generic;

namespace ObFramework.Utility.Collection
{
    public abstract class BlackboardKey
    {
        public string Name { get; }
        protected BlackboardKey(string name) => Name = name;
    }

    public sealed class BlackboardKey<T> : BlackboardKey
    {
        public BlackboardKey(string name) : base(name) { }
    }

    public sealed class Blackboard
    {
        private abstract class Entry
        {
            public int Version;
        }

        private sealed class Entry<T> : Entry
        {
            public T Val;

            public Entry(T value)
            {
                Val = value;
                Version = 1;
            }
        }

        private readonly Dictionary<BlackboardKey, Entry> _map = new();
        public int Version { get; private set; }

        public T Get<T>(BlackboardKey<T> key)
            => _map.TryGetValue(key, out var entry) ? ((Entry<T>)entry).Val : throw new KeyNotFoundException(key.Name);
        public int GetVersion<T>(BlackboardKey<T> key)
            => _map.TryGetValue(key, out var entry) ? entry.Version : 0;

        public void Set<T>(BlackboardKey<T> key, T value)
        {
            if (_map.TryGetValue(key, out var entry))
            {
                var typed = (Entry<T>)entry;
                if (EqualityComparer<T>.Default.Equals(typed.Val, value))
                    return;

                typed.Val = value;
                typed.Version++;
            }
            else
            {
                _map.Add(key, new Entry<T>(value));
            }

            Version++;
        }
    }
}