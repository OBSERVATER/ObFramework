using ObFramework.Core;
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
    public interface IConsumerService : IService, ICanGetPlugin { }
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
        public override ICounter Execute() => Plugins.Get<ICounter>();
    }
    public sealed class CounterData : IRuntimeData
    {
        public string Id => "counter";
        public ICounter Counter { get; }
        public CounterData(ICounter counter) => Counter = counter;
    }
    public sealed class CounterQuery : AQuery<CounterData>
    {
        protected override void OnRefresh() => _data["counter"] = new CounterData(Plugins.Get<ICounter>());
    }
    public sealed class Helper
    {
        public ICounter Counter { get; }
        public Helper(ICounter counter) => Counter = counter;
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
            Assert.That(((ICanGetPlugin)first).Plugins.Get<ICounter>(), Is.SameAs(counter));
            var system = (ConsumerSystem)((IArchitecture)first).GetSystem<IConsumerSystem>();
            Assert.That(system.Plugins, Is.SameAs(host.Context));
            Assert.That(((ICanGetPlugin)system).Plugins, Is.SameAs(host.Context));
            Assert.That(system.ServiceContext, Is.SameAs(host.Context));
            Assert.That(first.PublicServiceContext, Is.SameAs(host.Context));
            Assert.Throws<VContainerException>(() => first.ResolveCounter());
            Assert.That(first.SendCommand(new CounterCommand()), Is.SameAs(counter));
            var query = ((IArchitecture)first).SendQuery<CounterQuery, CounterData>();
            Assert.That(query.TryGet("counter", out var data), Is.True);
            Assert.That(data!.Counter, Is.SameAs(counter));
            Assert.That(new Helper(counter).Counter, Is.SameAs(counter));
            first.Deinit();
            Assert.That(second.Plugins.Get<ICounter>(), Is.SameAs(counter));
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
    public async Task RejectsEarlyContextAndReplacementButAllowsSameContext()
    {
        var host = new PluginHost();
        var other = new PluginHost();
        Assert.Throws<InvalidOperationException>(() => FirstArchitecture.InitInterface(host.Context));
        await host.StartAsync();
        await other.StartAsync();
        FirstArchitecture.InitInterface(host.Context);
        try
        {
            FirstArchitecture.InitInterface(host.Context);
            Assert.Throws<InvalidOperationException>(() => FirstArchitecture.InitInterface(other.Context));
            Assert.Throws<InvalidOperationException>(() => FirstArchitecture.InitInterface());
        }
        finally
        {
            FirstArchitecture.Arch.Deinit();
            await host.StopAsync();
            await other.StopAsync();
        }
        Assert.Throws<InvalidOperationException>(() => FirstArchitecture.InitInterface(host.Context));
    }

    [Test]
    public void CoreCanRunWithoutPluginsAndQueryNeedsArchitectureAssignment()
    {
        SecondArchitecture.InitInterface();
        var architecture = SecondArchitecture.Arch;
        try
        {
            Assert.Throws<InvalidOperationException>(() => architecture.Plugins.Get<ICounter>());
            Assert.Throws<InvalidOperationException>(() => new CounterQuery().Refresh());
        }
        finally { architecture.Deinit(); }
    }

    [Test]
    public void ConsumerContractsExposeNoLifecycleOrContainerOperations()
    {
        Assert.That(typeof(IPluginContext).GetMethods().Select(x => x.Name), Is.EqualTo(new[] { "Get" }));
        Assert.That(typeof(ICanGetPlugin).GetInterfaces(), Is.Empty);
        Assert.That(typeof(ICanGetPlugin).GetProperty("Plugins")!.CanWrite, Is.False);
        Assert.That(typeof(IService).GetInterfaces(), Does.Not.Contain(typeof(ICanGetPlugin)));
        var bases = new[] { typeof(ASystem), typeof(AModel), typeof(ACommand), typeof(ACommand<int>), typeof(AAsyncCommand), typeof(AAsyncCommand<int>), typeof(CounterQuery) };
        foreach (var type in bases)
            Assert.That(type.GetProperty("Plugins")!.PropertyType, Is.EqualTo(typeof(IPluginContext)));
    }
}
