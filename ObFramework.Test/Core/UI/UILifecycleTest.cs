using ObFramework.Core;
using ObFramework.Integrated.UI;
using ObFramework.Test.Core.UI;

namespace ObFramework.Test.Core.UI;

[TestFixture]
public class UILifecycleTest
{
    private static async Task<PluginHost> StartAsync(TestUIAdapter adapter, int capacity = 1)
    {
        var host = new PluginHost(new UIPlugin(adapter, capacity));
        await host.StartAsync();
        return host;
    }

    [Test]
    public async Task OnInitRunsOnceAndOnOpenRunsOnEveryOpenIncludingCacheReuse()
    {
        var adapter = new TestUIAdapter().Map<FirstView>().Map<SecondView>();
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var firstData = new TestData("first");
            var view = await ui.OpenAsync<FirstView>(data: firstData);
            Assert.That(view!.CountOf("init"), Is.EqualTo(1));
            Assert.That(view.CountOf("open"), Is.EqualTo(1));
            Assert.That(view.LastOpenData, Is.SameAs(firstData));

            var secondData = new TestData("second");
            var reopened = await ui.OpenAsync<FirstView>(EUIOpenMode.Single, secondData);
            Assert.That(reopened, Is.SameAs(view));
            Assert.That(view.CountOf("init"), Is.EqualTo(1));
            Assert.That(view.CountOf("open"), Is.EqualTo(2));
            Assert.That(view.LastOpenData, Is.SameAs(secondData));

            Assert.That(ui.Close(view), Is.True);
            Assert.That(view.CountOf("close"), Is.EqualTo(1));

            var cached = await ui.OpenAsync<FirstView>(EUIOpenMode.Multiple);
            Assert.That(cached, Is.SameAs(view));
            Assert.That(view.CountOf("init"), Is.EqualTo(1));
            Assert.That(view.CountOf("open"), Is.EqualTo(3));

            var thirdData = new TestData("third");
            await ui.OpenAsync<SecondView>(data: thirdData);
            // The earlier closed FirstView is still cached until another cache entry needs space.
            Assert.That(view.CountOf("destroy"), Is.Zero);
            await host.StopAsync();
            Assert.That(adapter.ReleasedCount, Is.EqualTo(2));
            Assert.That(view.CountOf("destroy"), Is.EqualTo(1));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task CloseIsManagedLifecycleAndNativeActivationIsNotDrivenByTheManager()
    {
        var adapter = new TestUIAdapter().Map<FirstView>();
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var view = await ui.OpenAsync<FirstView>();
            ui.Close(view);
            Assert.That(view!.CountOf("enabled"), Is.Zero);
            Assert.That(view.CountOf("disabled"), Is.Zero);
            Assert.That(view.CountOf("close"), Is.EqualTo(1));
            Assert.That(ui.TryGet<FirstView>(out _), Is.False);
            Assert.That(adapter.OpenedCount, Is.EqualTo(1));
            Assert.That(adapter.ClosedCount, Is.EqualTo(1));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task OnDestroyRunsOnlyWhenTheBindingOwnerReleasesTheView()
    {
        var adapter = new TestUIAdapter().Map<FirstView>().Map<SecondView>();
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var view = await ui.OpenAsync<FirstView>();
            ui.Close(view);
            ui.Close(view);
            Assert.That(view!.CountOf("destroy"), Is.Zero);
            Assert.That(view.CountOf("close"), Is.EqualTo(1));

            var second = await ui.OpenAsync<SecondView>();
            ui.Close(second);
            // Capacity 1: caching SecondView evicted and released the cached FirstView pair.
            Assert.That(view.CountOf("destroy"), Is.EqualTo(1));
            view.Dispose();
            Assert.That(view.CountOf("destroy"), Is.EqualTo(1));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task FailedOpenReleasesThePartialPairAndNeverCachesIt()
    {
        var adapter = new TestUIAdapter().Map<FirstView>();
        adapter.FailOpenTimes = 1;
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await ui.OpenAsync<FirstView>());
            Assert.That(error!.Message, Does.Contain("fake open failure"));
            Assert.That(adapter.ReleasedCount, Is.EqualTo(1));
            Assert.That(((TestView)adapter.Released[0].View).CountOf("destroy"), Is.EqualTo(1));
            Assert.That(ui.TryGet<FirstView>(out _), Is.False);

            var view = await ui.OpenAsync<FirstView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(adapter.CreatedCount, Is.EqualTo(2));
            Assert.That(view!.CountOf("init"), Is.EqualTo(1));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task FailedCreateLeavesNoCacheEntryAndCanBeRetried()
    {
        var adapter = new TestUIAdapter().Map<FirstView>();
        adapter.FailCreateTimes = 1;
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await ui.OpenAsync<FirstView>());
            Assert.That(ui.TryGet<FirstView>(out _), Is.False);
            var view = await ui.OpenAsync<FirstView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view!.CountOf("init"), Is.EqualTo(1));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task NonCommonWindowsCannotBePushedOntoTheStack()
    {
        var adapter = new TestUIAdapter().Map<FirstView>();
        adapter.Level = EUILevel.Overlay;
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var overlay = await ui.OpenAsync<FirstView>();
            Assert.Throws<InvalidOperationException>(() => ui.Push(overlay!));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task StackReconstructsEvictedWindowsFromTypeModeAndData()
    {
        var adapter = new TestUIAdapter().Map<FirstView>().Map<SecondView>();
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var data = new TestData("stack");
            var first = await ui.OpenAsync<FirstView>(data: data);
            ui.Push(first!);
            Assert.That(ui.TryGet<FirstView>(out _), Is.False);

            var second = await ui.OpenAsync<SecondView>();
            Assert.That(second, Is.Not.Null);
            ui.Close(second);
            // Caching SecondView evicted and released the pushed FirstView pair.
            Assert.That(first!.CountOf("destroy"), Is.EqualTo(1));

            var popped = await ui.Pop<FirstView>();
            Assert.That(popped, Is.Not.Null);
            Assert.That(popped, Is.Not.SameAs(first));
            Assert.That(popped!.LastOpenData, Is.SameAs(data));
            Assert.That(popped.CountOf("init"), Is.EqualTo(1));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task ShutdownClosesActiveViewsAndReleasesThemThroughTheAdapter()
    {
        var adapter = new TestUIAdapter().Map<FirstView>();
        var host = await StartAsync(adapter);
        var ui = host.Context.Get<UIPlugin>();
        var view = await ui.OpenAsync<FirstView>();
        await host.StopAsync();
        Assert.That(view!.CountOf("close"), Is.EqualTo(1));
        Assert.That(view.CountOf("destroy"), Is.EqualTo(1));
        Assert.That(adapter.ReleasedCount, Is.EqualTo(1));
    }
}
