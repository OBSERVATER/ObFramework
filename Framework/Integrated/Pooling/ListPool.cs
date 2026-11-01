using System;
using System.Collections.Generic;

namespace ObFramework.Integrated.Pooling
{
    public class ListPoolPolicy<T> : APoolPolicy<List<T>>
    {
        public override List<T> Create() => new List<T>();
        public override bool OnReturn(List<T> obj)
        {
            obj.Clear();
            return true;
        }
    }
    public class ListPool<T> : AObjectPool<List<T>>
    {
        private static readonly Lazy<ListPool<T>> _default = new Lazy<ListPool<T>>(() => new ListPool<T>(8, 0, new ListPoolPolicy<T>(), checkIfRepeat: true));

        public ListPool(int capacity, int prewarmCount, IPoolPolicy<List<T>> policy, int maxRetained = 1024, bool checkIfRepeat = false)
            : base(capacity, prewarmCount, policy, maxRetained, checkIfRepeat)
        {
        }

        public static ListPool<T> Pool => _default.Value;
    }
}
