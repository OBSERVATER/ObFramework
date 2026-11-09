using ObFramework.Core;
using static ObFramework.Test.Core.Plugins.PluginHostTest;

namespace ObFramework.Test.Core.Plugins;

[TestFixture]
public class PluginLifecycleTest
{
    private sealed class First { }
    private sealed class Second { }
    private sealed class Third { }

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
    [TestCase("cycle")]
    public async Task RejectsInvalidGraphBeforeAnyInitialization(string problem)
    {
        var initialized = false;
        var first = new Plugin<First>
        {
            Initialize = (_, _) => { initialized = true; return Task.CompletedTask; }
        };
        var second = new Plugin<Second>();
        IPlugin[] plugins = { first, second };
        switch (problem)
        {
            case "missing": second.Configure = r => r.Require<Plugin<Third>>(); break;
            case "duplicate plugin": plugins = new IPlugin[] { first, first }; break;
            case "cycle":
                first.Configure = r => r.Require<Plugin<Second>>();
                second.Configure = r => r.Require<Plugin<First>>();
                break;
        }
        var host = new PluginHost(plugins);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());
        Assert.That(initialized, Is.False);
        Assert.Throws<InvalidOperationException>(() => host.StartAsync());
    }

    [Test]
    public async Task BusinessContextWaitsForWholeHostAndOnlyEnabledPluginsAreVisible()
    {
        var entered = new TaskCompletionSource<bool>();
        var release = new TaskCompletionSource<bool>();
        var plugin = new Plugin<First>
        {
            Initialize = async (plugins, _) =>
            {
                entered.SetResult(true);
                await release.Task;
            }
        };
        var host = new PluginHost(plugin);
        var context = host.Context;
        Assert.Throws<InvalidOperationException>(() => context.Get<Plugin<First>>());
        var start = host.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<InvalidOperationException>(() => context.Get<Plugin<First>>());
        release.SetResult(true);
        await start;
        Assert.That(context.Get<Plugin<First>>(), Is.SameAs(plugin));
        Assert.Throws<InvalidOperationException>(() => context.Get<Plugin<Second>>());
        await host.StopAsync();
        Assert.Throws<InvalidOperationException>(() => context.Get<Plugin<First>>());
    }

    [Test]
    public async Task FailureCleansPartialPluginThenDependenciesAndKeepsOriginalError()
    {
        var events = new List<string>();
        var original = new InvalidOperationException("init failed");
        CounterPlugin? counter = null;
        var host = new PluginHost(new CounterPlugin(events), new Plugin<First>
        {
            Configure = r => r.Require<CounterPlugin>(),
            Initialize = (plugins, _) => { counter = plugins.Get<CounterPlugin>(); events.Add("partial:init"); throw original; },
            Stop = plugins =>
            {
                Assert.That(plugins.Get<CounterPlugin>().Stops, Is.Zero);
                events.Add("partial:stop");
                throw new ApplicationException("cleanup failed");
            }
        });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());
        Assert.That(error, Is.SameAs(original));
        Assert.That(error!.Data["PluginCleanupErrors"], Is.TypeOf<AggregateException>());
        Assert.That(events, Is.EqualTo(new[] { "counter:init", "partial:init", "partial:stop", "counter:stop" }));
        Assert.That(counter!.Stops, Is.EqualTo(1));
        await host.StopAsync();
        Assert.That(counter.Stops, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => host.StartAsync());
    }

    [Test]
    public async Task ShutdownWaitsForInitializationToUnwindBeforeCleanup()
    {
        var entered = new TaskCompletionSource<bool>();
        var unwinding = new TaskCompletionSource<bool>();
        var release = new TaskCompletionSource<bool>();
        var events = new List<string>();
        var host = new PluginHost(new Plugin<First>
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
    public async Task PluginCannotAccessAnUndeclaredDependencyDuringInitialization()
    {
        var events = new List<string>();
        var host = new PluginHost(new CounterPlugin(events), new Plugin<First>
        {
            Initialize = (plugins, _) =>
            {
                plugins.Get<CounterPlugin>();
                return Task.CompletedTask;
            }
        });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());
        Assert.That(error!.Message, Does.Contain("did not declare dependency"));
        Assert.That(events, Is.EqualTo(new[] { "counter:init", "counter:stop" }));
        await host.StopAsync();
    }
}
