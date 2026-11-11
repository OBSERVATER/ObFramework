using ObFramework.Core;
using ObFramework.Integrated.UI;
using UnrealSharp;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using UnrealSharp.UMG;

namespace ObFramework.Adapter.UnrealSharp.UI;

/// <summary>
/// UnrealSharp implementation of the final <see cref="IUIAdapter"/> pair contract.
/// <para>
/// A window is a UMG widget class loaded asynchronously through the Unreal context
/// (<c>UObject.AsyncStaticLoadClass</c>) from its registration resource key, conventionally
/// <c>/Game/UI/{ViewName}.{ViewName}_C</c>. It is created with <c>UObject.CreateWidget</c> on the
/// owning <see cref="APlayerController"/>, kept alive with a <see cref="TStrongObjectPtr{T}"/> and
/// added to the Scene/Common/Overlay canvas hierarchy provided by <see cref="UIRoot"/>. Each widget
/// is paired with one business <see cref="AUIView"/> created by the explicit registration factory.
/// UIManager drives the business lifecycle; this adapter only creates, adds, hides and destroys
/// native widgets.
/// </para>
/// <para>
/// Architecture resolution: the factory receives <see cref="UIViewRegistration.Factory"/>'s
/// <see cref="IArchitecture"/>. It is either the architecture supplied to the constructor (or
/// <see cref="Architecture"/>) or, when none was supplied, the architecture reported by the created
/// engine widget through <see cref="IUIWindow.GetArchitecture"/>. No engine type leaks into the
/// Framework and no assembly scanning or setup.json read happens here.
/// </para>
/// <para>
/// Lifecycle ownership in this phase: there is no generated UnrealSharp ViewBinding yet, so the
/// adapter is the binding owner. It attaches the architecture to the View
/// (<see cref="ICanSetArchitecture.SetArchitecture"/>) and disposes the View exactly once in
/// <see cref="Release"/>; <see cref="UIManager"/> must not dispose it independently.
/// </para>
/// <para>
/// Call <see cref="InitAsync"/> once before opening UI (for example from the
/// <see cref="UIPlugin"/> initialization callback) and <see cref="Deinit"/> on shutdown.
/// </para>
/// </summary>
public class UnrealUIAdapter : IUIAdapter
{
    private const string RootClassPath = "/Game/UI/WBP_ObUIRoot.WBP_ObUIRoot_C";

    private readonly APlayerController _owningController;
    private readonly Dictionary<IUIWindow, WindowState> _windows = new();
    private TStrongObjectPtr<UObject>? _rootHandle;

    /// <summary>UIRoot that provides the Scene/Common/Popup canvases. Available after <see cref="InitAsync"/>.</summary>
    public UIRoot? Root { get; private set; }

    /// <summary>
    /// Architecture passed to registration factories. When null (default) the architecture is read
    /// from the created engine widget's <see cref="IUIWindow.GetArchitecture"/>.
    /// </summary>
    public IArchitecture? Architecture { get; set; }

    /// <summary>
    /// Adapter-owned explicit registration map. Hand-written startup code and future generated
    /// <c>RegisterAll</c> code register through <see cref="Register{TView,TWindow}"/> or directly
    /// through this registry. Registration is never inferred by scanning assemblies.
    /// </summary>
    public UIViewRegistry Registry { get; } = new();

    /// <param name="owningController">Player controller used to create and own the widgets.</param>
    /// <param name="architecture">
    /// Optional architecture forwarded to View factories. When omitted it is resolved from the created
    /// widget, which keeps the existing PlayerController-only adapter construction working
    /// (PluginApplication creates the architecture after plugin init).
    /// </param>
    public UnrealUIAdapter(APlayerController owningController, IArchitecture? architecture = null)
    {
        _owningController = owningController ?? throw new ArgumentNullException(nameof(owningController));
        Architecture = architecture;
    }

    #region Registration

