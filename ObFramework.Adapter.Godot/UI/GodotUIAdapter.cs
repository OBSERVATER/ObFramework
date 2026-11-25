using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ObFramework.Adapter.Godot.ViewBinding;
using ObFramework.Integrated.Resource;
using ObFramework.Integrated.Resource.Handle;
using ObFramework.Integrated.UI;

namespace ObFramework.Adapter.Godot.UI
{
    public sealed class GodotUIAdapter : IUIAdapter
    {
        private readonly ResourcePlugin _assets;
        private readonly Dictionary<IUIWindow, (IHandle Handle, Node.ProcessModeEnum Mode)> _windows = new(ReferenceEqualityComparer.Instance);

        public UIRoot? Root { get; set; }
        public UIViewRegistry Registry { get; }

        public GodotUIAdapter(ResourcePlugin assets, UIViewRegistry registry)
        {
            _assets = assets;
            Registry = registry;
        }

        public void Register<TView, TWindow>(string resourceKey)
            where TView : AUIView where TWindow : AUIWindow
            => Registry.Register<TView, TWindow>(resourceKey);

        public async Task<UIViewInstance?> CreateAsync(Type viewType, CancellationToken ct = default)
        {
            if (!Registry.TryGet(viewType, out var registration))
                throw new InvalidOperationException($"UI view '{viewType.FullName}' is not registered.");

            var views = GetBindings();
            IHandle? handle = null;
            Node? node = null;
            try
            {
                handle = await _assets.LoadAssetAsync<PackedScene>(registration.ResourceKey, ct);
                ct.ThrowIfCancellationRequested();
                var scene = handle.Get<PackedScene>();
                node = scene.Instantiate();
                if (node is not Control control || node is not IUIWindow window ||
                    !registration.WindowType.IsInstanceOfType(window))
                    throw new InvalidOperationException($"Scene '{registration.ResourceKey}' has an invalid window root.");

                var mode = control.ProcessMode;
                control.Hide();
                control.ProcessMode = Node.ProcessModeEnum.Disabled;
                GetParent(window.Level).AddChild(control);
                views.CompleteBranchReady(control);
                ct.ThrowIfCancellationRequested();

                if (!TryGetView(window, out var view) || view.GetType() != registration.ViewType)
                    throw new InvalidOperationException($"Scene '{registration.ResourceKey}' did not bind '{viewType.FullName}'.");

                _windows.Add(window, (handle, mode));
                return new UIViewInstance(window, view);
            }
            catch
            {
                try
                {
                    if (GodotObject.IsInstanceValid(node)) node!.QueueFree();
                }
                finally { handle?.Dispose(); }
                throw;
            }
        }

        public void Open(UIViewInstance instance, IUIData? data = null)
        {
            if (!TryGetControl(instance.Window, out var control))
                throw new ObjectDisposedException(instance.Window.GetType().FullName);
            if (control.GetParent() != GetParent(instance.Window.Level))
                throw new InvalidOperationException("A bound UI window cannot change its parent.");

            var views = GetBindings();
            control.MoveToFront();
            control.ProcessMode = _windows[instance.Window].Mode;
            control.Show();
            views.ChangeBranchEnabled(control, true);
        }

        public void Close(UIViewInstance instance)
        {
            if (!TryGetControl(instance.Window, out var control)) return;
            control.Hide();
            control.ProcessMode = Node.ProcessModeEnum.Disabled;
            GodotViewBindings.Current?.ChangeBranchEnabled(control, false);
        }

        public void Release(UIViewInstance instance)
        {
            if (!_windows.Remove(instance.Window, out var state)) return;
            try
            {
                if (TryGetControl(instance.Window, out var control))
                {
                    try { Close(instance); }
                    finally { if (GodotObject.IsInstanceValid(control)) control.QueueFree(); }
                }
            }
            finally { state.Handle.Dispose(); }
        }

        public bool TryGetView(IUIWindow window, out AUIView view)
        {
            if (TryGetControl(window, out var control) && GodotViewBindings.Current is { } bindings &&
                bindings.TryGetView(control, out var bound) && bound is AUIView typed)
            {
                view = typed;
                return true;
            }
            view = null!;
            return false;
        }

        private static GodotViewBindings GetBindings() =>
            GodotViewBindings.Current is { IsReady: true } bindings ? bindings :
                throw new InvalidOperationException("Godot view binding runtime is not ready.");

        private Control GetParent(EUILevel level)
        {
            var root = Root ?? throw new InvalidOperationException("Godot UI root is not initialized.");
            return level switch
            {
                EUILevel.Scene => root.SceneRoot,
                EUILevel.Common => root.CommonRoot,
                EUILevel.Overlay => root.PopupRoot,
                _ => root.CommonRoot
            };
        }

        private static bool TryGetControl(IUIWindow window, out Control control)
        {
            if (window is Control candidate && GodotObject.IsInstanceValid(candidate) && !candidate.IsQueuedForDeletion())
            {
                control = candidate;
                return true;
            }
            control = null!;
            return false;
        }
    }
}
