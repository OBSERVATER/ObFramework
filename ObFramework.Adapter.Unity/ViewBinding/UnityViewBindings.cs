using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;
using ObFramework.Core.FrameLoop;
using ObFramework.Integrated.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ObFramework.Adapter.Unity.ViewBinding
{
    internal sealed class UnityViewBindings
    {
        private readonly CancellationToken _stopping;
        private readonly Dictionary<int, ViewBinding> _bindings = new();
        private readonly HashSet<ViewBinding> _watchForDestruction = new();
        private readonly HashSet<ViewBinding> _deferredTicks = new();
        private IArchitecture? _architecture;
        private TaskCompletionSource<bool>? _idle;
        private int _operations;
        private bool _closing;

        public static UnityViewBindings? Current { get; private set; }
        private bool IsStopping => _closing || _stopping.IsCancellationRequested;
        public bool IsReady => _architecture != null && !IsStopping;
        public FrameDispatcher Frame => _architecture!.Frame;

        public UnityViewBindings(CancellationToken stopping)
        {
            if (Current != null)
                throw new InvalidOperationException("Another OBF Unity view binding runtime is active.");

            _stopping = stopping;
            ConfigureMainThreadDispatch();
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Current = this;
        }

        /// <summary>
        /// Generated bindings may be notified from background threads; marshal those updates through
        /// Unity's main-thread synchronization context captured here (constructed on the main thread).
        /// </summary>
        private static void ConfigureMainThreadDispatch()
        {
            var mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var mainThread = SynchronizationContext.Current;
            UIBindingDispatch.ConfigureMainThread(mainThreadId, action =>
            {
                if (mainThread != null)
                    mainThread.Post(_ => action(), null);
                else
                    action();
            });
        }

        public void Prepare(IArchitecture architecture) => _architecture = architecture;

        public void BindClone(GameObject clone)
        {
            var created = new List<ViewBinding>();
            BeginOperation();
            try
            {
                EnsureReady();
                foreach (var component in clone.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    EnsureReady();
                    Bind(component, created);
                }
                EnsureReady();
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                foreach (var binding in created)
                {
                    try { RemoveAndRelease(binding); }
                    catch (Exception cleanupError) { errors.Add(cleanupError); }
                }
                try
                {
                    if (clone)
                        clone.SetActive(false);
                }
                catch (Exception cleanupError) { errors.Add(cleanupError); }
                finally
                {
                    if (clone)
                        Object.Destroy(clone);
                }
                if (errors.Count > 1)
                    throw new AggregateException("Failed to roll back instantiated views.", errors);
                throw;
            }
            finally { EndOperation(); }
        }

        private void EnsureReady()
        {
            if (!IsReady)
                throw new InvalidOperationException("OBF view binding is stopping.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ScanRoots(scene.GetRootGameObjects());

        public void ScanRoots(IEnumerable<GameObject> roots)
        {
            BeginOperation();
            try
            {
                foreach (var root in roots)
                {
                    if (!root)
                        continue;
                    foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (IsStopping)
                            return;
                        if (!IsReady)
                        {
                            if (component && component is IGeneratedViewBinding source &&
                                source.ControllerType == component.GetType())
                                Debug.LogError($"View binding skipped for {Describe(component)}: the scene loaded before application initialization.", component);
                            continue;
                        }
                        try { Bind(component); }
                        catch (Exception error) { Debug.LogError(error, component); }
                    }
                }
            }
            finally { EndOperation(); }
        }

        private void Bind(MonoBehaviour controller, List<ViewBinding>? created = null)
        {
            if (!controller || !(controller is IGeneratedViewBinding source) ||
                source.ControllerType != controller.GetType() || Find(controller) != null)
                return;

            var binding = new ViewBinding(this, controller, Describe(controller));
            _bindings.Add(binding.Id, binding);
            created?.Add(binding);
            if (!controller.isActiveAndEnabled)
                _watchForDestruction.Add(binding);

            try
            {
                var view = source.CreateView(_architecture!);
                // The generated binding constructs the business View; the Architecture capability is
                // injected here so business Views never need a second DI container.
                if (view is ICanSetArchitecture setter)
                    setter.SetArchitecture(_architecture!);

                // The VC is the window identity; a generated GetArchitecture() that points at a
                // different architecture than the bootstrapped one would bind a View to the wrong
                // context and only fail on first use, so reject the pairing immediately.
                if (controller is IBelongToArchitecture belongs)
                {
                    var controllerArchitecture = belongs.GetArchitecture();
                    if (controllerArchitecture != null && !ReferenceEquals(controllerArchitecture, _architecture))
                        throw new InvalidOperationException(
                            $"View controller '{controller.GetType().FullName}' resolves a different architecture than the active " +
                            "view-binding runtime; generated GetArchitecture() and the bootstrapped architecture must agree.");
                }

                // Child components are Views created directly by the binding layer, with no UIManager
                // entry that could run OnInit later; initialize them here exactly once. Window VCs are
                // initialized by UIManager.InitView (with their open data) and must not be initialized
                // here with null data.
                if (!(controller is IUIWindow) && view is AUIView childView)
                    childView.InitView(null);

                binding.Initialize(view);
                if (!controller)
                    RemoveAndRelease(binding);
                else if (controller.isActiveAndEnabled)
                {
                    // A controller that was already active when its binding was created (scene roots)
                    // missed the generated OnEnable refresh; refresh now that the View exists.
                    GeneratedViewBinding.Refresh(controller);
                }
            }
            catch (Exception error)
            {
                throw ReleaseAfterFailure(binding, error);
            }
        }

        public void ChangeEnabled(MonoBehaviour controller, bool enabled)
        {
            if (enabled && !IsReady)
                return;
            var binding = Find(controller);
            if (binding == null)
                return;

            if (enabled)
                _watchForDestruction.Remove(binding);
            BeginOperation();
            try { binding.ChangeEnabled(enabled); }
            catch (Exception error) { Debug.LogError(ReleaseAfterFailure(binding, error), controller); }
            finally { EndOperation(); }
        }

        public void Destroy(MonoBehaviour controller)
        {
            var binding = Find(controller);
            if (binding != null)
                ReleaseAndLog(binding);
        }

        /// <summary>
        /// Resolves the business View paired with a bound controller. Used by
        /// <see cref="UI.UnityUIAdapter"/> to build a <see cref="ObFramework.Integrated.UI.UIViewInstance"/>
        /// from the object the generated binding already constructed; it never creates a second View.
        /// </summary>
        public bool TryGetView(MonoBehaviour controller, out object view)
        {
            var binding = controller ? Find(controller) : null;
            if (binding?.View is { } bound)
            {
                view = bound;
                return true;
            }

            view = null!;
            return false;
        }

        public void DeferTick(ViewBinding binding) => _deferredTicks.Add(binding);
        public void CancelDeferredTick(ViewBinding binding) => _deferredTicks.Remove(binding);

        public void BeforeUpdate()
        {
            if (!IsReady)
                return;

            SweepDestroyed();
            _deferredTicks.RemoveWhere(binding => binding.TryStartDeferredTick());
        }

        private void OnSceneUnloaded(Scene scene) => SweepDestroyed();

        private void SweepDestroyed()
        {
            // Unity can omit OnDestroy for objects that have never been active.
            List<ViewBinding>? destroyed = null;
            foreach (var binding in _watchForDestruction)
            {
                if (!binding.Controller)
                    (destroyed ??= new List<ViewBinding>()).Add(binding);
            }
            if (destroyed == null)
                return;
            foreach (var binding in destroyed)
                ReleaseAndLog(binding);
        }

        private void ReleaseAndLog(ViewBinding binding)
        {
            BeginOperation();
            try { RemoveAndRelease(binding); }
            catch (Exception error) { Debug.LogError(error, binding.Controller); }
            finally { EndOperation(); }
        }

        private void RemoveAndRelease(ViewBinding binding)
        {
            if (_bindings.TryGetValue(binding.Id, out var current) && ReferenceEquals(current, binding))
                _bindings.Remove(binding.Id);
            _watchForDestruction.Remove(binding);

            try { binding.Release(); }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Failed to release view for {binding.Description}.", error);
            }
        }

        private static Exception ReleaseAfterFailure(ViewBinding binding, Exception failure)
        {
            Exception reason = failure;
            try { binding.Release(); }
            catch (Exception cleanupError) { reason = new AggregateException(failure, cleanupError); }
            return new InvalidOperationException($"View binding failed for {binding.Description}: {failure.Message}", reason);
        }

        private ViewBinding? Find(MonoBehaviour controller)
        {
            return _bindings.TryGetValue(controller.GetInstanceID(), out var binding) &&
                   ReferenceEquals(controller, binding.Controller) ? binding : null;
        }

        private static string Describe(MonoBehaviour controller)
        {
            var path = controller.name;
            for (var parent = controller.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            var scene = controller.gameObject.scene;
            return $"'{scene.name}/{path}' ({controller.GetType().FullName})";
        }

        public void BeginOperation() => _operations++;

        public void EndOperation()
        {
            if (--_operations == 0)
                _idle?.TrySetResult(true);
        }

        public async Task ReleaseAsync()
        {
            _closing = true;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;

            if (_operations != 0)
            {
                _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                await _idle.Task;
            }

            var bindings = new List<ViewBinding>(_bindings.Values);
            var errors = new List<Exception>();
            foreach (var binding in bindings)
            {
                try { RemoveAndRelease(binding); }
                catch (Exception error) { errors.Add(error); }
            }
            Current = null;
            if (errors.Count != 0)
                throw new AggregateException("Unity view cleanup failed.", errors);
        }
    }
}
