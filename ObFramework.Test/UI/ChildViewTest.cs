using ObFramework.Core;
using ObFramework.Integrated.Resource.Handle;
using ObFramework.Integrated.UI;
using ObFramework.Test.Core.UI;

namespace ObFramework.Test.UI;

// T12 — reusable child Views re-bound through AUIView.Bind(IUIData).
//
// These are PURE .NET simulations of the runtime contract (no UnityEngine reference):
//   * re-binding the same child View instance never re-runs OnInit/OnOpen and never creates a new View;
//   * display updates flow through the generated ObservableProperty notification channel
//     (simulated by a View-authored property raising PropertyChanged) and idempotent refreshes do not
//     leave duplicate subscriptions behind;
//   * the parent binding owner releases its child Views exactly once, together with the resource handle,
//     without introducing a UIChildManager;
//   * re-binding does not cancel a running async business command.
//
// Engine-verified vs deferred:
//   * VERIFIED HERE: AUIView lifecycle/notification/command runtime semantics and single-owner dispose.
//   * DEFERRED TO THE UNITY SAMPLE PROJECT: real prefab child VCs, Unity OnEnable/OnDisable ordering,
//     Addressables handle lifetime, generated [UIBindData] subscribe/unsubscribe code and the parent VC
//     destroying child GameObjects. The runtime-level rules proven below are exactly what that generated
//     code consumes.

[TestFixture]
public class ChildViewTest
{
    public sealed class ChildData : IUIData
    {
        public ChildData(string title) => Title = title;
        public string Title { get; }
    }

    /// <summary>
    /// Reusable child View: the parent can call <see cref="AUIView.Bind"/> repeatedly. Title mimics a
    /// generated [ObservableProperty] property: it raises the notification channel only on change.
    /// </summary>
    public sealed class BindableChildView : TestView
    {
        private string _title = string.Empty;

        public string Title
        {
            get => _title;
            private set
            {
                if (_title == value)
                    return;
                _title = value;
                NotifyPropertyChanged(nameof(Title));
            }
        }

        protected override void OnBind(IUIData data)
        {
            base.OnBind(data);
            Title = ((ChildData)data).Title;
        }

        // Stand-in for a generated [RelayCommand] async method. The business identity captured at task
        // start (here: nothing, in real code an item id) is what keeps a late completion valid across
        // re-binds; Bind never cancels it.
        public readonly TaskCompletionSource<bool> Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Loads;
        public int Cancelled;

        public sealed class LoadCommand : AUIAsyncCommand
        {
            private readonly BindableChildView _owner;

            public LoadCommand(BindableChildView view) : base(view, nameof(LoadCommand)) => _owner = view;

            protected override async ValueTask ExecuteCore(CancellationToken ct)
            {
                _owner.Started.TrySetResult(true);
                try
                {
                    await _owner.Gate.Task.WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    _owner.Cancelled++;
                    throw;
                }

                _owner.Loads++;
            }
        }
    }

    private static async Task<PluginHost> StartAsync(TestUIAdapter adapter)
    {
        var host = new PluginHost(new UIPlugin(adapter, 2));
        await host.StartAsync();
        return host;
    }

    private static async Task<BindableChildView> OpenChildAsync(PluginHost host)
    {
        var ui = host.Context.Get<UIPlugin>();
        var view = await ui.OpenAsync<BindableChildView>();
        Assert.That(view, Is.Not.Null);
        return view!;
    }

