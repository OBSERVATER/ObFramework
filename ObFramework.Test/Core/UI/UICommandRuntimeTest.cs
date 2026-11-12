using ObFramework.Core;
using ObFramework.Integrated.UI;
using ObFramework.Test.Core.UI;

namespace ObFramework.Test.Core.UI;

[TestFixture]
public class UICommandRuntimeTest
{
    public sealed class CommandView : TestView
    {
        public bool CanRun = true;
        public int Executed;
        public int Entered;
        public bool Fail;
        public TaskCompletionSource<bool> Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken LastToken;
        public int Cancelled;

        public sealed class SyncCommand : AUICommand
        {
            private readonly CommandView _owner;
            public SyncCommand(CommandView view) : base(view, nameof(SyncCommand)) => _owner = view;

            protected override bool CanExecuteCore() => _owner.CanRun;

            protected override void ExecuteCore()
            {
                if (_owner.Fail)
                    throw new InvalidOperationException("fake command failure");
                _owner.Executed++;
            }
        }

        public sealed class AsyncCommand : AUIAsyncCommand
        {
            private readonly CommandView _owner;
            public AsyncCommand(CommandView view, bool allowConcurrent = false, bool cancelOnClose = true)
                : base(view, nameof(AsyncCommand))
            {
                _owner = view;
                AllowConcurrentExecutions = allowConcurrent;
                CancelOnClose = cancelOnClose;
            }

            protected override bool CanExecuteCore() => _owner.CanRun;

            protected override async ValueTask ExecuteCore(CancellationToken ct)
            {
                _owner.Entered++;
                _owner.LastToken = ct;
                _owner.Started.TrySetResult(true);
                try
                {
                    await _owner.Gate.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _owner.Cancelled++;
                    throw;
                }
                if (_owner.Fail)
                    throw new InvalidOperationException("fake command failure");
                _owner.Executed++;
            }
        }
    }

    private static async Task<PluginHost> StartAsync(TestUIAdapter adapter)
    {
        var host = new PluginHost(new UIPlugin(adapter, 2));
        await host.StartAsync();
        return host;
    }

