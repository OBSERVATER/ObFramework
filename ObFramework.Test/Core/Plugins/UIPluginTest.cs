using ObFramework.Core;
using ObFramework.Integrated.Resource;
using ObFramework.Integrated.Resource.Handle;
using ObFramework.Integrated.UI;
using static ObFramework.Test.Core.Plugins.ResourceConfigPluginTest;

namespace ObFramework.Test.Core.Plugins;

[TestFixture]
public class UIPluginTest
{
    public class Window : IUIWindow
    {
        public EUILevel Level => EUILevel.Common;
        public IUIData? Data;
        public IArchitecture GetArchitecture() => throw new NotSupportedException();
        public void Init(IUIData? data = null) { }
        public void Open(IUIData? data = null) => Data = data;
        public void Close() { }
    }
    public sealed class OtherWindow : Window { }
    public sealed class Data : IUIData { }
    public sealed class Adapter : IUIAdapter, IDisposable
    {
        public readonly ResourcePlugin? Assets;
        public int Created;
        public int Released;
        public int Disposals;
        public bool FailRelease;
        public bool Block;
        public TaskCompletionSource<bool> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<IUIWindow, IHandle> _handles = new();
        public Adapter(ResourcePlugin? assets = null) => Assets = assets;
        public async Task<T?> CreateAsync<T>(CancellationToken ct = default) where T : class, IUIWindow => (T)await CreateAsync(typeof(T), ct);
        public async Task<IUIWindow> CreateAsync(Type type, CancellationToken ct = default)
        {
            var window = (IUIWindow)Activator.CreateInstance(type)!;
            Created++;
            if (Assets != null)
                _handles.Add(window, await Assets.LoadAssetAsync<Payload>(type.Name));
            if (Block)
            {
                Entered.TrySetResult(true);
                await Complete.Task;
            }
            window.Init();
            return window;
        }
        public void Open(IUIWindow instance, IUIData? data = null) => instance.Open(data);
        public void Close(IUIWindow instance) => instance.Close();
        public void Release(IUIWindow instance)
        {
            Released++;
            if (_handles.Remove(instance, out var handle))
                handle.Dispose();
            if (FailRelease)
                throw new ApplicationException("release failed");
        }
        public void Dispose() => Disposals++;
    }

    [Test]
    public async Task SingleMultipleStackAndCacheKeepTheirExistingBehavior()
    {
        var adapter = new Adapter();
        var host = new PluginHost(new UIPlugin(adapter, 1));
        await host.StartAsync();
        var ui = host.Context.Get<UIPlugin>();
        var first = await ui.OpenAsync<Window>();
        var data = new Data();
        Assert.That(await ui.OpenAsync<Window>(data: data), Is.SameAs(first));
        Assert.That(first!.Data, Is.SameAs(data));
        ui.Push(first);
        Assert.That(await ui.Pop<Window>(), Is.SameAs(first));
        ui.Close(first);
        Assert.That(await ui.OpenAsync<Window>(EUIOpenMode.Multiple), Is.SameAs(first));
        var second = await ui.OpenAsync<Window>(EUIOpenMode.Multiple);
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(ui.CloseAll<Window>(), Is.EqualTo(2));
        Assert.That(adapter.Released, Is.EqualTo(1));
        await host.StopAsync();
        Assert.That(adapter.Released, Is.EqualTo(2));
        Assert.That(adapter.Disposals, Is.Zero);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await ui.OpenAsync<Window>());
    }

    [Test]
    public async Task UIUsesTheSharedResourcePluginAndStopsWhileResourcesAreAvailable()
    {
        var assetsAdapter = new AssetAdapter();
        Adapter? adapter = null;
        var rootStopped = false;
        var plugin = new UIPlugin(assets => adapter = new Adapter(assets), shutdown: ui =>
        {
            using var handle = ((Adapter)ui).Assets!.LoadAsset<Payload>("root-stop");
            rootStopped = true;
            return Task.CompletedTask;
        });
        var host = new PluginHost(plugin, ResourcePlugin.Create(assetsAdapter));
        await host.StartAsync();
        Assert.That(adapter!.Assets, Is.SameAs(host.Context.Get<ResourcePlugin>()));
        var ui = host.Context.Get<UIPlugin>();
        var cached = await ui.OpenAsync<Window>();
        ui.Close(cached!);
        await ui.OpenAsync<OtherWindow>();
        await host.StopAsync();
        Assert.That(adapter.Released, Is.EqualTo(2));
        Assert.That(adapter.Disposals, Is.EqualTo(1));
        Assert.That(assetsAdapter.Released, Is.EquivalentTo(new[] { 1, 2, 3 }));
        Assert.That(rootStopped, Is.True);
        Assert.That(assetsAdapter.Disposals, Is.Zero);
    }

    [Test]
    public async Task UIShutdownContinuesAfterReleaseErrors()
    {
        var adapter = new Adapter { FailRelease = true };
        var rootStopped = false;
        var host = new PluginHost(new UIPlugin(adapter, shutdown: _ => { rootStopped = true; return Task.CompletedTask; }));
        await host.StartAsync();
        var ui = host.Context.Get<UIPlugin>();
        var cached = await ui.OpenAsync<Window>();
        ui.Close(cached!);
        await ui.OpenAsync<OtherWindow>();
        var error = await Assert.ThrowsAsync<AggregateException>(async () => await host.StopAsync());
        Assert.That(error!.Flatten().InnerExceptions.Count, Is.EqualTo(2));
        Assert.That(adapter.Released, Is.EqualTo(2));
        Assert.That(rootStopped, Is.True);
    }

    [Test]
    public async Task ShutdownWaitsForPendingWindowAndReleasesItsLateResult()
    {
        var adapter = new Adapter { Block = true };
        var host = new PluginHost(new UIPlugin(adapter));
        await host.StartAsync();
        var open = host.Context.Get<UIPlugin>().OpenAsync<Window>();
        await adapter.Entered.Task;
        var stop = host.StopAsync();
        Assert.That(stop.IsCompleted, Is.False);
        adapter.Complete.SetResult(true);
        await Assert.CatchAsync<OperationCanceledException>(async () => await open);
        await stop;
        Assert.That(adapter.Released, Is.EqualTo(1));
    }
}
