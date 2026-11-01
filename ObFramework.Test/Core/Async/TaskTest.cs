using ObFramework.Core.Async;

namespace ObFramework.Test.Core.Async;

public class TaskTest
{
    [SetUp]
    public void Setup()
    {
    }


    [Test]
    public async Task ObTask_Await_ShouldReturnResult()
    {
        var tcs = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // 创建我们自己的 ObTask
        var obTask = new ObTask<int>(tcs.Task);

        // 测试一：任务尚未完成
        Assert.That(obTask.GetAwaiter().IsCompleted, Is.False);

        // 开始等待，但不立即完成底层任务
        Task<int> consumer = ConsumeAsync(obTask);

        Assert.That(consumer.IsCompleted, Is.False);

        // 测试二：由外部完成任务
        tcs.SetResult(100);

        // 测试三：恢复执行并取得结果
        int result = await consumer;

        Assert.That(result, Is.EqualTo(100));
    }

    [Test]
    public async Task ObTask_AsyncMethod_ShouldReturn42()
    {
        ObTask<int> task = CalculateAsync();

        int result = await task;

        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public async Task ObTask_AsyncMethod_ShouldResumeAfterAwait()
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        ObTask<int> input = new ObTask<int>(tcs.Task);
        // 调用 async ObTask 方法
        ObTask<int> operation = AddOneAsync(input);
        // 底层任务尚未完成，外层任务也应处于等待状态
        Assert.That(operation.GetAwaiter().IsCompleted, Is.False);
        // 完成底层任务，触发状态机恢复
        tcs.SetResult(100);
        int result = await operation;
        Assert.That(result, Is.EqualTo(101));
    }
    [Test]
    public async Task ObTask_ShouldPropagateException()
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new ObTask<int>(tcs.Task);

        // 调用后先挂起
        ObTask<int> operation = ThrowAsync(input);
        Assert.That(operation.GetAwaiter().IsCompleted, Is.False);
        // 让底层操作成功完成
        tcs.SetResult(100);
        // 但是外层异步方法会自己抛出异常
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await operation;
        });
        Assert.That(exception.Message, Is.EqualTo("Test Error"));
    }
    [Test]
    public async System.Threading.Tasks.Task ObTask_ShouldPropagateCancellation()
    {
        using var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new ObTask<int>(tcs.Task);
        ObTask<int> operation = AddOneAsync(input);

        // 1. 外层任务尚未完成
        Assert.That(operation.GetAwaiter().IsCompleted, Is.False);

        // 2. 发出取消请求
        cts.Cancel();

        // 3. 明确将底层任务设置为 Canceled
        Assert.That(tcs.TrySetCanceled(cts.Token), Is.True);

        // 4. 外层 ObTask 应传播取消异常
        var exception = await Assert.CatchAsync<OperationCanceledException>(
            async () =>
            {
                await operation;
            });

        // 5. 验证取消令牌
        Assert.That(exception.CancellationToken, Is.EqualTo(cts.Token));
    }
    [Test]
    public async Task ObTask_NonGeneric_ShouldComplete()
    {
        ObTask operation = DoSomethingAsync();
        await operation;
        Assert.That(operation.GetAwaiter().IsCompleted, Is.True);
    }

    [Test]
    public async Task ObTask_NonGeneric_ShouldSuspendAndResume()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool resumed = false;

        // 启动异步方法
        ObTask operation = WaitAsync(tcs.Task);

        Assert.Multiple(() =>
        {
            // 尚未完成
            Assert.That(operation.GetAwaiter().IsCompleted, Is.False);
            Assert.That(resumed, Is.False);
        });

        // 现在才允许异步方法继续
        tcs.SetResult(true);

        await operation;

        // 验证 await 后面的代码确实执行了
        Assert.That(resumed, Is.True);

        async ObTask WaitAsync(Task task)
        {
            await task;
            resumed = true;
        }
    }

    private static async ObTask DoSomethingAsync()
    {
        await Task.Yield();
    }
    private static async ObTask<int> ThrowAsync(ObTask<int> input)
    {
        await input;
        throw new InvalidOperationException("Test Error");
    }
    private static async ObTask<int> AddOneAsync(ObTask<int> input)
    {
        int value = await input;
        return value + 1;
    }

    private static async ObTask<int> CalculateAsync()
    {
        await Task.CompletedTask;
        return 42;
    }

    private static async Task<int> ConsumeAsync(ObTask<int> task)
    {
        return await task;
    }

}
