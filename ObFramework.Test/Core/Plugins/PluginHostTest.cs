using ObFramework.Core;

namespace ObFramework.Test.Core.Plugins;

[TestFixture]
public class PluginHostTest
{
    public sealed class CounterPlugin : IPlugin
    {
        private readonly List<string> _events;
        public int Stops { get; private set; }
        public CounterPlugin(List<string> events) => _events = events;
        public void Register(PluginRegistration registration) { }
        public Task InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            _events.Add("counter:init");
            return Task.CompletedTask;
        }
        public Task StopAsync(IPluginContext plugins)
        {
            Stops++;
            _events.Add("counter:stop");
            return Task.CompletedTask;
        }
    }

    public sealed class ConsumerPlugin : IPlugin
    {
        private readonly List<string> _events;
        public ConsumerPlugin(List<string> events) => _events = events;
        public void Register(PluginRegistration registration) => registration.Require<CounterPlugin>();
        public Task InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            Assert.That(plugins.Get<CounterPlugin>().Stops, Is.Zero);
            _events.Add("consumer:init");
            return Task.CompletedTask;
        }
        public Task StopAsync(IPluginContext plugins)
        {
            Assert.That(plugins.Get<CounterPlugin>().Stops, Is.Zero);
            _events.Add("consumer:stop");
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task StartsDependenciesFirstAndStopsInReverseOnce()
    {
        var events = new List<string>();
        var counter = new CounterPlugin(events);
        var host = new PluginHost(new ConsumerPlugin(events), counter);
        await host.StartAsync();
        Assert.That(host.Context.Get<CounterPlugin>(), Is.SameAs(counter));
        await host.StopAsync();
        await host.StopAsync();
        Assert.That(events, Is.EqualTo(new[] { "counter:init", "consumer:init", "consumer:stop", "counter:stop" }));
        Assert.That(counter.Stops, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => host.Context.Get<CounterPlugin>());
    }
}
