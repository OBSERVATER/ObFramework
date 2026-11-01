using System.Collections.Generic;

namespace ObFramework.Extension
{
    public static class SystemCollectionExtension
    {
        public static V GetOrAdd<K, V>(this Dictionary<K, V> self, K key, V val)
        {
            if (!self.TryGetValue(key, out var value))
            {
                self.Add(key, val);
                value = val;
            }
            return value;
        }
    }
}
