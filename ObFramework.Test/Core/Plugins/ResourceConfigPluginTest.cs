using System.Text;
using Newtonsoft.Json;
using ObFramework.Core;
using ObFramework.Integrated.Config;
using ObFramework.Integrated.Resource;

namespace ObFramework.Test.Core.Plugins;

[TestFixture]
public class ResourceConfigPluginTest
{
    public sealed class Payload { }
    public sealed class AssetAdapter : IAssetAdapter<int>, IRawFileAdapter<int>, IDisposable
    {
        public int Reads;
        public int Disposals;
        public bool InvalidAsset;
        public bool Block;
        public bool IgnoreCancellation;
        public TaskCompletionSource<bool> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<int> Released { get; } = new();
        public Dictionary<string, string> Files { get; } = new();
        public AssetLoadResult<T, int> Read<T>(AssetKey key) where T : class => new(InvalidAsset ? null! : (T)(object)new Payload(), ++Reads);
        public async Task<AssetLoadResult<T, int>> ReadAsync<T>(AssetKey key, CancellationToken ct = default) where T : class
        {
            var handle = ++Reads;
            if (Block)
            {
                Entered.TrySetResult(true);
                await Complete.Task.WaitAsync(IgnoreCancellation ? CancellationToken.None : ct);
            }
            return new AssetLoadResult<T, int>(InvalidAsset ? null! : (T)(object)new Payload(), handle);
        }
        public Task<AssetLoadResult<byte[], int>> ReadRawFileAsync(string location)
            => Task.FromResult(new AssetLoadResult<byte[], int>(Encoding.UTF8.GetBytes(Files[location]), ++Reads));
        public void Release(int handle)
        {
            Released.Add(handle);
        }
        public void Dispose() => Disposals++;
    }

    [Test]
    public async Task SyncAsyncAndRawLoadsShareEntriesAndRespectHandleOwnership()
    {
        var adapter = new AssetAdapter();
        adapter.Files["raw"] = "hello";
        var host = new PluginHost(new ResourcePlugin<int>(adapter));
        await host.StartAsync();
        var assets = host.Context.Get<IAssetManager>();
        var first = assets.LoadAsset<Payload>("asset");
        var second = assets.LoadAsset<Payload>("asset");
        var third = await assets.LoadAssetAsync<Payload>("asset");
        Assert.That(second.Get<Payload>(), Is.SameAs(first.Get<Payload>()));
        Assert.That(third.Get<Payload>(), Is.SameAs(first.Get<Payload>()));
        Assert.That(adapter.Reads, Is.EqualTo(1));
        first.Dispose();
        second.Dispose();
        Assert.That(adapter.Released, Is.Empty);
        third.Dispose();
        Assert.That(adapter.Released, Is.EqualTo(new[] { 1 }));
        using (var raw = await assets.LoadRawFileAsync("raw"))
            Assert.That(raw.Get<string>(), Is.EqualTo("hello"));
        await host.StopAsync();
        Assert.That(adapter.Released, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(adapter.Disposals, Is.Zero);
        Assert.Throws<ObjectDisposedException>(() => assets.LoadAsset<Payload>("after-stop"));
    }

    [Test]
    public async Task InvalidLoadReleasesNativeHandleAndAllowsRetry()
    {
        var adapter = new AssetAdapter { InvalidAsset = true };
        var host = new PluginHost(new ResourcePlugin<int>(adapter));
        await host.StartAsync();
        var assets = host.Context.Get<IAssetManager>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await assets.LoadAssetAsync<Payload>("bad"));
        Assert.That(adapter.Released, Is.EqualTo(new[] { 1 }));
        adapter.InvalidAsset = false;
        var handle = await assets.LoadAssetAsync<Payload>("bad");
        await host.StopAsync();
        handle.Dispose();
        Assert.That(adapter.Released, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public async Task CancelingOneCallerDoesNotCancelOtherUsersOfTheSameLoad()
    {
        var adapter = new AssetAdapter { Block = true };
        var host = new PluginHost(new ResourcePlugin<int>(adapter));
        await host.StartAsync();
        var assets = host.Context.Get<IAssetManager>();
        using var cancellation = new CancellationTokenSource();
        var first = assets.LoadAssetAsync<Payload>("shared", cancellation.Token);
        await adapter.Entered.Task;
        var second = assets.LoadAssetAsync<Payload>("shared");
        cancellation.Cancel();
        adapter.Complete.SetResult(true);
        await Assert.CatchAsync<OperationCanceledException>(async () => await first);
        var handle = await second;
        Assert.That(adapter.Reads, Is.EqualTo(1));
        Assert.That(adapter.Released, Is.Empty);
        handle.Dispose();
        await host.StopAsync();
        Assert.That(adapter.Released, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public async Task ShutdownWaitsForPendingLoadAndReleasesLateResults()
    {
        var adapter = new AssetAdapter { Block = true, IgnoreCancellation = true };
        var host = new PluginHost(new ResourcePlugin<int>(adapter));
        await host.StartAsync();
        var load = host.Context.Get<IAssetManager>().LoadAssetAsync<Payload>("pending");
        await adapter.Entered.Task;
        var stop = host.StopAsync();
        Assert.That(stop.IsCompleted, Is.False);
        adapter.Complete.SetResult(true);
        await Assert.CatchAsync<Exception>(async () => await load);
        await stop;
        Assert.That(adapter.Released, Is.EqualTo(new[] { 1 }));
    }

    public sealed class Row : IConfig
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
    public sealed class Catalog : IConfigCatalog
    {
        public IReadOnlyList<ConfigEntry> Entries { get; } = new[] { new ConfigEntry(typeof(Row), "rows") };
    }

    [Test]
    public async Task ConfigLoadsBeforeBusinessAndReleasesRawHandles()
    {
        var adapter = new AssetAdapter();
        adapter.Files["rows"] = "[{\"Id\":7,\"Name\":\"seven\"}]";
        var host = new PluginHost(new ConfigPlugin(new Catalog()), new ResourcePlugin<int>(adapter));
        await host.StartAsync();
        var config = host.Context.Get<IConfigManager>();
        Assert.That(config.Get<Row>(7)!.Name, Is.EqualTo("seven"));
        Assert.That(config.Get<Row>().Count, Is.EqualTo(1));
        Assert.That(config.Get<Row>(8), Is.Null);
        Assert.That(adapter.Released, Is.EqualTo(new[] { 1 }));
        adapter.Files["rows"] = "invalid";
        await Assert.CatchAsync<JsonException>(async () => await config.LoadAllAsync(new Catalog()));
        Assert.That(config.Get<Row>(7)!.Name, Is.EqualTo("seven"));
        await host.StopAsync();
        Assert.Throws<ObjectDisposedException>(() => config.Get<Row>(7));
        Assert.That(adapter.Released, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public async Task ConfigFailureRollsBackAndMissingResourceFailsBeforeLoad()
    {
        var missing = new PluginHost(new ConfigPlugin(new Catalog()));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await missing.StartAsync());
        await missing.StopAsync();
        var adapter = new AssetAdapter();
        adapter.Files["rows"] = "null";
        var host = new PluginHost(new ResourcePlugin<int>(adapter), new ConfigPlugin(new Catalog()));
        await Assert.CatchAsync<JsonException>(async () => await host.StartAsync());
        Assert.That(adapter.Released, Is.EqualTo(new[] { 1 }));
        Assert.Throws<InvalidOperationException>(() => host.Context.Get<IConfigManager>());
        await host.StopAsync();
    }
}
