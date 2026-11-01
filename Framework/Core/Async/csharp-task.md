## cs自定义task类笔记

> 写跨引擎框架的时候发现有些地方要使用异步，而涉及到异步就要处理多线程的各种冲突。而大部分引擎都是单线程引擎，贸然使用task会因为不是同一个task上下文可能会导致线程死锁或者生命周期竟态等，因此这里会边学(ai太伟大了)边实现自己的跨引擎task（当然需要实现各个引擎的adapter绑定生命周期^-^||）
*task是怎么实现异步的？*

### 底层逻辑：状态机
**C# 的 async/await 本质上是一套由编译器生成状态机、由 Awaiter 控制等待与续执行、由 AsyncMethodBuilder 管理异步方法完成结果的协作机制。** 它不等同于多线程，也不一定会创建线程。
也就是说`await`是cs给的关于异步的语法糖。

#### 从一段代码开始
``` csharp
async ObTask<int> FooAsync()
{
    int value = await SomeOperation();
    return value + 10;
}
```
这里编译器会给底层转化成一个可暂停可恢复的状态机，调用方法后流程像下面这样（伪代码）
 ```
MoveNext() // 执行到 await SomeOperation()
if !Awaiter.IsCompleted
    // 注册续执行，然后返回调用方
// 继续或恢复 MoveNext()
GetResult() // 取得结果或抛出异常
Builder.SetResult(value + 10)
return ObTask.CompletedTask
 ```
一个 async 方法通常对应一个编译器生成的状态机；多个 await 是该状态机中的多个挂起位置，而不是每个 await 各生成一个状态机。 不同异步方法各有自己的状态机。

### 1. 实现await、async

a. await实现目标：使以下测试通过
```csharp
[Test]
public async Task ObTask_Await_ShouldReturnResult()
{
    var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    var obTask = new ObTask<int>(tcs.Task);

    // 测试一：任务尚未完成
    Assert.That(obTask.GetAwaiter().IsCompleted,Is.False);

    // 开始等待，但不立即完成底层任务
    Task<int> consumer = ConsumeAsync(obTask);
    Assert.That(consumer.IsCompleted, Is.False);
    // 测试二：由外部完成任务
    tcs.SetResult(100);
    // 测试三：恢复执行并取得结果
    int result = await consumer;
    Assert.That(result, Is.EqualTo(100));
}
private static async Task<int> ConsumeAsync(ObTask<int> task) => await task;
```
**await实现别人如何等待这个方法完成。**
这里先让ObTask借`ValueTask<T>`的壳上市实现await,要能使用await关键字只需要实现`GetAwaiter()`方法
```csharp
public readonly struct ObTask<T>
{
    private readonly ValueTask<T> _inner;

    public ObTask(Task<T> task)
    {
        // 把传入的 Task<T> 包装成 ValueTask<T>
        _inner = new ValueTask<T>(task);
    }

    public ValueTaskAwaiter<T> GetAwaiter()
    {
        // 返回 _inner 对应的 Awaiter
        return _inner.GetAwaiter();
    }
}
```
测试通过！