    /// <summary>
    /// Hand-written registration of one business View. The factory is an explicit delegate, never
    /// reflection, and receives the resolved architecture. Example:
    /// <code>
    /// ui.Register&lt;ShopView, ShopWindow&gt;("/Game/UI/ShopView.ShopView_C", architecture => new ShopView(architecture));
    /// </code>
    /// Duplicate View types are rejected by <see cref="UIViewRegistry"/>.
    /// </summary>
    public void Register<TView, TWindow>(string resourceKey, Func<IArchitecture, TView> factory)
        where TView : AUIView
        where TWindow : class, IUIWindow
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        Registry.Register<TView, TWindow>(resourceKey, factory);
    }

    /// <summary>
    /// Non-generic registration overload matching <see cref="UIViewRegistry.Register(Type, Type, string, Func{IArchitecture, AUIView})"/>.
    /// A factory is required because UnrealSharp has no generated ViewBinding that could construct the View.
    /// </summary>
    public void Register(Type viewType, Type windowType, string resourceKey, Func<IArchitecture, AUIView> factory)
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        Registry.Register(viewType, windowType, resourceKey, factory);
    }

    #endregion

    #region Lifecycle

    /// <summary>Creates the engine UIRoot widget, adds it to the owning player screen and caches its handle.</summary>
    public async Task InitAsync(CancellationToken ct = default)
    {
        if (Root != null)
            return;

        ct.ThrowIfCancellationRequested();

        if (_owningController.IsDestroyed)
            throw new InvalidOperationException("Owning PlayerController is destroyed.");

        var widgetClass = await UObject.AsyncStaticLoadClass<UUserWidget>(RootClassPath)
            .ConfigureWithUnrealContext();

        ct.ThrowIfCancellationRequested();

        if (_owningController.IsDestroyed)
            throw new InvalidOperationException("Owning PlayerController is destroyed.");

        var widget = UObject.CreateWidget(widgetClass, _owningController);

        if (widget == null || widget.IsDestroyed)
            throw new InvalidOperationException($"Failed to create UIRoot: {RootClassPath}");

        var handle = new TStrongObjectPtr<UObject>(widget);

        try
        {
            var root = new UIRoot(widget);

            ct.ThrowIfCancellationRequested();

            if (!widget.AddToPlayerScreen(0))
                throw new InvalidOperationException("Failed to add UIRoot to player screen.");
            Root = root;
            _rootHandle = handle;
        }
        catch
        {
            try
            {
                if (!widget.IsDestroyed)
                    widget.RemoveFromParent();
            }
            finally
            {
                handle.Dispose();
            }

            throw;
        }
    }

    /// <summary>Releases every live pair and then the engine UIRoot widget.</summary>
    public void Deinit()
    {
        var errors = new List<Exception>();

        foreach (var window in new List<IUIWindow>(_windows.Keys))
        {
            if (!_windows.TryGetValue(window, out var state))
                continue;

            try
            {
                Release(new UIViewInstance(window, state.View));
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        try
        {
            if (Root is { Widget.IsDestroyed: false } root)
                root.Widget.RemoveFromParent();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        finally
        {
            _rootHandle?.Dispose();
            _rootHandle = null;
            Root = null;
        }

        if (errors.Count > 0)
            throw new AggregateException("Unreal UI adapter shutdown failed.", errors);
    }

    #endregion

    #region IUIAdapter

    /// <summary>
    /// Loads the registered widget class asynchronously, creates the widget on the owning controller,
    /// creates the paired business View through the registration factory and returns the inactive pair.
    /// No business lifecycle method is invoked.
    /// </summary>
    public async Task<UIViewInstance?> CreateAsync(Type viewType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(viewType);

        if (!Registry.TryGet(viewType, out var registration))
        {
            throw new InvalidOperationException(
                $"UI view '{viewType.FullName}' is not registered. Call {nameof(Register)} during application startup.");
        }

        if (!typeof(UUserWidget).IsAssignableFrom(registration.WindowType) ||
            !typeof(IUIWindow).IsAssignableFrom(registration.WindowType))
        {
            throw new ArgumentException(
                $"Registered window type '{registration.WindowType.FullName}' must inherit UUserWidget and implement IUIWindow.");
        }

        var location = registration.ResourceKey;

        ct.ThrowIfCancellationRequested();

        var widgetClass = await UObject.AsyncStaticLoadClass<UUserWidget>(location)
            .ConfigureWithUnrealContext();

        ct.ThrowIfCancellationRequested();

        if (_owningController.IsDestroyed)
            throw new InvalidOperationException("Owning PlayerController is destroyed.");

        var widget = UObject.CreateWidget(widgetClass, _owningController);

        if (widget == null || widget.IsDestroyed)
            throw new InvalidOperationException($"Failed to create widget: {location}");

        if (widget is not IUIWindow window || !registration.WindowType.IsInstanceOfType(widget))
        {
            widget.RemoveFromParent();

            throw new InvalidOperationException(
                $"Widget {location} is not compatible with {registration.WindowType.FullName}");
        }

        // Create hidden: CreateAsync only prepares the pair, UIManager decides when to show it.
        var handle = new TStrongObjectPtr<UObject>(widget);
        AUIView? view = null;

        try
        {
            widget.Visibility = ESlateVisibility.Collapsed;
            // Add to the UIRoot canvas while hidden so the widget is initialized before the View
            // factory asks it for its architecture.
            GetParent(window.Level).AddChildToCanvas(widget);

            view = CreateView(registration, window);
            _windows.Add(window, new WindowState(view, handle));

            return new UIViewInstance(window, view);
        }
        catch
        {
            try
            {
                view?.Dispose();
            }
            finally
            {
                try
                {
                    if (!widget.IsDestroyed)
                        widget.RemoveFromParent();
                }
                finally
                {
                    handle.Dispose();
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Adds the existing native widget to its <see cref="EUILevel"/> canvas when needed and makes it
    /// visible. No business lifecycle call.
    /// </summary>
    public void Open(UIViewInstance instance, IUIData? data = null)
    {
        if (instance.Window is not UUserWidget widget)
            throw new ArgumentException("Unreal UI window must be a UUserWidget.", nameof(instance));

        if (widget.IsDestroyed)
            throw new ObjectDisposedException(widget.GetType().FullName, "Cannot open a destroyed Unreal UI widget.");

        var parent = GetParent(instance.Level);
        if (!parent.HasChild(widget))
        {
            if (widget.Parent != null)
                widget.RemoveFromParent();

            parent.AddChildToCanvas(widget);
        }

        widget.Visibility = ESlateVisibility.Visible;
    }

    /// <summary>Collapses the native widget. No business lifecycle call.</summary>
    public void Close(UIViewInstance instance)
    {
        if (instance.Window is UUserWidget { IsDestroyed: false } widget)
            widget.Visibility = ESlateVisibility.Collapsed;
    }

    /// <summary>
    /// Removes the native widget from its parent and disposes the strong object handle, then disposes
    /// the paired business View exactly once. The adapter is the binding owner in this phase because
    /// UnrealSharp has no generated ViewBinding yet. UIManager must not dispose the View independently.
    /// </summary>
    public void Release(UIViewInstance instance)
    {
        if (instance.Window == null || !_windows.Remove(instance.Window, out var state))
            return;

        try
        {
            if (instance.Window is UUserWidget { IsDestroyed: false } widget)
                widget.RemoveFromParent();
        }
        finally
        {
            try
            {
                state.Handle.Dispose();
            }
            finally
            {
                state.View.Dispose();
            }
        }
    }

    /// <summary>True while the widget still owns a live VC/View pair (not yet released).</summary>
    public bool TryGetView(IUIWindow window, out AUIView view)
    {
        if (window != null && _windows.TryGetValue(window, out var state))
        {
            view = state.View;
            return true;
        }

        view = null!;
        return false;
    }

    #endregion

    private AUIView CreateView(UIViewRegistration registration, IUIWindow window)
    {
        var factory = registration.Factory
            ?? throw new InvalidOperationException(
                $"UI view '{registration.ViewType.FullName}' is registered without a factory. UnrealSharp has no generated ViewBinding, so a Func<IArchitecture, AUIView> factory is required.");

        var architecture = Architecture ?? window.GetArchitecture()
            ?? throw new InvalidOperationException(
                $"Unreal UI window '{window.GetType().FullName}' returned no architecture for view '{registration.ViewType.FullName}'.");

        var view = factory(architecture)
            ?? throw new InvalidOperationException(
                $"The registration factory for '{registration.ViewType.FullName}' returned null.");

        try
        {
            if (!registration.ViewType.IsInstanceOfType(view))
            {
                throw new InvalidOperationException(
                    $"The registration factory for '{registration.ViewType.FullName}' returned '{view.GetType().FullName}'.");
            }

            ((ICanSetArchitecture)view).SetArchitecture(architecture);
            return view;
        }
        catch
        {
            view.Dispose();
            throw;
        }
    }

    private UCanvasPanel GetParent(EUILevel level)
    {
        var root = Root ?? throw new InvalidOperationException("UnrealUIAdapter is not initialized. Call InitAsync first.");

        return level switch
        {
            EUILevel.Scene => root.SceneRoot,
            EUILevel.Common => root.CommonRoot,
            EUILevel.Overlay => root.PopupRoot,
            _ => root.CommonRoot
        };
    }

    private sealed class WindowState
    {
        public WindowState(AUIView view, TStrongObjectPtr<UObject> handle)
        {
            View = view;
            Handle = handle;
        }

        public AUIView View { get; }
        public TStrongObjectPtr<UObject> Handle { get; }
    }
}
