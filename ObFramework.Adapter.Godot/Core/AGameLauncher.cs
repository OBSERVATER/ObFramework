using System;
using System.Threading.Tasks;
using Godot;
using ObFramework.Core;

namespace ObFramework.Adapter.Godot.Core
{
    public abstract partial class AGameLauncher : AViewController
    {
        private PluginApplication _application = null!;
        private bool _quitting;
        protected abstract PluginApplication CreateApplication();
        public override IArchitecture GetArchitecture() => _application.Architecture;
        public Task StopAsync() => _application?.StopAsync() ?? Task.CompletedTask;

        public override void _Ready()
        {
            base._Ready();
            GetTree().AutoAcceptQuit = false;
            _ = RunLaunchAsync();
        }

        private async Task RunLaunchAsync()
        {
            try
            {
                _application = CreateApplication();
                await _application.StartAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { GD.PushError(error.ToString()); }
        }

        public override void _ExitTree()
        {
            _ = StopAndObserveAsync();
            base._ExitTree();
        }

        public override void _Notification(int what)
        {
            base._Notification(what);
            if (what != NotificationWMCloseRequest || _quitting)
                return;
            _quitting = true;
            _ = QuitAsync();
        }

        private async Task QuitAsync()
        {
            var tree = GetTree();
            await StopAndObserveAsync();
            if (GodotObject.IsInstanceValid(tree))
                tree.Quit();
        }

        private async Task StopAndObserveAsync()
        {
            try { await StopAsync(); }
            catch (Exception error) { GD.PushError(error.ToString()); }
        }
    }
}
