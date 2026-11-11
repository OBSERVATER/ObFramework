using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ObFramework.Core;
using ObFramework.Integrated.Resource;
using ObFramework.Integrated.Resource.Handle;
using ObFramework.Integrated.UI;

namespace ObFramework.Adapter.Godot.UI
{
    /// <summary>
    /// Godot implementation of the final <see cref="IUIAdapter"/> pair contract.
    /// <para>
    /// A window is a <see cref="PackedScene"/> loaded through the shared <see cref="ResourcePlugin"/>
    /// from its registration resource key (hand-written registrations use
    /// <c>res://UI/{ViewName}.tscn</c>). Its root must be a <see cref="Control"/> implementing the
    /// registered window type and keeps the Scene/Common/Overlay hierarchy provided by
    /// <see cref="UIRoot"/>. Each window is paired with one business <see cref="AUIView"/> created by
    /// the explicit registration factory. UIManager drives the business lifecycle
    /// (<c>InitView/OpenView/CloseView</c>); this adapter only creates, shows, hides and frees native
    /// objects.
    /// </para>
    /// <para>
    /// Architecture resolution: the factory receives <see cref="UIViewRegistration.Factory"/>'s
    /// <see cref="IArchitecture"/>. It is either the architecture supplied to the constructor (or
    /// <see cref="Architecture"/>) or, when none was supplied, the architecture reported by the
    /// created engine window through <see cref="IUIWindow.GetArchitecture"/>. No engine type leaks into
    /// the Framework and no assembly scanning or setup.json read happens here.
    /// </para>
    /// <para>
    /// Lifecycle ownership in this phase: there is no generated Godot ViewBinding yet, so the adapter
    /// is the binding owner. It attaches the architecture to the View
    /// (<see cref="ICanSetArchitecture.SetArchitecture"/>) and disposes the View exactly once in
    /// <see cref="Release"/>.
    /// </para>
    /// </summary>
    public class GodotUIAdapter : IUIAdapter
    {
        private readonly ResourcePlugin _assets;
        private readonly Dictionary<IUIWindow, WindowState> _windows = new Dictionary<IUIWindow, WindowState>();

        /// <summary>
        /// UIRoot that provides the Scene/Common/Overlay layers. Assign it (usually with
        /// <see cref="UIRoot.Ensure"/>) before the first UI instance is created.
        /// </summary>
        public UIRoot? Root { get; set; }

        /// <summary>
        /// Architecture passed to registration factories. When null (default) the architecture is
        /// read from the created engine window's <see cref="IUIWindow.GetArchitecture"/>.
        /// </summary>
        public IArchitecture? Architecture { get; set; }

        /// <summary>
        /// Adapter-owned explicit registration map. Hand-written startup code and future generated
        /// <c>RegisterAll</c> code register through <see cref="Register{TView,TWindow}"/> or directly
        /// through this registry. Registration is never inferred by scanning assemblies.
        /// </summary>
        public UIViewRegistry Registry { get; } = new UIViewRegistry();

        /// <param name="assets">Shared resource plugin used to load <see cref="PackedScene"/> instances.</param>
        /// <param name="architecture">
        /// Optional architecture forwarded to View factories. When omitted it is resolved from the
        /// created Godot window, which keeps the existing <see cref="ResourcePlugin"/>-only adapter
        /// construction working (PluginApplication creates the architecture after plugin init).
        /// </param>
        public GodotUIAdapter(ResourcePlugin assets, IArchitecture? architecture = null)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            Architecture = architecture;
        }

        #region Registration

        /// <summary>
        /// Hand-written registration of one business View. The factory is an explicit delegate, never
        /// reflection, and receives the resolved architecture. Example:
        /// <code>
        /// ui.Register&lt;ShopView, ShopWindow&gt;("res://UI/ShopView.tscn", architecture => new ShopView(architecture));
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
        /// A factory is required because Godot has no generated ViewBinding that could construct the View.
        /// </summary>
        public void Register(Type viewType, Type windowType, string resourceKey, Func<IArchitecture, AUIView> factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            Registry.Register(viewType, windowType, resourceKey, factory);
        }

        #endregion

        #region IUIAdapter

