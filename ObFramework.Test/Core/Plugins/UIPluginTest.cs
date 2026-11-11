using ObFramework.Core;
using ObFramework.Integrated.Resource;
using ObFramework.Integrated.UI;
using ObFramework.Test.Core.Plugins;
using ObFramework.Test.Core.UI;

namespace ObFramework.Test.Core.Plugins;

[TestFixture]
public class UIPluginTest
{
    public sealed class Window : IUIWindow
    {
        public EUILevel Level => EUILevel.Common;
        public IUIData? Data;
        public IArchitecture GetArchitecture() => throw new NotSupportedException();
    }

    [Test]
    public async Task SingleMultipleStackAndCacheKeepTheirExistingBehavior()
    {
        var adapter = new TestUIAdapter().Map<FirstView>();
        var host = new PluginHost(new UIPlugin(adapter, 1));
        await host.StartAsync();
        var ui = host.Context.Get<UIPlugin>();

        var first = await ui.OpenAsync<FirstView>();
        Assert.That(first, Is.Not.Null);
        var reopenData = new TestData("reopen");
        var reopened = await ui.OpenAsync<FirstView>(data: reopenData);
        Assert.That(reopened, Is.SameAs(first));
        Assert.That(first!.LastOpenData, Is.SameAs(reopenData));
        Assert.That(first.CountOf("init"), Is.EqualTo(1));
        Assert.That(first.CountOf("open"), Is.EqualTo(2));

        ui.Push(first);
        Assert.That(await ui.Pop<FirstView>(), Is.SameAs(first));
        Assert.That(first.CountOf("init"), Is.EqualTo(1));

        ui.Close(first);
        Assert.That(await ui.OpenAsync<FirstView>(EUIOpenMode.Multiple), Is.SameAs(first));

        var second = await ui.OpenAsync<FirstView>(EUIOpenMode.Multiple);
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second!.CountOf("init"), Is.EqualTo(1));

        Assert.That(ui.CloseAll<FirstView>(), Is.EqualTo(2));
        Assert.That(adapter.ReleasedCount, Is.EqualTo(1));
        await host.StopAsync();
        Assert.That(adapter.ReleasedCount, Is.EqualTo(2));
        Assert.That(adapter.Disposals, Is.Zero);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await ui.OpenAsync<FirstView>());
    }

    [Test]
    public async Task UIUsesTheSharedResourcePluginAndStopsWhileResourcesAreAvailable()
    {
        var assetsAdapter = new ResourceConfigPluginTest.AssetAdapter();
        TestUIAdapter? adapter = null;
        var rootStopped = false;
        var plugin = new UIPlugin(assets =>
        {
            adapter = (TestUIAdapter)new TestUIAdapter(assets).Map<FirstView>().Map<SecondView>();
            return adapter;
        }, shutdown: ui =>
        {
            using var handle = ((TestUIAdapter)ui).Assets!.LoadAsset<ResourceConfigPluginTest.Payload>("root-stop");
            rootStopped = true;
            return Task.CompletedTask;
        });
        var host = new PluginHost(plugin, ResourcePlugin.Create(assetsAdapter));
        await host.StartAsync();
        Assert.That(adapter!.Assets, Is.SameAs(host.Context.Get<ResourcePlugin>()));
        var ui = host.Context.Get<UIPlugin>();

        var cached = await ui.OpenAsync<FirstView>();
        ui.Close(cached!);
        await ui.OpenAsync<SecondView>();
        await host.StopAsync();
        Assert.That(adapter.ReleasedCount, Is.EqualTo(2));
        Assert.That(adapter.Disposals, Is.EqualTo(1));
        Assert.That(assetsAdapter.Released, Is.EquivalentTo(new[] { 1, 2, 3 }));
        Assert.That(rootStopped, Is.True);
        Assert.That(assetsAdapter.Disposals, Is.Zero);
    }

    [Test]
    public async Task UIShutdownContinuesAfterReleaseErrors()
    {
        var adapter = new TestUIAdapter { FailRelease = true }.Map<FirstView>().Map<SecondView>();
        var rootStopped = false;
        var host = new PluginHost(new UIPlugin(adapter, shutdown: _ =>
        {
            rootStopped = true;
            return Task.CompletedTask;
        }));
        await host.StartAsync();
        var ui = host.Context.Get<UIPlugin>();
        var cached = await ui.OpenAsync<FirstView>();
        ui.Close(cached!);
        await ui.OpenAsync<SecondView>();
        var error = await Assert.ThrowsAsync<AggregateException>(async () => await host.StopAsync());
        Assert.That(error!.Flatten().InnerExceptions.Count, Is.EqualTo(2));
        Assert.That(adapter.ReleasedCount, Is.EqualTo(2));
        Assert.That(rootStopped, Is.True);
    }

    [Test]
    public async Task ShutdownWaitsForPendingWindowAndReleasesItsLateResult()
    {
        var adapter = new TestUIAdapter { Block = true }.Map<FirstView>();
        var host = new PluginHost(new UIPlugin(adapter));
        await host.StartAsync();
        var open = host.Context.Get<UIPlugin>().OpenAsync<FirstView>();
        await adapter.Entered.Task;
        var stop = host.StopAsync();
        Assert.That(stop.IsCompleted, Is.False);
        adapter.Complete.SetResult(true);
        await Assert.CatchAsync<OperationCanceledException>(async () => await open);
        await stop;
        Assert.That(adapter.ReleasedCount, Is.EqualTo(1));
        Assert.That(((TestView)adapter.Released[0].View).CountOf("destroy"), Is.EqualTo(1));
    }
}