b. async实现目标：使以下测试通过，分别对应是否成功创建builder，是否能成功挂起并回复，是否能正常抛出异常，是否能被cts取消
```csharp
[Test]
public async Task ObTask_AsyncMethod_ShouldReturn42()
{
    ObTask<int> task = CalculateAsync();
    int result = await task;
    Assert.That(result, Is.EqualTo(42));
}
private static async ObTask<int> CalculateAsync()
{
    await Task.CompletedTask;
    return 42;
}

[Test]
public async Task ObTask_AsyncMethod_ShouldResumeAfterAwait()
{
    var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

    ObTask<int> input = new ObTask<int>(tcs.Task);
    // 调用 async ObTask 方法
    ObTask<int> operation = AddOneAsync(input);
    // 底层任务尚未完成，外层任务也应处于等待状态
    Assert.That(operation.GetAwaiter().IsCompleted,Is.False);

    // 完成底层任务，触发状态机恢复
    tcs.SetResult(100);
    int result = await operation;
    Assert.That(result, Is.EqualTo(101));
}
private static async ObTask<int> AddOneAsync(ObTask<int> input)
{
    int value = await input;
    return value + 1;
}

[Test]
public async Task ObTask_ShouldPropagateException()
{
    var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    var input = new ObTask<int>(tcs.Task);

    // 调用后先挂起
    ObTask<int> operation = ThrowAsync(input);
    Assert.That(operation.GetAwaiter().IsCompleted,Is.False);
    // 让底层操作成功完成，外层 ThrowAsync() 的 Builder的SetResult()方法不会进入。
    tcs.SetResult(100);
    // 但是外层异步方法会自己抛出异常
    var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
    {
        await operation;
    });
    Assert.That(exception.Message, Is.EqualTo("Test Error"));
}
private static async ObTask<int> ThrowAsync(ObTask<int> input)
{
    await input;
    throw new InvalidOperationException("Test Error");
}

[Test]
public void ObTask_ShouldPropagateCancellation()
{
    using var cts = new CancellationTokenSource();
    var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    var input = new ObTask<int>(tcs.Task);
    ObTask<int> operation = AddOneAsync(input);

    // 1. 外层任务尚未完成
    Assert.That(operation.GetAwaiter().IsCompleted,Is.False);

    // 2. 发出取消请求
    cts.Cancel();

    // 3. 明确将底层任务设置为 Canceled
    Assert.That(tcs.TrySetCanceled(cts.Token),Is.True);

    // 4. 外层 ObTask 应传播取消异常
    var exception = Assert.CatchAsync<OperationCanceledException>(
        async () =>
        {
            await operation;
        });

    // 5. 验证取消令牌
    Assert.That(exception.CancellationToken,Is.EqualTo(cts.Token));
}

private static async ObTask<int> AddOneAsync(ObTask<int> input)
{
    int value = await input;
    return value + 1;
}

```
*没有TaskCompletionSource，而且使用Task.CompletedTask，任务不会被阻塞。*
按目前的实现会出现`CS1983 异步方法的返回类型必须为 void、Task 或 Task<T>、类似任务的类型、IAsyncEnumerable<T> 或 IAsyncEnumerator<T>`错误,因为没有`AsyncMethodBuilder`
Builder实现**一个 async ObTask<T> 方法如何产生自己的 ObTask，并在执行结束后保存结果。** 他会在状态机创建时构建。
先给`ObTask<T>`加一个Attribute`[AsyncMethodBuilder(typeof(ObTaskMethodBuilder<>))]`
然后实现(参考了源码的一部分实现)
```csharp
namespace ObFramework.Core.Async
{
    public struct ObTaskMethodBuilder<T>
    {
        private AsyncValueTaskMethodBuilder<T> _inner;

        // TODO 1：创建 Builder
        public static ObTaskMethodBuilder<T> Create()
        {
            return default;
        }

        // TODO 2：返回 ObTask<T>
        public ObTask<T> Task
        {
            get
            {
                return new ObTask<T>(_inner.Task));
            }
        }

        // 启动状态机
        public void Start<TStateMachine>(ref TStateMachine stateMachine)where TStateMachine : IAsyncStateMachine
        {
            _inner.Start(ref stateMachine);
        }

        // TODO 3：保存成功结果
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
            _inner.AwaitOnCompleted(ref awaiter,ref stateMachine);
        }

        // 注册 critical continuation
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            _inner.AwaitUnsafeOnCompleted(ref awaiter,ref stateMachine);
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            _inner.SetStateMachine(stateMachine);
        }
    }
}
```
*取消任务也是走SetException(),抛出`OperationCanceledException`，任务状态是Cancelled*

自此实现了一个十分简单的套壳版Task
非泛型实现(把泛型删了就是，这样就没有返回值了)：
```csharp
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
```

### 2.异步生命周期安全
*异步方法执行期间，其依赖的对象、Service 或 Architecture 如果开始销毁，框架怎样保证安全?*
这里先不急着用ObTask，先用回原生的Task进行修改。

我们为框架引入scope，在框架Deinit的时候让所有的任务安全取消，并阻止新的任务访问已经释放的资源。
最初的想法很简单，用一个hashset管理task，停止时发送cancel并等待他们全部完成。
```csharp
namespace ObFramework.Core.Async
{
    public class ObExecutionScope
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private HashSet<Task> _tasks = new HashSet<Task>();
        private bool _stopping;

        public Task Run([NotNull]Func<CancellationToken, Task> work)
        {
            if (_stopping)
            {
                throw new InvalidOperationException("ObExecutionScope is stopping.");
            }
            var task = work.Invoke(_cts.Token) ?? throw new InvalidOperationException($"{nameof(work)} is null");
            _tasks.Add(task);
            return task;
        }

        public async Task StopAsync()
        {
            _stopping = true;
            Task result;
            _cts.Cancel();
            result = Task.WhenAll(_tasks);
            try
            {
                await result;
            }
            catch (OperationCanceledException) when (result.IsCanceled)
            {
            }
        }
    }
}
```
如果是单线程这样没问题，想法很美好，但是有一种情况,在
```csharp
var task = work.Invoke(_cts.Token) ?? throw new InvalidOperationException($"{nameof(work)} is null");
_tasks.Add(task);
```
期间如果执行`StopAsync()`就出bug了,工作已经开始，但 Scope 还不知道有这项工作。

