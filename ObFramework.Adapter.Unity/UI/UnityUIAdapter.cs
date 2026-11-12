using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Adapter.Unity.ViewBinding;
using ObFramework.Integrated.Resource;
using ObFramework.Integrated.Resource.Handle;
using ObFramework.Integrated.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ObFramework.Adapter.Unity.UI
{
    /// <summary>
    /// Unity engine adapter for the UI 2.0 pair contract. It owns the native window object and the
    /// prefab resource handle, and reuses the existing generated ViewBinding path to construct the
    /// business View inside an inactive instantiation context. Business lifecycle methods are driven
    /// exclusively by <see cref="ObFramework.Integrated.UI.UIManager"/>; the adapter only activates
    /// or deactivates the native object and refreshes the generated display bindings after Open.
    /// </summary>
    public sealed class UnityUIAdapter : IUIAdapter
    {
        private readonly ResourcePlugin _assets;
        private readonly Dictionary<IUIWindow, IHandle> _handles = new Dictionary<IUIWindow, IHandle>(ReferenceComparer.Instance);

        public UnityUIAdapter(ResourcePlugin assets, UIViewRegistry registry)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// UI hierarchy root used to pick the Scene/Common/Overlay parent. Assign it before any view is
        /// created; it must be initialized (<see cref="UIRoot.Init"/>) so its hierarchy exists.
        /// </summary>
        public UIRoot? Root { get; set; }

        /// <summary>
        /// Adapter-owned explicit View -> Unity VC -> resource key registry shared by hand-written and
        /// generated registrations. Registration never preloads assets, scans assemblies or reads
        /// setup.json at runtime.
        /// </summary>
        public UIViewRegistry Registry { get; }

        /// <summary>Registers a business View with its Unity VC and Addressables/Resources resource key.</summary>
        public void Register<TView, TWindow>(string resourceKey)
            where TView : AUIView
            where TWindow : AUIWindow
        {
            Registry.Register<TView, TWindow>(resourceKey);
        }

        /// <summary>
        /// Loads the registered prefab, instantiates and binds it while inactive (so the generated
        /// ViewBinding constructs the business View before first display), then returns the VC/View pair.
        /// The UIManager never receives a half-created instance: on any failure the clone is destroyed
        /// and the resource handle disposed.
        /// </summary>
        public async Task<UIViewInstance?> CreateAsync(Type viewType, CancellationToken ct = default)
        {
            if (viewType == null)
                throw new ArgumentNullException(nameof(viewType));
            if (!Registry.TryGet(viewType, out var registration))
                throw new InvalidOperationException(
                    $"UI view '{viewType.FullName}' is not registered on the Unity adapter. Register it with " +
                    $"{nameof(Register)}<TView, TWindow>(resourceKey) (or the generated RegisterAll) before opening.");

            EnsureRoot();

            IHandle? handle = null;
            GameObject? holder = null;
            GameObject? clone = null;
            try
            {
                handle = await _assets.LoadAssetAsync<GameObject>(registration.ResourceKey, ct);
                ct.ThrowIfCancellationRequested();

                var prefab = handle.Get<GameObject>()
                    ?? throw new InvalidOperationException(
                        $"UI resource '{registration.ResourceKey}' is not a GameObject prefab (view '{viewType.FullName}').");

                // Instantiate under an inactive holder: Awake/OnEnable run only when Open activates the
                // window, after the generated binding has created the business View and injected the
                // Architecture. This keeps native activation clearly separate from business OnOpen.
                holder = CreateInactiveHolder(registration.ResourceKey);
                clone = ObUnity.Instantiate(prefab, holder.transform, false);
                if (!clone)
                    throw new InvalidOperationException(
                        $"UI prefab '{registration.ResourceKey}' was destroyed while instantiating view '{viewType.FullName}'.");

                // The registered window controller identifies the window and therefore lives on the
                // prefab root (the clone root is the window GameObject).
                if (clone.GetComponent(registration.WindowType) is not IUIWindow window)
                    throw new InvalidOperationException(
                        $"UI prefab '{registration.ResourceKey}' does not have the registered Unity controller '{registration.WindowType.FullName}' on its root.");

                if (!TryGetView(window, out var view))
                    throw new InvalidOperationException(
                        $"Unity controller '{window.GetType().FullName}' for resource '{registration.ResourceKey}' did not create a bound " +
                        "business View. The controller must be a generated [BindView] partial MonoBehaviour and the OBF view binding runtime " +
                        "must be initialized before creating views.");

                // The registry maps one View type per VC. If the generated binding creates a different
                // View (for example a stale generator output after a View rename), fail here instead of
                // letting UIManager show a window whose View type does not match the request.
                if (view.GetType() != registration.ViewType)
                    throw new InvalidOperationException(
                        $"Unity controller '{window.GetType().FullName}' (resource '{registration.ResourceKey}') created business View " +
                        $"'{view.GetType().FullName}', but the adapter registered '{registration.ViewType.FullName}'. Regenerate the binding data " +
                        "so the [BindView] View type and the adapter registration agree.");

                // Force the clone inactive before reparenting: a prefab root whose activeSelf is true
                // would otherwise become active in the hierarchy under the active level root and fire
                // Awake/OnEnable (and therefore the generated display bindings) before UIManager runs
                // OnInit/OnOpen. Open is the only place that activates the native object.
                clone.SetActive(false);
                clone.transform.SetParent(GetParent(window.Level), false);

                _handles.Add(window, handle);
                handle = null; // the adapter now owns the prefab handle until Release
                clone = null;  // the adapter now owns the native object until Release
                return new UIViewInstance(window, view);
            }
            catch
            {
                if (clone)
                    Object.Destroy(clone);
                handle?.Dispose();
                throw;
            }
            finally
            {
                if (holder)
                    Object.Destroy(holder);
            }
        }

        /// <summary>
        /// Activates the existing native object and then refreshes the generated bindings (an already
        /// active Single window does not receive a new OnEnable, so the explicit refresh covers it).
        /// No business lifecycle method is called here.
        /// </summary>
        public void Open(UIViewInstance instance, IUIData? data = null)
        {
            var component = GetWindowComponent(instance.Window);
            var root = component.gameObject;
            root.transform.SetParent(GetParent(instance.Window.Level), false);
            root.SetActive(true);
            if (instance.Window is IGeneratedUIBinding binding)
                binding.RefreshBindings();
        }

        /// <summary>Deactivates the native object only. Business close already ran in UIManager.</summary>
        public void Close(UIViewInstance instance)
        {
            if (instance.Window is not Component component || !component)
                return; // the native object was destroyed externally; Release still disposes the handle.
            var root = component.gameObject;
            if (root.activeSelf)
                root.SetActive(false);
        }

        /// <summary>
        /// Destroys the native object and disposes its prefab handle. The business View is owned by the
        /// generated ViewBinding (its OnDestroy path disposes it exactly once); the adapter never
        /// disposes the View itself.
        /// </summary>
        public void Release(UIViewInstance instance)
        {
            _handles.Remove(instance.Window, out var handle);
            try
            {
                if (instance.Window is Component component && component)
                    Object.Destroy(component.gameObject);
            }
            finally
            {
                handle?.Dispose();
            }
        }

        /// <summary>Resolves the business View the generated binding already constructed for a window.</summary>
        public bool TryGetView(IUIWindow window, out AUIView view)
        {
            view = null!;
            if (window is not MonoBehaviour controller || !controller)
                return false;

            var bindings = UnityViewBindings.Current;
            if (bindings == null || !bindings.TryGetView(controller, out var bound) || bound is not AUIView typed)
                return false;

            view = typed;
            return true;
        }

        private static Component GetWindowComponent(IUIWindow window)
        {
            if (window is not Component component || !component)
                throw new InvalidOperationException(
                    $"Unity UI window '{window.GetType().FullName}' is not a live MonoBehaviour. The native object was destroyed or Release already ran.");
            return component;
        }

        private UIRoot EnsureRoot()
        {
            var root = Root;
            if (root == null)
                throw new InvalidOperationException(
                    $"{nameof(UnityUIAdapter)}.{nameof(Root)} must be assigned before creating or opening views.");
            return root;
        }

        private Transform GetParent(EUILevel level)
        {
            var root = EnsureRoot();
            Transform? parent = level switch
            {
                EUILevel.Scene => root.SceneRoot,
                EUILevel.Common => root.CommonRoot,
                EUILevel.Overlay => root.PopupRoot,
                _ => root.CommonRoot
            };
            return parent != null
                ? parent
                : throw new InvalidOperationException(
                    $"UIRoot has no hierarchy for level '{level}'. Call {nameof(UIRoot)}.{nameof(UIRoot.Init)}() before creating or opening views.");
        }

        private GameObject CreateInactiveHolder(string resourceKey)
        {
            // A temporary inactive parent keeps Object.Instantiate from running Awake/OnEnable until
            // Open. It is destroyed as soon as the clone is reparented under its level root.
            var holder = new GameObject($"OBF.UI.Instantiating({resourceKey})", typeof(RectTransform));
            holder.transform.SetParent(EnsureRoot().transform, false);
            holder.SetActive(false);
            return holder;
        }

        /// <summary>Reference identity comparer: Unity's destroyed-object null semantics must not break handle tracking.</summary>
        private sealed class ReferenceComparer : IEqualityComparer<IUIWindow>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public bool Equals(IUIWindow x, IUIWindow y) => ReferenceEquals(x, y);

            public int GetHashCode(IUIWindow obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
