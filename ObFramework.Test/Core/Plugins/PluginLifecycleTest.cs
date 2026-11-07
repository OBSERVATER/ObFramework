using ObFramework.Core;
using static ObFramework.Test.Core.Plugins.PluginHostTest;

namespace ObFramework.Test.Core.Plugins;

[TestFixture]
public class PluginLifecycleTest
{
    public interface IFirst { }
    public interface ISecond { }
    public interface IThird { }
    public sealed class First : IFirst { }
    public sealed class Second : ISecond { }

    public sealed class Plugin<T> : IPlugin
    {
        public Action<PluginRegistration> Configure { get; set; } = _ => { };
        public Func<IPluginContext, CancellationToken, Task> Initialize { get; set; } = (_, _) => Task.CompletedTask;
        public Func<IPluginContext, Task> Stop { get; set; } = _ => Task.CompletedTask;
        public void Register(PluginRegistration registration) => Configure(registration);
        public Task InitAsync(IPluginContext plugins, CancellationToken ct) => Initialize(plugins, ct);
        public Task StopAsync(IPluginContext plugins) => Stop(plugins);
    }

    [TestCase("missing")]
    [TestCase("duplicate plugin")]
    [TestCase("duplicate provider")]
    [TestCase("cycle")]
    public async Task RejectsInvalidGraphBeforeAnyInitialization(string problem)
    {
        var initialized = false;
        var first = new Plugin<IFirst>
        {
            Configure = r => r.Export<IFirst, First>(),
            Initialize = (_, _) => { initialized = true; return Task.CompletedTask; }
        };
        var second = new Plugin<ISecond>();
        IPlugin[] plugins = { first, second };
        switch (problem)
        {
            case "missing": second.Configure = r => r.Require<IThird>(); break;
            case "duplicate plugin": plugins = new IPlugin[] { first, first }; break;
            case "duplicate provider": second.Configure = r => r.Export<IFirst, First>(); break;
            case "cycle":
                first.Configure = r => { r.Export<IFirst, First>(); r.Require<ISecond>(); };
                second.Configure = r => { r.Export<ISecond, Second>(); r.Require<IFirst>(); };
                break;
        }
        var host = new PluginHost(plugins);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());
        Assert.That(initialized, Is.False);
        Assert.Throws<InvalidOperationException>(() => host.StartAsync());
    }

    [Test]
    public async Task BusinessContextWaitsForWholeHostAndOnlyExportsAreVisible()
    {
        var entered = new TaskCompletionSource<bool>();
        var release = new TaskCompletionSource<bool>();
        var host = new PluginHost(new Plugin<IFirst>
        {
            Configure = r => { r.Export<IFirst, First>(); r.Register<Second>(); },
            Initialize = async (plugins, _) =>
            {
                Assert.That(plugins.Get<IFirst>(), Is.TypeOf<First>());
                entered.SetResult(true);
                await release.Task;
            }
        });
        var context = host.Context;
        Assert.Throws<InvalidOperationException>(() => context.Get<IFirst>());
        var start = host.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<InvalidOperationException>(() => context.Get<IFirst>());
        release.SetResult(true);
        await start;
        Assert.That(context.Get<IFirst>(), Is.TypeOf<First>());
        Assert.Throws<InvalidOperationException>(() => context.Get<Second>());
        await host.StopAsync();
        Assert.Throws<InvalidOperationException>(() => context.Get<IFirst>());
    }

    [Test]
    public async Task FailureCleansPartialPluginThenDependenciesAndKeepsOriginalError()
    {
        var events = new List<string>();
        var original = new InvalidOperationException("init failed");
        ICounter? counter = null;
        var host = new PluginHost(new CounterPlugin(events), new Plugin<IFirst>
        {
            Configure = r => r.Require<ICounter>(),
            Initialize = (plugins, _) => { counter = plugins.Get<ICounter>(); events.Add("partial:init"); throw original; },
            Stop = plugins =>
            {
                Assert.That(plugins.Get<ICounter>().Disposals, Is.Zero);
                events.Add("partial:stop");
                throw new ApplicationException("cleanup failed");
            }
        });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());
        Assert.That(error, Is.SameAs(original));
        Assert.That(error!.Data["PluginCleanupErrors"], Is.TypeOf<AggregateException>());
        Assert.That(events, Is.EqualTo(new[] { "counter:init", "partial:init", "partial:stop", "counter:stop" }));
        Assert.That(counter!.Disposals, Is.EqualTo(1));
        await host.StopAsync();
        Assert.That(counter.Disposals, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => host.StartAsync());
    }

    [Test]
    public async Task ShutdownWaitsForInitializationToUnwindBeforeCleanup()
    {
        var entered = new TaskCompletionSource<bool>();
        var unwinding = new TaskCompletionSource<bool>();
        var release = new TaskCompletionSource<bool>();
        var events = new List<string>();
        var host = new PluginHost(new Plugin<IFirst>
        {
            Initialize = async (_, ct) =>
            {
                entered.SetResult(true);
                try { await Task.Delay(Timeout.Infinite, ct); }
                finally
                {
                    unwinding.SetResult(true);
                    await release.Task;
                    events.Add("init:unwound");
                }
            },
            Stop = _ => { events.Add("stop"); return Task.CompletedTask; }
        });
        var start = host.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = host.StopAsync();
        await unwinding.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(events, Is.Empty);
        Assert.That(stop.IsCompleted, Is.False);
        release.SetResult(true);
        await Assert.CatchAsync<OperationCanceledException>(async () => await start);
        await stop;
        Assert.That(events, Is.EqualTo(new[] { "init:unwound", "stop" }));
    }

    [Test]
    public async Task PrivateRegistrationCannotReplaceAnExportedCapability()
    {
        var initialized = false;
        var host = new PluginHost(new Plugin<IFirst>
        {
            Configure = r => r.Export<IFirst, First>(),
            Initialize = (_, _) => { initialized = true; return Task.CompletedTask; }
        }, new Plugin<ISecond> { Configure = r => r.RegisterInstance<IFirst>(new First()) });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());
        Assert.That(initialized, Is.False);
        await host.StopAsync();
    }
}