这里换一种计数方式：引用计数，只要正在运行的任务数=0并且正在停止就代表所有任务完成了。
不过为了防止竟态在每一次操控引用计数器都要加锁
```csharp
// 和上面的不是一个东西
namespace ObFramework.Core.Async
{
    public class ObExecutionScope
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _gate = new object();
        private bool _stopping;
        private int _runningCount;
        private TaskCompletionSource<bool> _drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Run([NotNull]Func<CancellationToken, Task> work)
        {
            lock (_gate)
            {
                if (_stopping)
                {
                    throw new InvalidOperationException("ObExecutionScope is stopping.");
                }
                _runningCount++;
            }
            return ExecuteTrackedAsync(work);
        }
        public async Task StopAsync()
        {
            Task completion;

            lock (_gate)
            {
                _stopping = true;
                completion = _runningCount == 0 ? Task.CompletedTask : _drained.Task;
            }

            _cts.Cancel();
            await completion;
        }

        private async Task ExecuteTrackedAsync(Func<CancellationToken, Task> work)
        {
            try
            {
                var task = work.Invoke(_cts.Token) ?? throw new InvalidOperationException($"{nameof(work)} is null");
                await task;
            }
            finally
            {
                bool complete;
                lock (_gate)
                {
                    _runningCount--;
                    complete = _stopping && _runningCount == 0;
                }
                if (complete)
                {
                    _drained.SetResult(true);
                }
            }
        }
    }
}
```

到目前为止，scope的基础生命周期管理已经建立起来了。

---

如果就这么拿去用的话，会发现一个问题：Scope 知道任务什么时候结束，却不知道任务是否成功。因为scope根本没法抛出任务内的异常给StopAsync。
因此我们可以把异常收集起来，后面统一throw
```csharp
namespace ObFramework.Core.Async
{
    public class ObExecutionScope
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _gate = new object();
        private TaskCompletionSource<bool> _drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> _stopCompletion;
        private List<Exception> _errors = new List<Exception>();
        private bool _stopping;
        private int _runningCount;

        public Task Run([NotNull]Func<CancellationToken, Task> work)
        {
            lock (_gate)
            {
                if (_stopping)
                {
                    throw new InvalidOperationException("ObExecutionScope is stopping.");
                }
                _runningCount++;
            }
            return ExecuteTrackedAsync(work);
        }
        public Task StopAsync()
        {
            Task completion;
            TaskCompletionSource<bool> source;
            lock (_gate)
            {
                if (_stopCompletion != null)
                {
                    return _stopCompletion.Task;
                }
                _stopping = true;
                source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _stopCompletion = source;
                completion = _runningCount == 0 ? Task.CompletedTask : _drained.Task;
            }

            _ = StopAsyncInternal(source,completion);
            return source.Task;
        }

        private async Task StopAsyncInternal(TaskCompletionSource<bool> source,Task completion)
        {
            try
            {
                var errors = new List<Exception>();
                try
                {
                    _cts.Cancel();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }

                await completion;

                lock (_gate)
                {
                    errors.AddRange(_errors);
                }

                if (errors.Count == 0)
                {
                    source.TrySetResult(true);
                }
                else if (errors.Count == 1)
                {
                    source.TrySetException(errors[0]);
                }
                else
                {
                    source.TrySetException(new AggregateException(errors));
                }
            }
            catch (Exception ex)
            {
                source.TrySetException(ex);
            }
        }

        private async Task ExecuteTrackedAsync(Func<CancellationToken, Task> work)
        {
            try
            {
                var task = work.Invoke(_cts.Token) ?? throw new InvalidOperationException($"{nameof(work)} is null");
                await task;
            }
            catch (OperationCanceledException e)when (_cts.IsCancellationRequested &&e.CancellationToken == _cts.Token)
            {
                // Scope 发出的预期取消不记录业务错误
                throw;
            }
            catch (Exception e)
            {
                lock (_gate)
                {
                    _errors.Add(e);
                }
                throw;
            }
            finally
            {
                bool complete;
                lock (_gate)
                {
                    _runningCount--;
                    complete = _stopping && _runningCount == 0;
                }
                if (complete)
                {
                    _drained.SetResult(true);
                }
            }
        }
    }
}
```

### 3. 共享关闭任务与异常安全

最开始的 Scope 已经能够阻止新任务、发出取消请求，并等待所有登记任务退出，但还有一个问题：

如果两个地方同时调用 `StopAsync()`，难道要执行两次关闭逻辑吗？

