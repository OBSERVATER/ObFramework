
using System;
using System.Buffers;

namespace ObFramework.Integrated.Pooling
{
    public interface IObjectPool<T> where T : class
    {
        T Get();
        void Return(T obj);

        int InactiveCount { get; }
        IPoolPolicy<T> Policy { get; }
    }

    public interface IPoolPolicy<T> where T : class
    {
        T Create();
        void OnCreate(T obj);
        void OnGet(T obj);
        bool OnReturn(T obj);
        void DestroyObj(T obj);
    }
    public abstract class APoolPolicy<T> : IPoolPolicy<T> where T : class
    {
        public abstract T Create();
        public virtual void OnCreate(T obj) { }
        public virtual void OnGet(T obj) { }
        public virtual bool OnReturn(T obj) => true;
        public virtual void DestroyObj(T obj) { }
    }
    public abstract class AObjectPool<T> : IObjectPool<T>, IDisposable where T : class
    {
        private readonly IPoolPolicy<T> _policy;
        private readonly int _maxRetained;

        private T[] _pool;
        private int _count;
        private bool _disposed;
        private bool _checkIfRepeat;

        public int InactiveCount => _count;
        public int Capacity => _pool.Length;
        public IPoolPolicy<T> Policy => _policy;

        protected AObjectPool(int capacity, int prewarmCount, IPoolPolicy<T> policy, int maxRetained = 1024, bool checkIfRepeat = false)
        {
            if (capacity <= 0 || maxRetained <= 0)
                throw new ArgumentOutOfRangeException();

            if (prewarmCount < 0 || prewarmCount > maxRetained)
                throw new ArgumentOutOfRangeException(nameof(prewarmCount));

            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _maxRetained = maxRetained;

            _pool = ArrayPool<T>.Shared.Rent(capacity);
            Array.Clear(_pool, 0, _pool.Length);

            try
            {
                for (int i = 0; i < prewarmCount; i++)
                    Return(CreateNew());
            }
            catch
            {
                Dispose();
                throw;
            }

            _checkIfRepeat = checkIfRepeat;
        }

        public virtual T Get()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AObjectPool<T>));

            T obj;

            if (_count == 0)
            {
                obj = CreateNew();
            }
            else
            {
                obj = _pool[--_count];
                _pool[_count] = null;
            }

            try
            {
                _policy.OnGet(obj);
                return obj;
            }
            catch
            {
                _policy.DestroyObj(obj);
                throw;
            }
        }

        public virtual void Return(T obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            if (_disposed)
            {
                _policy.DestroyObj(obj);
                return;
            }
            if (_checkIfRepeat)
            {
                CheckDuplicateReturn(obj);
            }
            // Policy 决定对象能否再次复用
            if (!_policy.OnReturn(obj))
            {
                _policy.DestroyObj(obj);
                return;
            }

            if (_count >= _maxRetained)
            {
                _policy.DestroyObj(obj);
                return;
            }

            if (_count == _pool.Length)
                Grow();

            _pool[_count++] = obj;
        }

        private T CreateNew()
        {
            T obj = _policy.Create() ?? throw new InvalidOperationException("Policy returned null.");
            try
            {
                _policy.OnCreate(obj);
                return obj;
            }
            catch
            {
                _policy.DestroyObj(obj);
                throw;
            }
        }

        protected virtual void Grow()
        {
            int size = (int)Math.Min((long)_pool.Length * 2, _maxRetained);

            T[] next = ArrayPool<T>.Shared.Rent(size);
            Array.Clear(next, 0, next.Length);
            Array.Copy(_pool, next, _count);

            T[] old = _pool;
            _pool = next;

            ArrayPool<T>.Shared.Return(old, clearArray: true);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            T[] items = _pool;
            int count = _count;

            _pool = Array.Empty<T>();
            _count = 0;

            Exception error = null;

            try
            {
                for (int i = 0; i < count; i++)
                {
                    T obj = items[i];
                    items[i] = null;

                    try
                    {
                        _policy.DestroyObj(obj);
                    }
                    catch (Exception ex)
                    {
                        error ??= ex;
                    }
                }
            }
            finally
            {
                ArrayPool<T>.Shared.Return(items, clearArray: true);
            }

            if (error != null)
                throw new AggregateException("Failed to destroy pooled objects.", error);
        }

        private void CheckDuplicateReturn(T obj)
        {
            if (!_checkIfRepeat)
                return;

            for (int i = 0; i < _count; i++)
            {
                if (ReferenceEquals(_pool[i], obj))
                {
                    throw new InvalidOperationException("Object has already been returned to the pool.");
                }
            }
        }
    }
}
