using ObFramework.Core.Async;

namespace ObFramework.Test;

public class ScopeTest
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public async Task StopAsync_RejectsNewTasks()
    {
        var scope = new ObExecutionScope();
        await scope.StopAsync();
        bool executed = false;

        Assert.Throws<InvalidOperationException>(() =>
        {
            scope.Run(token =>
            {
                executed = true;
                return Task.CompletedTask;
            });
        });
        Assert.That(executed, Is.False);
    }


    [Test]
    public async Task StopAsync_ShouldWaitUntilWorkExits()
    {
        var scope = new ObExecutionScope();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken receivedToken = default;

        Task work = scope.Run(async token =>
        {
            receivedToken = token;
            // 模拟不立即响应取消的操作
            await release.Task;
            token.ThrowIfCancellationRequested();
        });

        // Architecture 开始停止
        Task stopping = scope.StopAsync();

        try
        {
            Assert.Multiple(() =>
            {
                // 取消请求应当已经发出
                Assert.That(receivedToken.IsCancellationRequested, Is.True);
                // 但工作任务还没有退出
                Assert.That(work.IsCompleted, Is.False);
                // 所以 Scope 也不能完成关闭
                Assert.That(stopping.IsCompleted, Is.False);
            });
        }
        finally
        {
            // 外部操作终于结束
            release.TrySetResult(true);
        }

        await Assert.CatchAsync<OperationCanceledException>(async () => await work);

        // 任务退出后，Scope 才能完成关闭
        await stopping;
    }

    [Test]
    public async Task StopAsync_ShouldNotMissRunningWork()
    {
        var scope = new ObExecutionScope();

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task stopping = Task.CompletedTask;

        Task running = scope.Run(token =>
        {
            // 注意：这里还没有返回 Task 给 Run()
            // 因此 Run() 还来不及执行 _tasks.Add(task)

            stopping = scope.StopAsync();

            // 这个任务还没完成
            return gate.Task;
        });

        try
        {
            // Scope 不应该认为关闭已经完成
            Assert.That(stopping.IsCompleted, Is.False);
        }
        finally
        {
            // 无论断言成功还是失败，都释放任务
            gate.TrySetResult(true);
        }

        await running;
        await stopping;
    }


    [Test]
    public async Task Shutdown_ShouldBeConsistentAndSafe()
    {
        var scope = new ObExecutionScope();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int callbackCount = 0;

        Task work = scope.Run(async token =>
        {
            using var registration = token.Register(() =>
            {
                Interlocked.Increment(ref callbackCount);
                throw new InvalidOperationException("Cancel callback failed");
            });

            // 模拟尚未退出的工作
            await release.Task;
            token.ThrowIfCancellationRequested();
        });

        // 两个调用方请求关闭同一个 Scope
        Task stopA = scope.StopAsync();
        Task stopB = scope.StopAsync();

        try
        {
            // 取消回调只应执行一次
            Assert.That(callbackCount, Is.EqualTo(1));

            // 工作未退出，两次关闭都不能完成
            Assert.Multiple(() =>
            {
                Assert.That(stopA.IsCompleted, Is.False);
                Assert.That(stopB.IsCompleted, Is.False);
            });
        }
        finally
        {
            release.TrySetResult(true);
        }

        await Assert.CatchAsync<OperationCanceledException>(async () => await work);

        // 两个调用方都应该观察到相同的关闭错误
        var errorA = Assert.ThrowsAsync<AggregateException>(async () => await stopA);
        var errorB = Assert.ThrowsAsync<AggregateException>(async () => await stopB);

        Assert.Multiple(() =>
        {
            Assert.That(errorA.Result.Flatten().InnerExceptions, Has.Some.TypeOf<InvalidOperationException>());
            Assert.That(errorB.Result.Flatten().InnerExceptions, Has.Some.TypeOf<InvalidOperationException>());
            Assert.That(work.IsCompleted, Is.True);
        });
    }

    [Test]
    public async Task Phase2_5_StaleAsyncWorkCannotModifyReusedObject()
    {
        var lifetime = new ObLifetime();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int carId = 1;

        // 车辆 A 的生命周期
        int oldVersion = lifetime.Capture();
        Task oldWork = LoadOldCarAsync();

        // A 被回收
        lifetime.Invalidate();

        // 同一个对象实例被重新用于 B
        lifetime.Renew();

        int newVersion = lifetime.Capture();
        carId = 2;

        // 旧生命周期已经失效
        Assert.That(lifetime.IsCurrent(oldVersion), Is.False);

        // 让 A 之前的异步操作恢复
        gate.SetResult(true);
        await oldWork;
        Assert.Multiple(() =>
        {
            // A 的旧任务不能修改 B
            Assert.That(carId, Is.EqualTo(2));

            // B 自己的生命周期仍然有效
            Assert.That(
                lifetime.IsCurrent(newVersion),
                Is.True);
        });

        async Task LoadOldCarAsync()
        {
            await gate.Task;

            // 恢复后验证原来的生命周期
            if (!lifetime.IsCurrent(oldVersion))
                return;

            carId = 1;
        }
    }

    [Test]
    public async Task Phase2_6_ArchitectureShutdown_ShouldBeSafe()
    {
        var lifetime = new ObLifetime();
        var scope = new ObExecutionScope();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new TestService();

        int version = lifetime.Capture();
        CancellationToken receivedToken = default;

        // 模拟 Architecture 正在进行的异步任务
        Task work = scope.Run(async token =>
        {
            receivedToken = token;

            await release.Task;

            // 防止旧生命周期的任务修改 Service
            if (lifetime.IsCurrent(version))
            {
                service.Modify();
            }

            token.ThrowIfCancellationRequested();
        });

        // 开始关闭 Architecture
        Task shutdown = ShutdownArchitectureAsync();

        try
        {
            Assert.Multiple(() =>
            {
                // 生命周期必须立即失效
                Assert.That(lifetime.IsCurrent(version), Is.False);
                // 任务必须收到取消请求
                Assert.That(receivedToken.IsCancellationRequested, Is.True);
                // 工作没有退出，不能提前完成关闭
                Assert.That(shutdown.IsCompleted, Is.False);
                // 不能提前释放 Service
                Assert.That(service.IsDisposed, Is.False);
            });
        }
        finally
        {
            release.TrySetResult(true);
        }

        await Assert.CatchAsync<OperationCanceledException>(async () => await work);

        await shutdown;

        Assert.Multiple(() =>
        {
            Assert.That(service.IsDisposed, Is.True);
            Assert.That(service.ModifyCount, Is.Zero);
        });

        var scope2 = new ObExecutionScope();
        var lifetime2 = new ObLifetime();
        var service2 = new TestService();

        var gate2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 1. 启动一个最终会失败的任务
        Task work2 = scope2.Run(async token =>
        {
            await gate2.Task;

            // 故意模拟业务异常
            throw new InvalidOperationException("Load failed");
        });

        // 2. Architecture 开始关闭
        Task shutdown2 = ShutdownWithErrorAsync();

        Assert.That(service2.IsDisposed, Is.False);
        Assert.That(shutdown2.IsCompleted, Is.False);

        // 3. 放行任务，使它发生异常
        gate2.SetResult(true);

        // 4. 验证关闭任务保留了原始业务异常
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await shutdown2);

        Assert.That(error.Result.Message, Is.EqualTo("Load failed"));

        // 5. 即使关闭时发生异常，Service 仍然必须释放
        Assert.That(service2.IsDisposed, Is.True);
        await

                // 同时观察工作任务自身的异常
                Assert.ThrowsAsync<InvalidOperationException>(async () => await work2);

        async Task ShutdownWithErrorAsync()
        {
            lifetime2.Invalidate();

            try
            {
                await scope2.StopAsync();
            }
            finally
            {
                service2.Dispose();
            }
        }

        // 本次练习：自己实现这个方法
        async Task ShutdownArchitectureAsync()
        {
            lifetime.Invalidate();
            try
            {
                await scope.StopAsync();
            }
            finally
            {
                service.Dispose();
            }
        }
    }

    private sealed class TestService : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public int ModifyCount { get; private set; }

        public void Modify()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(TestService));

            ModifyCount++;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

}