    [Test]
    public async Task DuplicateAsyncExecutionIsRejectedUnlessConcurrencyIsAllowed()
    {
        var adapter = new TestUIAdapter().Map<CommandView>();
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var view = await ui.OpenAsync<CommandView>();
            var command = new CommandView.AsyncCommand(view!);
            var first = command.Execute(CancellationToken.None).AsTask();
            await view!.Started.Task;

            var second = command.Execute(CancellationToken.None).AsTask();
            Assert.That(second.IsCompleted, Is.True);
            Assert.That(view.Entered, Is.EqualTo(1));
            Assert.That(view.IsCommandRunning(nameof(CommandView.AsyncCommand)), Is.True);

            view.Gate.TrySetResult(true);
            await first;
            Assert.That(view.Executed, Is.EqualTo(1));
            Assert.That(view.IsCommandRunning(nameof(CommandView.AsyncCommand)), Is.False);

            var concurrentView = view;
            var concurrentCommand = new CommandView.AsyncCommand(concurrentView, allowConcurrent: true);
            var third = concurrentCommand.Execute(CancellationToken.None).AsTask();
            await concurrentView.Started.Task;
            var fourth = concurrentCommand.Execute(CancellationToken.None).AsTask();
            Assert.That(concurrentView.Entered, Is.EqualTo(3), "opt-in concurrency must allow overlapping executions");
            concurrentView.Gate.TrySetResult(true);
            await third;
            await fourth;
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task RunningStateIsOwnedPerViewInstance()
    {
        var adapter = new TestUIAdapter().Map<CommandView>();
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var left = await ui.OpenAsync<CommandView>(EUIOpenMode.Multiple);
            var right = await ui.OpenAsync<CommandView>(EUIOpenMode.Multiple);
            Assert.That(left, Is.Not.SameAs(right));

            var run = new CommandView.AsyncCommand(left!).Execute(CancellationToken.None).AsTask();
            await left!.Started.Task;
            Assert.That(left.IsCommandRunning(nameof(CommandView.AsyncCommand)), Is.True);
            Assert.That(right!.IsCommandRunning(nameof(CommandView.AsyncCommand)), Is.False);

            var other = new CommandView.AsyncCommand(right).Execute(CancellationToken.None).AsTask();
            await right.Started.Task;
            Assert.That(right.Entered, Is.EqualTo(1));

            left.Gate.TrySetResult(true);
            right.Gate.TrySetResult(true);
            await run;
            await other;
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task CanExecuteIsCheckedAgainAtExecutionTimeAndStateChangesAreObservable()
    {
        var adapter = new TestUIAdapter().Map<CommandView>();
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var view = await ui.OpenAsync<CommandView>();
            view!.CanRun = false;
            var sync = new CommandView.SyncCommand(view);
            Assert.That(sync.CanExecute(), Is.False);
            sync.Execute();
            Assert.That(view.Executed, Is.Zero);

            view.CanRun = true;
            Assert.That(sync.CanExecute(), Is.True);
            sync.Execute();
            Assert.That(view.Executed, Is.EqualTo(1));

            view.TrackCommandState();
            var async = new CommandView.AsyncCommand(view);
            var run = async.Execute(CancellationToken.None).AsTask();
            await view.Started.Task;
            view.Gate.TrySetResult(true);
            await run;
            Assert.That(view.CommandStateChanges, Is.EqualTo(new[] { nameof(CommandView.AsyncCommand), nameof(CommandView.AsyncCommand) }));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task ExceptionsRestoreRunningAndCanBeReportedAtTheBoundary()
    {
        var adapter = new TestUIAdapter().Map<CommandView>();
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var view = await ui.OpenAsync<CommandView>();
            var sync = new CommandView.SyncCommand(view!);
            view!.Fail = true;
            Assert.Throws<InvalidOperationException>(() => sync.Execute());
            Assert.That(view.IsCommandRunning(nameof(CommandView.SyncCommand)), Is.False);

            var async = new CommandView.AsyncCommand(view);
            var run = async.Execute(CancellationToken.None).AsTask();
            await view.Started.Task;
            view.Gate.TrySetResult(true);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await run);
            Assert.That(view.IsCommandRunning(nameof(CommandView.AsyncCommand)), Is.False);

            view.Fail = false;
            sync.Execute();
            view.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var retry = new CommandView.AsyncCommand(view).Execute(CancellationToken.None).AsTask();
            await view.Started.Task;
            view.Gate.TrySetResult(true);
            await retry;
            Assert.That(view.Executed, Is.EqualTo(2));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task CloseRequestsCancellationForCancelOnCloseCommands()
    {
        var adapter = new TestUIAdapter().Map<CommandView>();
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var view = await ui.OpenAsync<CommandView>();
            var command = new CommandView.AsyncCommand(view!);
            var run = command.Execute(CancellationToken.None).AsTask();
            await view!.Started.Task;
            Assert.That(view.IsCommandRunning(nameof(CommandView.AsyncCommand)), Is.True);

            ui.Close(view);
            await Assert.CatchAsync<OperationCanceledException>(async () => await run);
            Assert.That(view.Cancelled, Is.EqualTo(1));
            Assert.That(view.IsCommandRunning(nameof(CommandView.AsyncCommand)), Is.False);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task DurableCommandsKeepRunningAcrossCloseAndReopenWithoutLosingTheGuard()
    {
        var adapter = new TestUIAdapter().Map<CommandView>();
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var view = await ui.OpenAsync<CommandView>();
            var command = new CommandView.AsyncCommand(view!, cancelOnClose: false);
            var run = command.Execute(CancellationToken.None).AsTask();
            await view!.Started.Task;

            ui.Close(view);
            Assert.That(view.IsCommandRunning(nameof(CommandView.AsyncCommand)), Is.True);
            var reopened = await ui.OpenAsync<CommandView>();
            Assert.That(reopened, Is.SameAs(view));
            Assert.That(view.Cancelled, Is.Zero);

            var duplicate = new CommandView.AsyncCommand(view, cancelOnClose: false).Execute(CancellationToken.None).AsTask();
            Assert.That(duplicate.IsCompleted, Is.True);
            Assert.That(view.Entered, Is.EqualTo(1));

            view.Gate.TrySetResult(true);
            await run;
            Assert.That(view.IsCommandRunning(nameof(CommandView.AsyncCommand)), Is.False);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task StartNotificationFailureRestoresRunningAndAllowsRetry()
    {
        var adapter = new TestUIAdapter().Map<CommandView>();
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var view = await ui.OpenAsync<CommandView>();
            var notifications = 0;
            view!.CommandStateChanged += name =>
            {
                if (name != nameof(CommandView.SyncCommand))
                    return;
                notifications++;
                if (notifications == 1)
                    throw new InvalidOperationException("display binding failed while publishing the Running state");
            };

            var command = new CommandView.SyncCommand(view);
            Assert.Throws<InvalidOperationException>(() => command.Execute());
            Assert.That(view.IsCommandRunning(nameof(CommandView.SyncCommand)), Is.False,
                "a throwing start notification must not leave the command Running forever");
            Assert.That(view.Executed, Is.Zero, "business code must not run when the start notification fails");

            command.Execute();
            Assert.That(view.Executed, Is.EqualTo(1), "the command must be retryable after the binding recovers");
            Assert.That(view.IsCommandRunning(nameof(CommandView.SyncCommand)), Is.False);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task ClosingTheWindowDuringTheStartNotificationCancelsTheAsyncCommand()
    {
        var adapter = new TestUIAdapter().Map<CommandView>();
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var view = await ui.OpenAsync<CommandView>();
            var closed = false;
            view!.CommandStateChanged += name =>
            {
                if (closed || name != nameof(CommandView.AsyncCommand) || !view.IsCommandRunning(name))
                    return;
                closed = true;
                // A display subscriber may close the window synchronously while Running is published;
                // cancellation registration happens before the notification, so it must reach the token.
                ui.Close(view);
            };

            var command = new CommandView.AsyncCommand(view);
            var run = command.Execute(CancellationToken.None).AsTask();
            await Assert.CatchAsync<OperationCanceledException>(async () => await run);

            Assert.That(view.Entered, Is.Zero, "a command cancelled during its start notification must not enter business code");
            Assert.That(view.IsCommandRunning(nameof(CommandView.AsyncCommand)), Is.False);
            Assert.That(adapter.ClosedCount, Is.EqualTo(1));
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
