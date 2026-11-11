using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ObFramework.Core;
using ObFramework.Core.FrameLoop;

namespace ObFramework.Adapter.Godot.Core
{
    public partial class GodotRuntimeHost : Node, IApplicationFrameLoopDriver
    {
        private const string RuntimeGroup = "_ObFrameworkRuntime";
        private SceneTree? _tree;
        private GodotFrameLoopBridge? _frameLoop;
        private Node? _uiRoot;
        private Task? _stopTask;
        private bool _autoAcceptQuit;
        private bool _quitRequested;

        public PluginApplication? Application { get; private set; }

        public static GodotRuntimeHost? Find(SceneTree tree) =>
            tree.GetFirstNodeInGroup(RuntimeGroup) as GodotRuntimeHost;

        internal void Attach(SceneTree tree, Node launcher)
        {
            if (this == tree.CurrentScene || IsAncestorOf(launcher))
                throw new InvalidOperationException("The runtime host cannot contain the Bootstrap launcher or replace the current scene.");
            if (IsQueuedForDeletion() || (IsInsideTree() && GetTree() != tree))
                throw new InvalidOperationException("The runtime host must be available in the launcher's SceneTree.");

            AddToGroup(RuntimeGroup);
            var parent = GetParent();
            if (parent == null)
                tree.Root.AddChild(this);
            else if (parent != tree.Root)
                Reparent(tree.Root);

            _tree = tree;
            _autoAcceptQuit = tree.AutoAcceptQuit;
            tree.AutoAcceptQuit = false;
        }

        internal async Task StartAsync(Func<PluginApplication> createApplication)
        {
            try
            {
                var application = createApplication()
                    ?? throw new InvalidOperationException("The launcher did not create an application.");
                if (_stopTask != null)
                {
                    await application.StopAsync();
                    return;
                }

                Application = application;
                await application.StartAsync(this);
            }
            catch (OperationCanceledException) when (_stopTask != null) { }
            catch
            {
                if (_stopTask == null)
                    await StopAndObserveAsync();
                throw;
            }
        }

        public Task StopAsync()
        {
            if (_stopTask != null)
                return _stopTask;
            if (_tree == null)
                return Task.CompletedTask;

            var completion = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopTask = completion.Task.Unwrap();
            completion.SetResult(StopCoreAsync());
            return _stopTask;
        }

        protected virtual void PrepareRuntime(IArchitecture architecture) { }

        void IApplicationFrameLoopDriver.BeginStartup(CancellationToken stopping) { }

        void IApplicationFrameLoopDriver.Prepare(IArchitecture architecture)
        {
            _frameLoop = new GodotFrameLoopBridge();
            AddChild(_frameLoop);
            PrepareRuntime(architecture);
        }

        void IFrameLoopDriver.Bind(FrameDispatcher dispatcher) => _frameLoop!.Bind(dispatcher);
        void IFrameLoopDriver.Unbind() => _frameLoop?.Unbind();
        Task IApplicationFrameLoopDriver.ReleaseAsync() => Task.CompletedTask;

        internal void OwnUIRoot(Node root) => _uiRoot = root;

        public override void _Notification(int what)
        {
            base._Notification(what);
            if (what != NotificationWMCloseRequest || _quitRequested || _tree == null)
                return;

            _quitRequested = true;
            _ = QuitAsync(_tree);
        }

        public override void _ExitTree()
        {
            if (_tree != null)
                _ = StopAndObserveAsync();
            base._ExitTree();
        }

        private async Task StopCoreAsync()
        {
            try
            {
                if (Application != null)
                    await Application.StopAsync();
            }
            finally
            {
                var tree = _tree;
                _tree = null;
                Application = null;
                if (IsInstanceValid(tree))
                    tree!.AutoAcceptQuit = _autoAcceptQuit;
                if (IsInstanceValid(_uiRoot))
                    _uiRoot!.QueueFree();
                _uiRoot = null;
                if (IsInstanceValid(this) && IsInsideTree() && !IsQueuedForDeletion())
                    QueueFree();
            }
        }

        private async Task QuitAsync(SceneTree tree)
        {
            await StopAndObserveAsync();
            if (IsInstanceValid(tree))
                tree.Quit();
        }

        private async Task StopAndObserveAsync()
        {
            try { await StopAsync(); }
            catch (Exception error) { GD.PushError(error.ToString()); }
        }
    }
}
