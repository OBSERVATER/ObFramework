
using System;
using System.Runtime.CompilerServices;
namespace ObFramework.Core.Async
{
    public struct ObTaskMethodBuilder<T>
    {
        private AsyncValueTaskMethodBuilder<T> _inner;

        // 创建 Builder
        public static ObTaskMethodBuilder<T> Create()
        {
            return default;
        }

        // 返回 ObTask<T>
        public ObTask<T> Task
        {
            get
            {
                return new ObTask<T>(_inner.Task);
            }
        }

        // 启动状态机
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            _inner.Start(ref stateMachine);
        }

        // 保存成功结果
        public void SetResult(T result)
        {
            _inner.SetResult(result);
        }

        // 保存异常
        public void SetException(Exception exception)
        {
            _inner.SetException(exception);
        }

        // 注册普通 continuation
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        }

        // 注册 critical continuation
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            _inner.SetStateMachine(stateMachine);
        }
    }
    public struct ObTaskMethodBuilder
    {
        private AsyncValueTaskMethodBuilder _inner;
        public static ObTaskMethodBuilder Create()
        {
            return default;
        }
        public ObTask Task
        {
            get
            {
                return new ObTask(_inner.Task);
            }
        }

        // 启动状态机
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            _inner.Start(ref stateMachine);
        }
        public void SetResult() => _inner.SetResult();

        public void SetException(Exception exception)
        {
            _inner.SetException(exception);
        }

        // 注册普通 continuation
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        }

        // 注册 critical continuation
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        }
        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            _inner.SetStateMachine(stateMachine);
        }
    }
}
