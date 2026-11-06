using ObFramework.Integrated.UI;
using UnrealSharp;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using UnrealSharp.UMG;

namespace ObFramework.Adapter.UnrealSharp.UI;

public class UnrealUIAdapter : IUIAdapter
{
    private const string RootClassPath = "/Game/UI/WBP_ObUIRoot.WBP_ObUIRoot_C";
    private readonly APlayerController _owningController;
    private readonly Dictionary<IUIWindow, TStrongObjectPtr<UObject>> _retained = new();
    private TStrongObjectPtr<UObject>? _rootHandle;
    public UIRoot? Root { get; private set; }

    public UnrealUIAdapter(APlayerController owningController)
    {
        _owningController = owningController ?? throw new ArgumentNullException(nameof(owningController));
    }

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

    public async Task<T?> CreateAsync<T>(CancellationToken ct = default, IUIData? data = null) where T : class, IUIWindow 
        => await CreateAsync(typeof(T), ct, data) as T;

    public async Task<IUIWindow> CreateAsync(Type type, CancellationToken ct = default, IUIData? data = null)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!typeof(IUIWindow).IsAssignableFrom(type) || !typeof(UUserWidget).IsAssignableFrom(type))
        {
            throw new ArgumentException(
                $"{type.Name} must inherit UUserWidget and implement IUIWindow.");
        }

        ct.ThrowIfCancellationRequested();

        var assetName = type.Name;
        var location = $"/Game/UI/{assetName}.{assetName}_C";

        var widgetClass = await UObject.AsyncStaticLoadClass<UUserWidget>(location)
            .ConfigureWithUnrealContext();

        ct.ThrowIfCancellationRequested();

        if (_owningController.IsDestroyed)
            throw new InvalidOperationException("Owning PlayerController is destroyed.");

        var widget = UObject.CreateWidget(widgetClass, _owningController);

        if (widget == null || widget.IsDestroyed)
            throw new InvalidOperationException($"Failed to create widget: {location}");

        if (widget is not IUIWindow window || !type.IsInstanceOfType(widget))
        {
            widget.RemoveFromParent();

            throw new InvalidOperationException($"Widget {location} is not compatible with {type.FullName}");
        }

        var handle = new TStrongObjectPtr<UObject>(widget);

        try
        {
            window.Init(data);
            ct.ThrowIfCancellationRequested();
            _retained.Add(window, handle);

            return window;
        }
        catch
        {
            try
            {
                if (!widget.IsDestroyed) widget.RemoveFromParent();
            }
            finally
            {
                handle.Dispose();
            }

            throw;
        }
    }
    
    public void Open(IUIWindow instance, IUIData? data = null)
    {
        var ui = (UUserWidget)instance;
        var parent = GetParent(instance.Level);

        if (!parent.HasChild(ui))
        {
            if (ui.Parent != null)
                ui.RemoveFromParent();

            parent.AddChildToCanvas(ui);
        }

        ui.Visibility = ESlateVisibility.Visible;

        try
        {
            instance.Open(data);
        }
        catch
        {
            if (!ui.IsDestroyed)
                ui.Visibility = ESlateVisibility.Collapsed;

            throw;
        }
    }

    public void Close(IUIWindow instance)
    {
        var ui = (UUserWidget)instance;

        try
        {
            instance.Close();
        }
        finally
        {
            if (!ui.IsDestroyed)
                ui.Visibility = ESlateVisibility.Collapsed;
        }
    }
    
    public void Release(IUIWindow instance)
    {
        if (!_retained.Remove(instance, out var handle))
            return;

        try
        {
            if (instance is UUserWidget { IsDestroyed: false } widget)
            {
                widget.RemoveFromParent();
            }
        }
        finally
        {
            handle.Dispose();
        }
    }

    public void Deinit()
    {
        var windows = new List<IUIWindow>(_retained.Keys);

        foreach (var window in windows)
        {
            Release(window);
        }

        try
        {
            if (Root is { Widget.IsDestroyed: false })
            {
                Root.Widget.RemoveFromParent();
            }
        }
        finally
        {
            _rootHandle?.Dispose();
            _rootHandle = null;
            Root = null;
        }
    }
    private UCanvasPanel GetParent(EUILevel level)
    {
        var root = Root ?? throw new InvalidOperationException("UnrealUIAdapter is not initialized.");

        return level switch
        {
            EUILevel.Scene => root.SceneRoot,
            EUILevel.Common => root.CommonRoot,
            EUILevel.Overlay => root.PopupRoot,
            _ => root.CommonRoot
        };
    }
}