    [Test]
    public async Task ReBindUpdatesTheSameChildInstanceWithoutReRunningItsLifecycle()
    {
        var adapter = new TestUIAdapter().Map<BindableChildView>();
        var host = await StartAsync(adapter);
        try
        {
            var ui = host.Context.Get<UIPlugin>();
            var openData = new ChildData("open");
            var child = await ui.OpenAsync<BindableChildView>(data: openData);
            Assert.That(child, Is.Not.Null);
            Assert.That(child!.IsInitialized, Is.True);
            Assert.That(child.CountOf("init"), Is.EqualTo(1));
            Assert.That(child.CountOf("open"), Is.EqualTo(1));

            // The parent now re-binds the existing child twice with different data.
            var second = new ChildData("second");
            var third = new ChildData("third");
            child.Bind(second);
            child.Bind(third);

            Assert.That(child.IsInitialized, Is.True);
            Assert.That(child.CountOf("init"), Is.EqualTo(1), "Bind must not re-run OnInit");
            Assert.That(child.CountOf("open"), Is.EqualTo(1), "Bind must not run the managed OnOpen lifecycle");
            Assert.That(child.CountOf("close"), Is.Zero, "Bind is not a close/reopen cycle");
            Assert.That(child.CountOf("bind"), Is.EqualTo(2));
            Assert.That(child.BoundData, Is.EqualTo(new[] { second, third }));
            Assert.That(child.Title, Is.EqualTo("third"), "the latest data drives the display state");

            // No new View/VC pair was created and the managed open data is untouched by Bind.
            Assert.That(adapter.CreatedCount, Is.EqualTo(1));
            Assert.That(ui.TryGet<BindableChildView>(out var tracked), Is.True);
            Assert.That(tracked, Is.SameAs(child));
            Assert.That(child.Data, Is.SameAs(openData), "Bind updates child data, not the managed window data");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task ObservableChangesDriveTheBoundDisplayAndRefreshIsIdempotent()
    {
        var adapter = new TestUIAdapter().Map<BindableChildView>();
        var host = await StartAsync(adapter);
        try
        {
            var child = await OpenChildAsync(host);
            var display = new SimulatedDisplayBinding(child);
            display.RefreshBindings();

            Assert.That(display.Subscriptions, Is.EqualTo(1));
            Assert.That(display.Text, Is.Empty, "initial refresh reads the current View state");

            child.Bind(new ChildData("one"));
            Assert.That(display.Text, Is.EqualTo("one"));
            Assert.That(display.Changes, Is.EqualTo(1));

            child.Bind(new ChildData("two"));
            Assert.That(display.Text, Is.EqualTo("two"));
            Assert.That(display.Changes, Is.EqualTo(2));

            // Generated RefreshBindings runs on every display cycle (OnEnable and after adapter Open);
            // it must not stack duplicate property subscriptions.
            display.RefreshBindings();
            display.RefreshBindings();
            Assert.That(display.Subscriptions, Is.EqualTo(1));

            child.Bind(new ChildData("three"));
            Assert.That(display.Text, Is.EqualTo("three"));
            Assert.That(display.Changes, Is.EqualTo(3), "a duplicate subscription would apply the change more than once");

            // Replacing the display: the old subscription is removed, so no stale display keeps updating.
            var replacement = new SimulatedDisplayBinding(child);
            replacement.RefreshBindings();
            display.Dispose();
            child.Bind(new ChildData("four"));

            Assert.That(replacement.Text, Is.EqualTo("four"));
            Assert.That(display.Text, Is.EqualTo("three"), "a disposed binding must not receive notifications");
            Assert.That(display.Subscriptions, Is.Zero);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task ReBindDoesNotCancelARunningBusinessTask()
    {
        var adapter = new TestUIAdapter().Map<BindableChildView>();
        var host = await StartAsync(adapter);
        try
        {
            var child = await OpenChildAsync(host);
            var run = new BindableChildView.LoadCommand(child).Execute(CancellationToken.None).AsTask();
            await child.Started.Task;
            Assert.That(child.IsCommandRunning(nameof(BindableChildView.LoadCommand)), Is.True);

            // Re-binding the child while the async business task is in flight must not cancel it.
            child.Bind(new ChildData("other"));
            Assert.That(child.Cancelled, Is.Zero);
            Assert.That(child.IsCommandRunning(nameof(BindableChildView.LoadCommand)), Is.True);
            Assert.That(child.CountOf("bind"), Is.EqualTo(1));

            child.Gate.TrySetResult(true);
            await run;
            Assert.That(child.Cancelled, Is.Zero);
            Assert.That(child.Loads, Is.EqualTo(1), "the task started before the re-bind must complete normally");
            Assert.That(child.IsCommandRunning(nameof(BindableChildView.LoadCommand)), Is.False);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public void ParentOwnedReleaseDisposesChildViewAndResourceHandleExactlyOnce()
    {
        // Models the generated parent VC binding owner: it creates the child View, owns its prefab
        // resource handle and releases both when the parent native object is destroyed.
        var child = new BindableChildView();
        var handle = new FakeAssetHandle("UI/Child");
        var owner = new ChildBindingOwner(child, handle);
        child.Bind(new ChildData("data"));

        owner.Dispose();
        owner.Dispose();

        Assert.That(child.CountOf("destroy"), Is.EqualTo(1), "AUIView.Dispose (OnDestroy) is single-ownership");
        Assert.That(handle.Disposals, Is.EqualTo(1), "the engine binding owner disposes the resource handle once");
        Assert.That(owner.DisposeCalls, Is.EqualTo(2), "the second release is a no-op, not a second dispose");
        Assert.That(ThrowsOnBind(child), Is.True, "a released child View is never re-bound");
    }

    [Test]
    public void NoDedicatedChildManagerTypeIsIntroduced()
    {
        // T12 explicitly avoids a UIChildManager: ownership belongs to the parent VC binding and the
        // adapter, so only the engine-independent View/VC contract types may exist.
        var childManagerTypes = typeof(AUIView).Assembly
            .GetTypes()
            .Where(type => type.Name.Contains("ChildManager", StringComparison.Ordinal))
            .ToArray();
        Assert.That(childManagerTypes, Is.Empty);
    }

    [Test]
    public void BindingLayerInitializesAChildViewExactlyOnceBeforeItsFirstBind()
    {
        // Unity ViewBinding constructs child Views directly (no UIManager entry), so the binding
        // layer must run OnInit once at creation; repeated RefreshBindings must not run it again.
        var child = new BindableChildView();
        Assert.That(child.IsInitialized, Is.False);

        child.InitView(null);
        Assert.That(child.IsInitialized, Is.True);
        Assert.That(child.CountOf("init"), Is.EqualTo(1));

        child.InitView(null);
        Assert.That(child.CountOf("init"), Is.EqualTo(1), "binding refresh must not re-run OnInit");

        child.Bind(new ChildData("after-init"));
        Assert.That(child.CountOf("init"), Is.EqualTo(1));
        Assert.That(child.CountOf("bind"), Is.EqualTo(1));
        Assert.That(child.Title, Is.EqualTo("after-init"));
    }

    private static bool ThrowsOnBind(AUIView view)
    {
        try
        {
            view.Bind(new ChildData("late"));
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    /// <summary>Simulates the generated binding for one display control ([UIBindData] OneWay).</summary>
    private sealed class SimulatedDisplayBinding : IDisposable
    {
        private readonly BindableChildView _view;

        public SimulatedDisplayBinding(BindableChildView view) => _view = view;

        /// <summary>Number of live PropertyChanged subscriptions held by this display binding.</summary>
        public int Subscriptions { get; private set; }
        public int Changes { get; private set; }
        public string Text { get; private set; } = string.Empty;
        private bool _subscribed;

        public void RefreshBindings()
        {
            if (!_subscribed)
            {
                _view.PropertyChanged += OnPropertyChanged;
                _subscribed = true;
                Subscriptions++;
            }

            Text = _view.Title;
        }

        private void OnPropertyChanged(string propertyName)
        {
            if (propertyName != nameof(BindableChildView.Title))
                return;
            Text = _view.Title;
            Changes++;
        }

        public void Dispose()
        {
            if (!_subscribed)
                return;
            _view.PropertyChanged -= OnPropertyChanged;
            _subscribed = false;
            Subscriptions--;
        }
    }

    /// <summary>Models the parent-owned engine resource handle; the adapter releases it on destroy.</summary>
    private sealed class FakeAssetHandle : IHandle
    {
        public FakeAssetHandle(string location) => Location = location;

        public string Location { get; }
        public int Disposals { get; private set; }

        public T? Get<T>() where T : class => null;

        public void Dispose() => Disposals++;
    }

    /// <summary>Models the generated parent VC binding: it owns the child View and the resource handle.</summary>
    private sealed class ChildBindingOwner : IDisposable
    {
        private readonly AUIView _child;
        private readonly IHandle _handle;
        private bool _disposed;

        public ChildBindingOwner(AUIView child, IHandle handle)
        {
            _child = child;
            _handle = handle;
        }

        public int DisposeCalls { get; private set; }

        public void Dispose()
        {
            DisposeCalls++;
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                _child.Dispose();
            }
            finally
            {
                _handle.Dispose();
            }
        }
    }
}
