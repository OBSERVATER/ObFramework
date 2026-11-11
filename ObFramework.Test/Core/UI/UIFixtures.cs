using ObFramework.Core;
using ObFramework.Integrated.Resource.Handle;
using ObFramework.Integrated.UI;
using ObFramework.Test.Core.Plugins;

namespace ObFramework.Test.Core.UI;

/// <summary>Engine VC stand-in: identity and level only, as reduced by the UI 2.0 contract.</summary>
public class TestWindow : IUIWindow
{
    public TestWindow(EUILevel level = EUILevel.Common, string name = "window")
    {
        Level = level;
        Name = name;
    }

    public EUILevel Level { get; }
    public string Name { get; }
    public bool IsShown { get; private set; }
    internal void MarkShown(bool shown) => IsShown = shown;

    public IArchitecture GetArchitecture() => throw new NotSupportedException();
}

/// <summary>Engine-independent business View stand-in recording the managed lifecycle.</summary>
public class TestView : AUIView
{
    public readonly List<string> Calls = new();
    public readonly List<IUIData?> OpenData = new();
    public readonly List<IUIData> BoundData = new();

    public TestView()
    {
        Calls.Clear();
    }

    public IUIData? LastOpenData => OpenData.Count == 0 ? null : OpenData[^1];

    protected override void OnInit(IUIData? data) => Calls.Add("init");
    protected override void OnOpen(IUIData? data)
    {
        Calls.Add("open");
        OpenData.Add(data);
    }

    protected override void OnClose() => Calls.Add("close");
    protected override void OnBind(IUIData data)
    {
        Calls.Add("bind");
        BoundData.Add(data);
    }

    protected override void OnDestroy() => Calls.Add("destroy");

    public bool ViewEnabled;
    public bool ViewDisabled;
    public override void OnViewEnabled()
    {
        base.OnViewEnabled();
        ViewEnabled = true;
        Calls.Add("enabled");
    }

    public override void OnViewDisabled()
    {
        base.OnViewDisabled();
        ViewDisabled = true;
        Calls.Add("disabled");
    }

    public readonly List<string> CommandStateChanges = new();

    public void TrackCommandState() => CommandStateChanged += name => CommandStateChanges.Add(name);

    public int CountOf(string call) => Calls.Count(item => item == call);
}

public sealed class FirstView : TestView
{
}

public sealed class SecondView : TestView
{
}

public sealed class TestData : IUIData
{
    public TestData(string name = "data") => Name = name;
    public string Name { get; }
}

/// <summary>
/// Fake engine adapter: creates explicit VC/View pairs without reflection, tracks adapter calls and
/// simulates the binding owner's single View dispose on release.
/// </summary>
public sealed class TestUIAdapter : IUIAdapter, IDisposable
{
    private readonly Dictionary<Type, Func<IUIWindow, AUIView>> _factories = new();
    private readonly Dictionary<IUIWindow, AUIView> _views = new();
    private readonly Dictionary<AUIView, IUIWindow> _windows = new();
    private readonly Dictionary<IUIWindow, IHandle> _handles = new();

    public readonly List<(Type ViewType, IUIWindow Window, AUIView View)> Created = new();
    public readonly List<UIViewInstance> Released = new();
    public int CreatedCount;
    public int OpenedCount;
    public int ClosedCount;
    public int ReleasedCount;
    public int Disposals;
    public bool FailRelease;
    public bool Block;
    public int FailCreateTimes;
    public int FailOpenTimes;
    public EUILevel Level = EUILevel.Common;
    public ObFramework.Integrated.Resource.ResourcePlugin? Assets;

    public TaskCompletionSource<bool> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TestUIAdapter(ObFramework.Integrated.Resource.ResourcePlugin? assets = null) => Assets = assets;

    public TestUIAdapter Map<TView>(Func<IUIWindow, TView> factory) where TView : AUIView
    {
        _factories[typeof(TView)] = factory;
        return this;
    }

    public TestUIAdapter Map<TView>() where TView : AUIView, new()
    {
        _factories[typeof(TView)] = _ => new TView();
        return this;
    }

    public async Task<UIViewInstance?> CreateAsync(Type viewType, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        CreatedCount++;
        if (FailCreateTimes > 0)
        {
            FailCreateTimes--;
            throw new InvalidOperationException("fake create failure");
        }

        if (!_factories.TryGetValue(viewType, out var factory))
            throw new InvalidOperationException($"No factory registered for {viewType.Name}.");

        var window = new TestWindow(Level, viewType.Name);
        var view = factory(window);
        _views.Add(window, view);
        _windows.Add(view, window);
        Created.Add((viewType, window, view));
        if (Assets != null)
            _handles.Add(window, await Assets.LoadAssetAsync<ResourceConfigPluginTest.Payload>(viewType.Name));

        if (Block)
        {
            Entered.TrySetResult(true);
            await Complete.Task;
        }

        return new UIViewInstance(window, view);
    }

    public void Open(UIViewInstance instance, IUIData? data = null)
    {
        OpenedCount++;
        if (FailOpenTimes > 0)
        {
            FailOpenTimes--;
            throw new InvalidOperationException("fake open failure");
        }
        ((TestWindow)instance.Window).MarkShown(true);
    }

    public void Close(UIViewInstance instance)
    {
        ClosedCount++;
        ((TestWindow)instance.Window).MarkShown(false);
    }

    public void Release(UIViewInstance instance)
    {
        ReleasedCount++;
        Released.Add(instance);
        if (_views.Remove(instance.Window, out var view))
            _windows.Remove(view);
        if (_handles.Remove(instance.Window, out var handle))
            handle.Dispose();
        try
        {
            // The engine binding owner disposes the business View exactly once.
            instance.View.Dispose();
        }
        finally
        {
            if (FailRelease)
                throw new ApplicationException("release failed");
        }
    }

    public bool TryGetView(IUIWindow window, out AUIView view) => _views.TryGetValue(window, out view!);

    public void Dispose() => Disposals++;
}
