using System.Collections.Generic;

namespace ObFramework.Core
{
    public interface IRuntimeData
    {
        string Id { get; }
    }
    public interface IRuntimeDataGroup<T> where T : IRuntimeData
    {
        IReadOnlyCollection<T> All { get; }
        bool TryGet(string Id, out T val);
    }
    public sealed class DefaultRuntimeDataGroup<T> : IRuntimeDataGroup<T> where T : class, IRuntimeData
    {
        private readonly Dictionary<string, T> _map;

        internal DefaultRuntimeDataGroup(Dictionary<string, T> map)
        {
            _map = map;
        }
        public IReadOnlyCollection<T> All => _map.Values;
        public bool TryGet(string id, out T val) => _map.TryGetValue(id, out val);
    }
}
