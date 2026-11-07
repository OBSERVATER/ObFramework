using ObFramework.Core;
using ObFramework.Extension;
using VContainer;
using static ObFramework.Test.Core.Plugins.PluginHostTest;

namespace ObFramework.Test.Core.Plugins;

[TestFixture]
public class PluginArchitectureTest
{
    public interface IConsumerSystem : ISystem
    {
        IPluginContext ServiceContext { get; }
    }
    public interface IConsumerService : IService { IPluginContext Plugins { get; } }
    public sealed class ConsumerService : IConsumerService
    {
        public IPluginContext Plugins { get; }
        public ConsumerService(IPluginContext plugins) => Plugins = plugins;
    }
    public sealed class ConsumerSystem : ASystem, IConsumerSystem
    {
        public IPluginContext ServiceContext { get; private set; } = null!;
        protected override void OnInit()
        {
            var architecture = GetArchitecture();
            architecture.RegisterLocalService<IConsumerService, ConsumerService>(this);
            ServiceContext = architecture.GetLocalService<IConsumerService>(this).Plugins;
        }
    }
    public sealed class FirstArchitecture : AArchitecture<FirstArchitecture>
    {
        protected override void Init()
        {
            RegisterSystem<IConsumerSystem, ConsumerSystem>();
            RegisterPubService<IConsumerService, ConsumerService>();
        }
        public IPluginContext PublicServiceContext => Resolver.Resolve<IConsumerService>().Plugins;
        public ICounter ResolveCounter() => Resolver.Resolve<ICounter>();
    }
    public sealed class SecondArchitecture : AArchitecture<SecondArchitecture>
    {
        protected override void Init() { }
    }
    public sealed class CounterCommand : ACommand<ICounter>
    {
        public override ICounter Execute() => this.GetPlugin<ICounter>();
    }
    public sealed class CounterData : IRuntimeData
    {
        public string Id => "counter";
        public ICounter Counter { get; }
        public CounterData(ICounter counter) => Counter = counter;
    }
    public sealed class CounterQuery : AQuery<CounterData>
    {
        protected override void OnRefresh() => _data["counter"] = new CounterData(this.GetPlugin<ICounter>());
    }
    [Test]
    public async Task ArchitecturesAndBusinessObjectsBorrowTheSameGameCapabilities()
    {
        var host = new PluginHost(new CounterPlugin(new List<string>()));
        await host.StartAsync();
        FirstArchitecture.InitInterface(host.Context);
        SecondArchitecture.InitInterface(host.Context);
        var first = (FirstArchitecture)FirstArchitecture.Arch;
        var second = SecondArchitecture.Arch;
        var counter = host.Context.Get<ICounter>();
        try
        {
            Assert.That(first.Plugins, Is.SameAs(host.Context));
            Assert.That(((IArchitecture)first).GetPlugin<ICounter>(), Is.SameAs(counter));
            var system = (ConsumerSystem)((IArchitecture)first).GetSystem<IConsumerSystem>();
            Assert.That(system.GetPlugin<ICounter>(), Is.SameAs(counter));
            Assert.That(((ICanGetPlugin)system).GetPlugin<ICounter>(), Is.SameAs(counter));
            Assert.That(system.ServiceContext, Is.SameAs(host.Context));
            Assert.That(first.PublicServiceContext, Is.SameAs(host.Context));
            Assert.Throws<VContainerException>(() => first.ResolveCounter());
            Assert.That(first.SendCommand(new CounterCommand()), Is.SameAs(counter));
            var query = ((IArchitecture)first).SendQuery<CounterQuery, CounterData>();
            Assert.That(query.TryGet("counter", out var data), Is.True);
            Assert.That(data!.Counter, Is.SameAs(counter));
            first.Deinit();
            Assert.That(second.GetPlugin<ICounter>(), Is.SameAs(counter));
            Assert.That(counter.Disposals, Is.Zero);
        }
        finally
        {
            first.Deinit();
            second.Deinit();
            await host.StopAsync();
        }
        Assert.That(counter.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void CoreCanRunWithoutPlugins()
    {
        SecondArchitecture.InitInterface();
        var architecture = SecondArchitecture.Arch;
        try
        {
            Assert.Throws<InvalidOperationException>(() => architecture.GetPlugin<ICounter>());
        }
        finally { architecture.Deinit(); }
    }
}
