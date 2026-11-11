using ObFramework.Core;
using ObFramework.Core.FrameLoop;
using ObFramework.Extension;

namespace ObFramework.Test.Core.Plugins;

[TestFixture]
public class PluginApplicationTest
{
    public sealed class Plugin : IPlugin
    {
        public List<string> Events { get; } = new();
        public TaskCompletionSource<bool>? Pending;
        public Exception? RollbackFailure;
        public void Register(PluginRegistration registration) { }
        public async Task InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            Events.Add("plugin-start");
            if (Pending != null)
                await Pending.Task;
        }
        public Task StopAsync(IPluginContext plugins)
        {
            Events.Add("plugin-stop");
            return RollbackFailure == null ? Task.CompletedTask : Task.FromException(RollbackFailure);
        }
    }
    public interface IBusiness : ISystem { }
    public sealed class Business : ASystem, IBusiness
    {
        protected override void OnInit() => this.GetPlugin<Plugin>().Events.Add("architecture-start");
        protected override void OnDeinit() => this.GetPlugin<Plugin>().Events.Add("architecture-stop");
    }
    public sealed class Game : AArchitecture<Game>
    {
        protected override void Init() => RegisterSystem<IBusiness, Business>();
    }
    public sealed class Application : PluginApplication
    {
        public readonly Plugin Plugin = new();
        public bool FailEntry;
        public TaskCompletionSource<bool>? PendingEntry;
        protected override IPlugin[] CreatePlugins() => new IPlugin[] { Plugin };
        protected override IArchitecture CreateArchitecture(IPluginContext plugins)
        {
            Game.InitInterface(plugins);
            return Game.Interface;
        }
        protected override Task EnterGameAsync(CancellationToken ct)
        {
            Architecture.GetPlugin<Plugin>().Events.Add("enter");
            if (FailEntry)
                throw new InvalidOperationException("entry failed");
            return PendingEntry?.Task ?? Task.CompletedTask;
        }
        protected override Task StopGameAsync()
        {
            Plugin.Events.Add("business-stop");
            return Task.CompletedTask;
        }
        protected override void ReleaseAdapters() => Plugin.Events.Add("adapter-release");
    }

    private sealed class ApplicationDriver : IApplicationFrameLoopDriver
    {
        private readonly List<string> _events;
        public Action? OnRelease;
        public ApplicationDriver(List<string> events) => _events = events;
        public void BeginStartup(CancellationToken stopping) => _events.Add("driver-startup");
        public void Prepare(IArchitecture architecture)
        {
            Assert.That(architecture.GetSystem<IBusiness>().Initialized, Is.True);
            _events.Add("driver-prepare");
        }
        public void Bind(FrameDispatcher dispatcher) => _events.Add("driver-bind");
        public void Unbind() => _events.Add("driver-unbind");
        public Task ReleaseAsync()
        {
            OnRelease?.Invoke();
            _events.Add("driver-release");
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task DriverPreparesBeforeEntryAndReleasesBeforeDependenciesWithReentrantStop()
    {
        var application = new Application();
        var driver = new ApplicationDriver(application.Plugin.Events);
        Task? reentrantStop = null;
        driver.OnRelease = () =>
        {
            Assert.That(application.Architecture.GetSystem<IBusiness>().Initialized, Is.True);
            reentrantStop = application.StopAsync();
        };

        await application.StartAsync(driver);
        var stop = application.StopAsync();
        await stop;

        Assert.That(reentrantStop, Is.SameAs(stop));
        Assert.That(application.Plugin.Events, Is.EqualTo(new[]
        {
            "driver-startup", "plugin-start", "architecture-start", "driver-prepare", "driver-bind", "enter", "driver-unbind",
            "driver-release", "business-stop", "architecture-stop", "plugin-stop", "adapter-release"
        }));
    }

    [Test]
    public async Task StopDuringEntryUnbindsFramesAndDrainsWorkBeforeReleasingViews()
    {
        var entry = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishWork = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var application = new Application { PendingEntry = entry };
        var events = application.Plugin.Events;
        var start = application.StartAsync(new ApplicationDriver(events));
        var work = application.RunAsync(async ct =>
        {
            await finishWork.Task;
            events.Add("work-stop");
            ct.ThrowIfCancellationRequested();
        });

        var stop = application.StopAsync();
        var detachedDuringEntry = events.Contains("driver-unbind");
        finishWork.SetResult(true);
        entry.SetResult(true);
        await Assert.CatchAsync<OperationCanceledException>(async () => await start);
        await stop;
        await Assert.CatchAsync<OperationCanceledException>(async () => await work);
        Assert.That(detachedDuringEntry, Is.True);
        Assert.That(events, Is.EqualTo(new[]
        {
            "driver-startup", "plugin-start", "architecture-start", "driver-prepare", "driver-bind", "enter",
            "driver-unbind", "work-stop", "driver-release", "business-stop", "architecture-stop", "plugin-stop", "adapter-release"
        }));
    }

    [Test]
    public async Task StopReportsPluginRollbackWithoutReplacingTheStartupFailure()
    {
        var application = new Application();
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startupError = new InvalidOperationException("startup failed");
        var rollbackError = new InvalidOperationException("rollback failed");
        application.Plugin.Pending = pending;
        application.Plugin.RollbackFailure = rollbackError;

        var start = application.StartAsync();
        var stop = application.StopAsync();
        pending.SetException(startupError);

        Assert.That(await Assert.ThrowsAsync<InvalidOperationException>(async () => await start), Is.SameAs(startupError));
        var shutdown = await Assert.ThrowsAsync<AggregateException>(async () => await stop);
        Assert.That(shutdown!.Flatten().InnerExceptions, Is.EqualTo(new[] { rollbackError }));
    }

    [Test]
    public async Task StopDuringStartupWaitsForPluginAndNeverEntersBusiness()
    {
        var application = new Application();
        application.Plugin.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = application.StartAsync();
        var stop = application.StopAsync();
        Assert.That(stop.IsCompleted, Is.False);
        application.Plugin.Pending.SetResult(true);
        await Assert.CatchAsync<OperationCanceledException>(async () => await start);
        await stop;
        Assert.That(application.Plugin.Events, Is.EqualTo(new[] { "plugin-start", "plugin-stop", "adapter-release" }));
    }

    [Test]
    public async Task EntryFailureRollsBackArchitectureAndPlugins()
    {
        var application = new Application { FailEntry = true };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await application.StartAsync());
        await application.StopAsync();
        Assert.That(application.Plugin.Events, Is.EqualTo(new[]
        {
            "plugin-start", "architecture-start", "enter", "business-stop", "architecture-stop", "plugin-stop", "adapter-release"
        }));
    }
}
