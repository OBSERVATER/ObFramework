using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ObFramework.Core.Async
{
    [AsyncMethodBuilder(typeof(ObTaskMethodBuilder<>))]
    public readonly struct ObTask<T>
    {
        private readonly ValueTask<T> _inner;

        public ObTask(Task<T> task)
        {
            // 把传入的 Task<T> 包装成 ValueTask<T>
            _inner = new ValueTask<T>(task);
        }

        public ObTask(ValueTask<T> inner)
        {
            _inner = inner;
        }

        public ValueTaskAwaiter<T> GetAwaiter()
        {
            // 返回 _inner 对应的 Awaiter
            return _inner.GetAwaiter();
        }
    }
    [AsyncMethodBuilder(typeof(ObTaskMethodBuilder))]
    public readonly struct ObTask
    {
        private readonly ValueTask _inner;

        public ObTask(Task task)
        {
            _inner = new ValueTask(task);
        }

        public ObTask(ValueTask inner)
        {
            _inner = inner;
        }

        public ValueTaskAwaiter GetAwaiter()
        {
            return _inner.GetAwaiter();
        }
    }
}
