using ObFramework.Core;

namespace ObFramework.Test.Core.Plugins;

[TestFixture]
public class PluginHostTest
{
    public interface ICounter
    {
        int Disposals { get; }
    }

    public sealed class Counter : ICounter, IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }

    public sealed class CounterPlugin : IPlugin
    {
        private readonly List<string> _events;
        public CounterPlugin(List<string> events) => _events = events;
        public void Register(PluginRegistration registration) => registration.Export<ICounter, Counter>();
        public Task InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            plugins.Get<ICounter>();
            _events.Add("counter:init");
            return Task.CompletedTask;
        }
        public Task StopAsync(IPluginContext plugins)
        {
            _events.Add("counter:stop");
            return Task.CompletedTask;
        }
    }

    public sealed class ConsumerPlugin : IPlugin
    {
        private readonly List<string> _events;
        public ConsumerPlugin(List<string> events) => _events = events;
        public void Register(PluginRegistration registration) => registration.Require<ICounter>();
        public Task InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            Assert.That(plugins.Get<ICounter>().Disposals, Is.Zero);
            _events.Add("consumer:init");
            return Task.CompletedTask;
        }
        public Task StopAsync(IPluginContext plugins)
        {
            Assert.That(plugins.Get<ICounter>().Disposals, Is.Zero);
            _events.Add("consumer:stop");
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task StartsDependenciesFirstAndStopsInReverseBeforeDisposingOnce()
    {
        var events = new List<string>();
        var host = new PluginHost(new ConsumerPlugin(events), new CounterPlugin(events));
        await host.StartAsync();
        var counter = host.Context.Get<ICounter>();
        Assert.That(host.Context.Get<ICounter>(), Is.SameAs(counter));
        await host.StopAsync();
        await host.StopAsync();
        Assert.That(events, Is.EqualTo(new[] { "counter:init", "consumer:init", "consumer:stop", "counter:stop" }));
        Assert.That(counter.Disposals, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => host.Context.Get<ICounter>());
    }
}
