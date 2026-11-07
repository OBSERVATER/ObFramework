using MessagePack;
using ObFramework.Core;
using ObFramework.Integrated.Save;
using ObFramework.Integrated.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ObFramework.Test.Core.Plugins;

[TestFixture]
public class SaveDiagnosticsPluginTest
{
    [MessagePackObject]
    public sealed class Snapshot : ASaveData
    {
        [Key(0)] public int Value { get; set; }
    }
    public interface IPlayerModel : IModel, ICanSave
    {
        int Value { get; set; }
    }
    public sealed class PlayerModel : AModel, IPlayerModel
    {
        public int Value { get; set; }
        public string Key => "player";
        public Type SaveDataType => typeof(Snapshot);
        public ASaveData Capture() => new Snapshot { Value = Value };
        public void Restore(ASaveData value) => Value = ((Snapshot)value).Value;
        protected override void OnInit() { }
    }
    public sealed class Architecture : AArchitecture<Architecture>
    {
        protected override void Init() => RegisterModel<IPlayerModel, PlayerModel>();
    }
    public sealed class Store : ISaveStore, IDisposable
    {
        public byte[] Data = Array.Empty<byte>();
        public int Disposals;
        public bool Block;
        public TaskCompletionSource<bool> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WriteAsync(byte[] data, string slotId)
        {
            if (Block)
            {
                Entered.TrySetResult(true);
                await Complete.Task;
            }
            Data = data;
        }
        public Task<byte[]> TryReadAsync(string slotId) => Task.FromResult(Data);
        public bool Exists(string slotId) => Data.Length > 0;
        public void Delete(string slotId) => Data = Array.Empty<byte>();
        public void Dispose() => Disposals++;
    }

    [Test]
    public async Task SaveBindsAfterArchitectureInitializationAndRoundTripsRealBytes()
    {
        var store = new Store();
        var host = new PluginHost(new SavePlugin(store));
        await host.StartAsync();
        var save = host.Context.Get<ISaveManager>();
        var binding = host.Context.Get<ISaveBinding>();
        Assert.That(binding, Is.SameAs(save));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await save.SaveAsync("slot"));
        Assert.Throws<InvalidOperationException>(() => binding.Bind(new Architecture()));
        Architecture.InitInterface(host.Context);
        var architecture = Architecture.Arch;
        try
        {
            binding.Bind(architecture);
            Assert.Throws<InvalidOperationException>(() => binding.Bind(architecture));
            var model = architecture.GetModel<IPlayerModel>();
            model.Value = 42;
            await save.SaveAsync("slot");
            Assert.That(store.Data, Is.Not.Empty);
            model.Value = 9;
            await save.LoadAsync("slot");
            Assert.That(model.Value, Is.EqualTo(42));
            await binding.UnbindAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await save.LoadAsync("slot"));
            binding.Bind(architecture);
            await binding.UnbindAsync();
        }
        finally
        {
            architecture.Deinit();
            await host.StopAsync();
        }
        Assert.That(store.Disposals, Is.Zero);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await save.SaveAsync("slot"));
    }

    [Test]
    public async Task ShutdownDrainsSaveBeforeReleasingModelReferences()
    {
        var store = new Store { Block = true };
        var host = new PluginHost(new SavePlugin(store));
        await host.StartAsync();
        Architecture.InitInterface(host.Context);
        var architecture = Architecture.Arch;
        try
        {
            host.Context.Get<ISaveBinding>().Bind(architecture);
            var save = host.Context.Get<ISaveManager>();
            var pending = save.SaveAsync("slot");
            await store.Entered.Task;
            var stop = host.StopAsync();
            Assert.That(stop.IsCompleted, Is.False);
            store.Complete.SetResult(true);
            await pending;
            await stop;
            Assert.That(store.Data, Is.Not.Empty);
        }
        finally
        {
            store.Complete.TrySetResult(true);
            await host.StopAsync();
            architecture.Deinit();
        }
    }

    public sealed class Sink : ILogEventSink, IDisposable
    {
        public List<LogEvent> Events { get; } = new();
        public int Disposals;
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
        public void Dispose() => Disposals++;
    }

    [Test]
    public async Task DiagnosticsFiltersAddsSourceAndFlushesWithoutDisposingBorrowedSink()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sink = new Sink();
        var host = new PluginHost(new DiagnosticsPlugin(new DiagnosticsOption(directory) { MinLevel = LogEventLevel.Warning }, sink));
        try
        {
            Assert.That(Directory.Exists(directory), Is.False);
            await host.StartAsync();
            var diagnostics = host.Context.Get<IDiagnosticsManager>();
            var logger = diagnostics.GetLogger("PluginTest");
            logger.Information("hidden-message");
            logger.Warning("visible-message");
            await host.StopAsync();
            await host.StopAsync();
            Assert.That(sink.Events.Count, Is.EqualTo(1));
            Assert.That(sink.Events[0].Properties["SourceContext"].ToString(), Is.EqualTo("\"PluginTest\""));
            Assert.That(sink.Disposals, Is.Zero);
            var text = File.ReadAllText(Directory.GetFiles(directory).Single());
            Assert.That(text, Does.Contain("visible-message").And.Not.Contain("hidden-message"));
            Assert.Throws<InvalidOperationException>(() => diagnostics.GetLogger("after-stop"));
        }
        finally
        {
            await host.StopAsync();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
