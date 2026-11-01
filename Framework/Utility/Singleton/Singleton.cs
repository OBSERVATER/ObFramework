using System;

namespace ObFramework.Utility.Singleton
{
    public class Singleton<T> : ISingleton, IDisposable where T : class, new()
    {
        public static T Inst
        {
            get
            {
                lock (_lock)
                {
                    inst ??= new T();
                }
                return inst;
            }
        }
        private static T inst;
        private readonly static object _lock = new object();
        public virtual void OnSingletonInit()
        {
        }

        public virtual void Dispose()
        {
            inst = null;
        }
    }
}
