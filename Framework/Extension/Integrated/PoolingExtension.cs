using ObFramework.Integrated.Pooling;
using System.Collections.Generic;

namespace ObFramework.Extension.Integrated
{
    public static class PoolingExtension
    {
        public static void ReleaseToPool<T>(this List<T> list) => ListPool<T>.Pool.Return(list);
        public static void ReleaseToPool<TK, TV>(this Dictionary<TK, TV> dict) => DictionaryPool<TK, TV>.Pool.Return(dict);
    }
}
