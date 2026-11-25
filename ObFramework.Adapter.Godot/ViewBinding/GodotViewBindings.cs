using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ObFramework.Core;
using ObFramework.Integrated.UI;
using ObFramework.Integrated.ViewBinding;

namespace ObFramework.Adapter.Godot.ViewBinding
{
    internal sealed class GodotViewBindings
    {
        private readonly SceneTree _tree;
        private readonly CancellationToken _stopping;
        private readonly Dictionary<ulong, ViewBinding> _bindings = new();
        private readonly HashSet<ViewBinding> _pending = new();
        private IArchitecture? _architecture;
        private TaskCompletionSource<bool>? _idle;
        private int _operations;
        private bool _closing;

        public static GodotViewBindings? Current { get; private set; }
        public bool IsReady => _architecture != null && !_closing && !_stopping.IsCancellationRequested;

        public GodotViewBindings(SceneTree tree, CancellationToken stopping)
        {
            if (Current != null)
                throw new InvalidOperationException("Another OBF Godot view binding runtime is active.");
            _tree = tree;
            _stopping = stopping;
            _tree.NodeAdded += OnNodeAdded;
            _tree.NodeRemoved += OnNodeRemoved;
            Current = this;
        }

        public void Prepare(IArchitecture architecture)
        {
            _architecture = architecture;
            Scan(_tree.Root);
        }

        private void Scan(Node node)
        {
            if (!IsReady || !GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) return;
            OnNodeAdded(node);
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) return;
            foreach (var child in node.GetChildren()) Scan(child);
        }

        private void OnNodeAdded(Node node)
        {
            if (!IsReady || node is not IGeneratedViewBinding<IController> source ||
                source.ControllerType != node.GetType() || Find(node) != null)
                return;

            BeginOperation();
            var binding = new ViewBinding(this, node, GetInitialEnabled(node));
            _bindings.Add(binding.Id, binding);
            try
            {
                var view = source.CreateView(_architecture!);
                binding.Initialize(view);
                if (binding.IsReleased) return;
                if (view is ICanSetArchitecture setter) setter.SetArchitecture(_architecture!);
                if (!binding.IsReleased) _pending.Add(binding);
            }
            catch (Exception error) { Fail(binding, error); }
            finally { EndOperation(); }
        }

        private bool GetInitialEnabled(Node node)
        {
            if (node is IUIWindow) return false;
            for (var parent = node.GetParent(); parent != null; parent = parent.GetParent())
                if (parent is IUIWindow) return Find(parent)?.RequestedEnabled ?? false;
            return true;
        }

