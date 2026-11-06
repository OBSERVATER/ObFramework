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
    public sealed class Third : IThird { }

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
    [TestCase("self cycle")]
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
            case "self cycle": first.Configure = r => { r.Export<IFirst, First>(); r.Require<IFirst>(); }; break;
        }
        var host = new PluginHost(plugins);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());
        Assert.That(error!.Message, Does.Contain(problem == "missing" ? nameof(IThird) : nameof(Plugin<IFirst>).Split('`')[0]));
        Assert.That(initialized, Is.False);
        Assert.Throws<InvalidOperationException>(() => host.StartAsync());
    }

    [Test]
    public async Task IndependentPluginsKeepConfigurationOrder()
    {
        var events = new List<int>();
        var host = new PluginHost(
            new Plugin<IFirst> { Initialize = (_, _) => { events.Add(1); return Task.CompletedTask; } },
            new Plugin<ISecond> { Initialize = (_, _) => { events.Add(2); return Task.CompletedTask; } },
            new Plugin<IThird> { Initialize = (_, _) => { events.Add(3); return Task.CompletedTask; } });
        await host.StartAsync();
        await host.StopAsync();
        Assert.That(events, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task BusinessContextWaitsForWholeHostAndOnlyExportsAreVisible()
    {
        var entered = new TaskCompletionSource<bool>();
        var release = new TaskCompletionSource<bool>();
        PluginRegistration? registration = null;
        var host = new PluginHost(new Plugin<IFirst>
        {
            Configure = r => { registration = r; r.Export<IFirst, First>(); r.Register<Second>(); },
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
        Assert.Throws<InvalidOperationException>(() => registration!.Register<Third>());
        Assert.Throws<InvalidOperationException>(() => registration!.ExportInstance<IThird>(new Third()));
        release.SetResult(true);
        await start;
        Assert.That(context.Get<IFirst>(), Is.TypeOf<First>());
        Assert.Throws<InvalidOperationException>(() => context.Get<Second>());
        await host.StopAsync();
        Assert.Throws<InvalidOperationException>(() => context.Get<IFirst>());
    }

    [Test]
    public async Task InitializationCannotUseUndeclaredDependencies()
    {
        var host = new PluginHost(new Plugin<IFirst> { Configure = r => r.Export<IFirst, First>() }, new Plugin<ISecond>
        {
            Initialize = (plugins, _) =>
            {
                Assert.Throws<InvalidOperationException>(() => plugins.Get<IFirst>());
                return Task.CompletedTask;
            }
        });
        await host.StartAsync();
        await host.StopAsync();
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancellationWaitsForInitializationToUnwindBeforeCleanup(bool stopHost)
    {
        using var cancellation = new CancellationTokenSource();
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
        var start = host.StartAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task? stop = null;
        if (stopHost)
            stop = host.StopAsync();
        else
            cancellation.Cancel();
        await unwinding.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(events, Is.Empty);
        if (stop != null)
            Assert.That(stop.IsCompleted, Is.False);
        release.SetResult(true);
        await Assert.CatchAsync<OperationCanceledException>(async () => await start);
        await (stop ?? host.StopAsync());
        Assert.That(events, Is.EqualTo(new[] { "init:unwound", "stop" }));
    }

    [Test]
    public async Task StopFailureDoesNotPreventOtherStopsOrContainerDisposal()
    {
        var events = new List<string>();
        var host = new PluginHost(new CounterPlugin(events), new Plugin<IFirst>
        {
            Configure = r => r.Require<ICounter>(),
            Stop = _ => throw new ApplicationException("stop failed")
        });
        await host.StartAsync();
        var counter = host.Context.Get<ICounter>();
        var stop = host.StopAsync();
        var error = await Assert.ThrowsAsync<AggregateException>(async () => await stop);
        Assert.That(error!.InnerExceptions.Single().Message, Is.EqualTo("stop failed"));
        Assert.That(events, Does.Contain("counter:stop"));
        Assert.That(counter.Disposals, Is.EqualTo(1));
        Assert.That(host.StopAsync(), Is.SameAs(stop));
    }

    [Test]
    public async Task ExternalInstanceRemainsOwnedByItsCreator()
    {
        var counter = new Counter();
        var host = new PluginHost(new Plugin<IFirst> { Configure = r => r.ExportInstance<ICounter>(counter) });
        await host.StartAsync();
        Assert.That(host.Context.Get<ICounter>(), Is.SameAs(counter));
        await host.StopAsync();
        Assert.That(counter.Disposals, Is.Zero);
        counter.Dispose();
    }

    public interface IBrokenDisposable { int Disposals { get; } }
    public sealed class BrokenDisposable : IBrokenDisposable, IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose()
        {
            Disposals++;
            throw new ApplicationException("dispose failed");
        }
    }

    [Test]
    public async Task FailingDisposeDoesNotSkipRemainingContainerOwnedObjects()
    {
        var host = new PluginHost(new Plugin<IFirst>
        {
            Configure = r => { r.Export<ICounter, Counter>(); r.Export<IBrokenDisposable, BrokenDisposable>(); }
        });
        await host.StartAsync();
        var counter = host.Context.Get<ICounter>();
        var broken = host.Context.Get<IBrokenDisposable>();
        await Assert.ThrowsAsync<AggregateException>(async () => await host.StopAsync());
        Assert.That(broken.Disposals, Is.EqualTo(1));
        Assert.That(counter.Disposals, Is.EqualTo(1));
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

    public interface ICounterAlias { }
    public sealed class AliasedCounter : ICounter, ICounterAlias, IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }

    [Test]
    public async Task MultipleExportsOfOneImplementationShareOneContainerOwnedInstance()
    {
        var host = new PluginHost(new Plugin<IFirst>
        {
            Configure = r => { r.Export<ICounter, AliasedCounter>(); r.Export<ICounterAlias, AliasedCounter>(); }
        });
        await host.StartAsync();
        var counter = host.Context.Get<ICounter>();
        Assert.That(host.Context.Get<ICounterAlias>(), Is.SameAs(counter));
        await host.StopAsync();
        Assert.That(counter.Disposals, Is.EqualTo(1));
    }

    [Test]
    public async Task AlreadyCanceledStartDoesNotInitializeAnyPlugin()
    {
        var events = new List<string>();
        var host = new PluginHost(new CounterPlugin(events));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.CatchAsync<OperationCanceledException>(async () => await host.StartAsync(cancellation.Token));
        await host.StopAsync();
        Assert.That(events, Is.Empty);
        Assert.Throws<InvalidOperationException>(() => host.StartAsync());
    }

    [Test]
    public async Task CancellationCallbackFailureStillWaitsForStartupAndCleansResources()
    {
        var entered = new TaskCompletionSource<bool>();
        var release = new TaskCompletionSource<bool>();
        var stopped = false;
        var host = new PluginHost(new Plugin<IFirst>
        {
            Initialize = async (_, ct) =>
            {
                using var registration = ct.Register(() => throw new ApplicationException("callback failed"));
                entered.SetResult(true);
                await release.Task;
                ct.ThrowIfCancellationRequested();
            },
            Stop = _ => { stopped = true; return Task.CompletedTask; }
        });
        var start = host.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = host.StopAsync();
        Assert.That(stopped, Is.False);
        release.SetResult(true);
        await Assert.CatchAsync<OperationCanceledException>(async () => await start);
        var error = await Assert.ThrowsAsync<AggregateException>(async () => await stop);
        Assert.That(error!.Flatten().InnerExceptions.Single().Message, Is.EqualTo("callback failed"));
        Assert.That(stopped, Is.True);
    }

    [Test]
    public async Task StopBeforeStartIsFinalAndIdempotent()
    {
        var host = new PluginHost();
        var stop = host.StopAsync();
        await stop;
        Assert.That(host.StopAsync(), Is.SameAs(stop));
        Assert.Throws<InvalidOperationException>(() => host.StartAsync());
    }
}