不需要。我们可以让所有调用者共享同一个关闭任务。

于是新增了 `_stopCompletion`：

- `_drained`：表示所有受管理任务已经退出。
- `_stopCompletion`：表示整个关闭流程已经完成，包括取消、等待与异常处理。

注意，**开始关闭不等于已经关闭完成**，所以不能简单地 `if (_stopping) return;`，否则第二个调用者可能提前释放资源。

另一个问题是取消回调也可能发生异常。`CancellationTokenSource.Cancel()` 会同步执行注册的取消回调，如果回调抛出异常，就可能让 `StopAsync()` 提前结束。

所以关闭流程改成：**停止准入 → 请求取消并保存错误 → 等待 `_drained` → 汇总错误 → 最后完成 `_stopCompletion`。**

正常取消不能直接视为业务异常；业务异常则由 `Run()` 返回的任务保留，同时由 Scope 收集，供关闭时报告。

**测试：** `Phase2_4_Shutdown_ShouldBeConsistentAndSafe`

通过模拟取消回调异常、未完成任务和重复关闭，验证：

1. 取消回调不会因重复关闭而重复执行。
2. 任务没有退出前，关闭不能完成。
3. 所有调用者得到同一个关闭任务及其结果。

至此，Scope 具有了基础的任务归属、协作取消、排空和错误汇总能力。

### 4. 生命周期有效性：ObLifetime

现在又出现一个问题。

即使 Scope 已经通知任务取消，异步操作也可能还没结束。特别是在对象池中，一个对象可能被释放，然后重新分配给另一个业务对象。

例如车辆 A 正在异步加载资源，期间 A 被回收并复用为车辆 B。旧任务恢复时，引用仍然指向同一个 C# 对象，但它已经不属于原来的生命周期。

因此，**判断对象是否还活着不够，还要判断它是不是原来的那次生命周期。**

这里参考 ET 的 `EntityRef<T>` 思想，设计一个纯 C# 的 `ObLifetime`。

它具有四个基本操作：

| API | 意义 |
|---|---|
| `Capture()` | 保存当前生命周期的版本 |
| `Invalidate()` | 让当前生命周期失效 |
| `Renew()` | 开始新的生命周期并更新版本 |
| `IsCurrent(version)` | 检查保存的版本是否仍然有效 |

例如，车辆 A 捕获版本 1，随后被回收并重新启用为 B，版本变为 2。此时即使对象重新处于活动状态，A 保存的版本 1 仍然无法通过检查。

**测试：** `Phase2_5_StaleAsyncWorkCannotModifyReusedObject`

利用 `TaskCompletionSource` 暂停旧任务，在等待期间执行 `Invalidate()` 和 `Renew()`，再恢复旧任务。

验证旧任务不会修改重新使用的对象，同时新的生命周期仍然有效。

需要注意：

`ObLifetime` 只负责验证版本，并不会自动取消任务，也不能强制阻止业务代码执行。恢复后的代码仍需要检查生命周期。

此外，多线程下的“检查有效 → 操作对象”之间可能发生销毁竞争。因此未来 Adapter 还需要保证引擎对象操作发生在正确的执行上下文。

### 2.6 Architecture 生命周期集成

现在我们已经有两个工具：

- `ObExecutionScope`：管理正在执行的异步任务。
- `ObLifetime`：判断异步恢复后对象是否仍然有效。

接下来把它们用于 Architecture 的关闭流程。

目标是：**Architecture 关闭时，先让生命周期失效，再等待任务退出，最后释放 Service 和 DI 相关资源。**

基本顺序：

`Lifetime.Invalidate()` → `await Scope.StopAsync()` → `Service.Dispose()`

但这里存在一个问题：`StopAsync()` 可能在等待任务全部退出后，向调用方报告业务异常。如果直接顺序执行，后面的 `Dispose()` 就会被跳过。

因此必须使用 `try/finally`：

```csharp
lifetime.Invalidate();

try
{
    await scope.StopAsync();
}
finally
{
    service.Dispose();
}
```

这里不需要额外的 `catch { throw; }`，因为 `finally` 本来就能在异常传播时执行清理。

**测试：** `Phase2_6_ArchitectureShutdown_ShouldBeSafe`

测试分为正常关闭与业务异常两种场景。

通过 `TaskCompletionSource` 暂停工作任务，开始关闭 Architecture，然后验证：

- 生命周期已经失效；
- 任务收到取消请求；
- 任务尚未退出时，Service 不能提前释放；
- 任务结束后，Service 被释放；
- 即使任务发生业务异常，Service 仍会释放，异常也不会丢失。
- 
---