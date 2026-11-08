using ObFramework.Core;
using ObFramework.Extension;

namespace ObFramework.Test.Core.Plugins;

[TestFixture]
public class PluginApplicationTest
{
    public interface ITrace { List<string> Events { get; } }
    public sealed class Trace : ITrace { public List<string> Events { get; } = new(); }
    public sealed class Plugin : IPlugin
    {
        public readonly Trace Trace = new();
        public TaskCompletionSource<bool>? Pending;
        public void Register(PluginRegistration registration) => registration.ExportInstance<ITrace>(Trace);
        public async Task InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            Trace.Events.Add("plugin-start");
            if (Pending != null)
                await Pending.Task;
        }
        public Task StopAsync(IPluginContext plugins)
        {
            Trace.Events.Add("plugin-stop");
            return Task.CompletedTask;
        }
    }
    public interface IBusiness : ISystem { }
    public sealed class Business : ASystem, IBusiness
    {
        protected override void OnInit() => this.GetPlugin<ITrace>().Events.Add("architecture-start");
        protected override void OnDeinit() => this.GetPlugin<ITrace>().Events.Add("architecture-stop");
    }
    public sealed class Game : AArchitecture<Game>
    {
        protected override void Init() => RegisterSystem<IBusiness, Business>();
    }
    public sealed class Application : PluginApplication
    {
        public readonly Plugin Plugin = new();
        public bool FailEntry;
        protected override IPlugin[] CreatePlugins() => new IPlugin[] { Plugin };
        protected override IArchitecture CreateArchitecture(IPluginContext plugins)
        {
            Game.InitInterface(plugins);
            return Game.Arch;
        }
        protected override Task EnterGameAsync(CancellationToken ct)
        {
            Architecture.GetPlugin<ITrace>().Events.Add("enter");
            if (FailEntry)
                throw new InvalidOperationException("entry failed");
            return Task.CompletedTask;
        }
        protected override Task StopGameAsync()
        {
            Plugin.Trace.Events.Add("business-stop");
            return Task.CompletedTask;
        }
        protected override void ReleaseAdapters() => Plugin.Trace.Events.Add("adapter-release");
    }

    [Test]
    public async Task StopDrainsBusinessBeforeArchitecturePluginsAndAdapters()
    {
        var application = new Application();
        await application.StartAsync();
        var work = application.RunAsync(async ct =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { application.Plugin.Trace.Events.Add("work-stop"); }
        });
        await application.StopAsync();
        await Assert.CatchAsync<OperationCanceledException>(async () => await work);
        await application.StopAsync();
        Assert.That(application.Plugin.Trace.Events, Is.EqualTo(new[]
        {
            "plugin-start", "architecture-start", "enter", "work-stop", "business-stop", "architecture-stop", "plugin-stop", "adapter-release"
        }));
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
        Assert.That(application.Plugin.Trace.Events, Is.EqualTo(new[] { "plugin-start", "plugin-stop", "adapter-release" }));
    }

    [Test]
    public async Task EntryFailureRollsBackArchitectureAndPlugins()
    {
        var application = new Application { FailEntry = true };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await application.StartAsync());
        await application.StopAsync();
        Assert.That(application.Plugin.Trace.Events, Is.EqualTo(new[]
        {
            "plugin-start", "architecture-start", "enter", "business-stop", "architecture-stop", "plugin-stop", "adapter-release"
        }));
    }
}
