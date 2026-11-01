using System;
using System.Collections.Generic;

namespace ObFramework.Integrated.Pooling
{
    public class DictionaryPoolPolicy<TKey, TValue> : APoolPolicy<Dictionary<TKey, TValue>>
    {
        public override Dictionary<TKey, TValue> Create() => new Dictionary<TKey, TValue>();
        public override bool OnReturn(Dictionary<TKey, TValue> obj)
        {
            obj.Clear();
            return true;
        }
    }
    public class DictionaryPool<TKey, TValue> : AObjectPool<Dictionary<TKey, TValue>>
    {
        public static DictionaryPool<TKey, TValue> Pool => _default.Value;
        private readonly static Lazy<DictionaryPool<TKey, TValue>> _default
            = new Lazy<DictionaryPool<TKey, TValue>>(() => new DictionaryPool<TKey, TValue>(8, 0, new DictionaryPoolPolicy<TKey, TValue>(), checkIfRepeat: true));
        public DictionaryPool(int capacity, int prewarmCount, IPoolPolicy<Dictionary<TKey, TValue>> policy, int maxRetained = 1024, bool checkIfRepeat = false)
            : base(capacity, prewarmCount, policy, maxRetained, checkIfRepeat)
        {
        }
    }
}
