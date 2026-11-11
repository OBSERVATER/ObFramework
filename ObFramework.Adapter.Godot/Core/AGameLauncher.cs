using System;
using System.Threading.Tasks;
using Godot;
using ObFramework.Core;

namespace ObFramework.Adapter.Godot.Core
{
    public abstract partial class AGameLauncher : AViewController
    {
        private GodotRuntimeHost? _runtime;

        protected abstract PluginApplication CreateApplication();
        protected virtual GodotRuntimeHost CreateRuntimeHost() => new GodotRuntimeHost { Name = "ObFrameworkRuntime" };

        public override IArchitecture GetArchitecture() => _runtime?.Application?.Architecture
            ?? throw new InvalidOperationException("Application is not initialized.");
        public Task StopAsync() => _runtime?.StopAsync() ?? Task.CompletedTask;

        public override void _Ready()
        {
            base._Ready();
            // The root can still be adding the Bootstrap scene during _Ready.
            Callable.From(() => { _ = LaunchAsync(); }).CallDeferred();
        }

        private async Task LaunchAsync()
        {
            try
            {
                if (!IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion())
                    return;

                var tree = GetTree();
                if (GodotRuntimeHost.Find(tree) != null)
                    return;

                var runtime = CreateRuntimeHost();
                runtime.Attach(tree, this);
                _runtime = runtime;
                await runtime.StartAsync(CreateApplication);
            }
            catch (Exception error) { GD.PushError(error.ToString()); }
        }
    }
}