        private void CollectBranch(Node node, List<ViewBinding> bindings, bool requireBinding)
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || node.IsQueuedForDeletion()) return;
            var binding = Find(node);
            if (binding != null) bindings.Add(binding);
            else if (requireBinding && node is IGeneratedViewBinding<IController> source && source.ControllerType == node.GetType())
                throw new InvalidOperationException($"Node '{node.GetPath()}' did not create its View.");
            foreach (var child in node.GetChildren()) CollectBranch(child, bindings, requireBinding);
        }

        public void CompleteBranchReady(Node root)
        {
            if (!IsReady) throw new InvalidOperationException("Godot view binding runtime is not ready.");
            var bindings = new List<ViewBinding>();
            CollectBranch(root, bindings, true);
            BeginOperation();
            try
            {
                foreach (var binding in bindings)
                {
                    if (!IsReady || binding.IsReleased || !GodotObject.IsInstanceValid(binding.Controller) ||
                        !binding.Controller.IsInsideTree() || binding.Controller.IsQueuedForDeletion() || !binding.Controller.IsNodeReady())
                        throw new InvalidOperationException($"View '{binding.Description}' is not ready.");
                    if (!_pending.Remove(binding)) continue;
                    try { CompleteReady(binding); }
                    catch (Exception error) { Fail(binding, error); throw; }
                }
            }
            finally { EndOperation(); }
        }

        public void ChangeBranchEnabled(Node root, bool enabled)
        {
            if (enabled && !IsReady) throw new InvalidOperationException("Godot view binding runtime is not ready.");
            var bindings = new List<ViewBinding>();
            CollectBranch(root, bindings, enabled);
            BeginOperation();
            try
            {
                foreach (var binding in bindings)
                {
                    try { binding.ChangeEnabled(enabled); }
                    catch (Exception error) { Fail(binding, error); throw; }
                }
            }
            finally { EndOperation(); }
        }

        private void CompleteReady(ViewBinding binding)
        {
            if (binding.Controller is IBelongToArchitecture belongs &&
                !ReferenceEquals(belongs.GetArchitecture(), _architecture))
                throw new InvalidOperationException("The controller resolves a different architecture.");
            binding.CompleteReady();
        }

        private void OnNodeRemoved(Node node)
        {
            var binding = Find(node);
            if (binding == null) return;
            BeginOperation();
            try { RemoveAndRelease(binding); }
            catch (Exception error) { GD.PushError(error.ToString()); }
            finally { EndOperation(); }
        }

        public bool TryGetView(Node controller, out IController view)
        {
            if (GodotObject.IsInstanceValid(controller) && Find(controller)?.View is { } bound)
            {
                view = bound;
                return true;
            }
            view = null!;
            return false;
        }

        public void ChangeEnabled(Node controller, bool enabled)
        {
            if ((enabled && !IsReady) || !GodotObject.IsInstanceValid(controller)) return;
            var binding = Find(controller);
            if (binding == null) return;
            BeginOperation();
            try { binding.ChangeEnabled(enabled); }
            catch (Exception error) { Fail(binding, error); }
            finally { EndOperation(); }
        }

        public void BeforeUpdate()
        {
            if (!IsReady || _pending.Count == 0) return;
            foreach (var binding in new List<ViewBinding>(_pending))
            {
                if (!IsReady) break;
                if (!_pending.Contains(binding)) continue;
                if (!GodotObject.IsInstanceValid(binding.Controller) || !binding.Controller.IsInsideTree() ||
                    binding.Controller.IsQueuedForDeletion())
                {
                    try { RemoveAndRelease(binding); }
                    catch (Exception error) { GD.PushError(error.ToString()); }
                    continue;
                }
                if (!binding.Controller.IsNodeReady()) continue;
                _pending.Remove(binding);
                try { CompleteReady(binding); }
                catch (Exception error) { Fail(binding, error); }
            }
        }

        private void Fail(ViewBinding binding, Exception error)
        {
            try { RemoveAndRelease(binding); }
            catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
            GD.PushError($"View binding failed for {binding.Description}: {error}");
        }

        private void RemoveAndRelease(ViewBinding binding)
        {
            if (_bindings.TryGetValue(binding.Id, out var current) && ReferenceEquals(current, binding))
                _bindings.Remove(binding.Id);
            _pending.Remove(binding);
            binding.Release();
        }

        private ViewBinding? Find(Node controller) =>
            _bindings.TryGetValue(controller.GetInstanceId(), out var binding) &&
            ReferenceEquals(controller, binding.Controller) ? binding : null;

        public void BeginOperation() => _operations++;

        public void EndOperation()
        {
            if (--_operations == 0) _idle?.TrySetResult(true);
        }

        public async Task ReleaseAsync()
        {
            _closing = true;
            if (GodotObject.IsInstanceValid(_tree))
            {
                _tree.NodeAdded -= OnNodeAdded;
                _tree.NodeRemoved -= OnNodeRemoved;
            }
            if (_operations != 0)
            {
                _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                await _idle.Task;
            }
            var errors = new List<Exception>();
            foreach (var binding in new List<ViewBinding>(_bindings.Values))
            {
                try { RemoveAndRelease(binding); }
                catch (Exception error) { errors.Add(error); }
            }
            _pending.Clear();
            Current = null;
            if (errors.Count > 0) throw new AggregateException("Godot view cleanup failed.", errors);
        }
    }
}
