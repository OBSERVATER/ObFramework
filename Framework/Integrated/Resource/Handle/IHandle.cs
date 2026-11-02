using System;
using System.Threading;

namespace ObFramework.Integrated.Resource.Handle
{
    public interface IHandle : IDisposable
    {
        string Location { get; }
        T? Get<T>() where T : class;
    }
    public abstract class AHandle : IHandle
    {
        public string Location { get; protected set; }
        protected int _disposed;
        protected void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(AssetHandle));
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }
            Release();
        }
        public abstract T? Get<T>() where T : class;
        protected abstract void Release();
    }
}