        /// <summary>
        /// Loads and instantiates the registered <see cref="PackedScene"/>, validates the root
        /// <see cref="Control"/>/<see cref="IUIWindow"/>, creates the paired business View through the
        /// registration factory and returns the inactive pair. No business lifecycle method is invoked.
        /// </summary>
        public async Task<UIViewInstance?> CreateAsync(Type viewType, CancellationToken ct = default)
        {
            if (viewType == null)
                throw new ArgumentNullException(nameof(viewType));

            if (!Registry.TryGet(viewType, out var registration))
            {
                throw new InvalidOperationException(
                    $"UI view '{viewType.FullName}' is not registered. Call {nameof(Register)} during application startup.");
            }

            var location = registration.ResourceKey;
            Node? node = null;
            IHandle? handle = null;
            AUIView? view = null;

            try
            {
                ct.ThrowIfCancellationRequested();

                handle = await _assets.LoadAssetAsync<PackedScene>(location, ct);
                ct.ThrowIfCancellationRequested();

                var scene = handle.Get<PackedScene>()
                    ?? throw new InvalidOperationException($"Resource '{location}' is not a PackedScene.");
                node = scene.Instantiate();

                if (node is not Control control || node is not IUIWindow window ||
                    !registration.WindowType.IsInstanceOfType(window))
                {
                    throw new InvalidOperationException(
                        $"Scene '{location}' root must be a Control implementing {registration.WindowType.FullName}.");
                }

                // Create hidden: CreateAsync only prepares the pair, UIManager decides when to show it.
                control.Hide();

                var parent = GetParent(window.Level);
                // Add to the tree first so the window's _Ready runs before the View factory asks it
                // for its architecture.
                parent.AddChild(control);
                view = CreateView(registration, window);

                _windows.Add(window, new WindowState(view, handle));
                handle = null;

                return new UIViewInstance(window, view);
            }
            catch
            {
                // A failed create must not leak the native node, the resource handle or the View.
                try
                {
                    view?.Dispose();
                }
                finally
                {
                    try
                    {
                        if (node != null && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
                            node.QueueFree();
                    }
                    finally
                    {
                        handle?.Dispose();
                    }
                }

                throw;
            }
        }

        /// <summary>
        /// Shows the existing native Control: reparents it to its <see cref="EUILevel"/> layer when
        /// needed, brings it to the front and shows it. No business lifecycle call.
        /// </summary>
        public void Open(UIViewInstance instance, IUIData? data = null)
        {
            var window = instance.Window;
            var control = GetControl(window, "Open");
            var parent = GetParent(window.Level);

            if (control.GetParent() == null)
                parent.AddChild(control);
            else if (control.GetParent() != parent)
                control.Reparent(parent, false);

            control.MoveToFront();
            control.Show();
        }

        /// <summary>Hides the native Control. No business lifecycle call.</summary>
        public void Close(UIViewInstance instance)
        {
            if (!TryGetControl(instance.Window, out var control))
                return;

            control.Hide();
        }

        /// <summary>
        /// Frees the native Control and its <see cref="PackedScene"/> handle, then disposes the paired
        /// business View exactly once. The adapter is the binding owner in this phase because Godot has
        /// no generated ViewBinding yet. UIManager must not dispose the View independently.
        /// </summary>
        public void Release(UIViewInstance instance)
        {
            var window = instance.Window;
            if (window == null || !_windows.Remove(window, out var state))
                return;

            try
            {
                if (TryGetControl(window, out var control))
                    control.QueueFree();
            }
            finally
            {
                try
                {
                    state.Handle?.Dispose();
                }
                finally
                {
                    state.View.Dispose();
                }
            }
        }

        /// <summary>True while the window still owns a live VC/View pair (not yet released).</summary>
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
                    $"UI view '{registration.ViewType.FullName}' is registered without a factory. Godot has no generated ViewBinding, so a Func<IArchitecture, AUIView> factory is required.");

            var architecture = Architecture ?? window.GetArchitecture()
                ?? throw new InvalidOperationException(
                    $"Godot UI window '{window.GetType().FullName}' returned no architecture for view '{registration.ViewType.FullName}'.");

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

        private Control GetParent(EUILevel level)
        {
            var root = Root
                ?? throw new InvalidOperationException(
                    "GodotUIAdapter.Root is not assigned. Call UIRoot.Ensure(tree) and assign the result before opening UI.");
            return level switch
            {
                EUILevel.Scene => root.SceneRoot,
                EUILevel.Common => root.CommonRoot,
                EUILevel.Overlay => root.PopupRoot,
                _ => root.CommonRoot
            };
        }

        private Control GetControl(IUIWindow window, string operation)
        {
            if (!TryGetControl(window, out var control))
            {
                throw new ObjectDisposedException(
                    window.GetType().FullName,
                    $"Cannot {operation} a Godot UI window that was released.");
            }

            return control;
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

        private sealed class WindowState
        {
            public WindowState(AUIView view, IHandle? handle)
            {
                View = view;
                Handle = handle;
            }

            public AUIView View { get; }
            public IHandle? Handle { get; }
        }
    }
}
