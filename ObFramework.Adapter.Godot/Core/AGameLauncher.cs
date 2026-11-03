using System;
using System.Threading.Tasks;
using Godot;

namespace ObFramework.Adapter.Godot.Core
{
    public abstract partial class AGameLauncher : AViewController
    {
        public override void _Ready()
        {
            _ = RunLaunchAsync();
        }

        private async Task RunLaunchAsync()
        {
            try
            {
                await LaunchAsync();
            }
            catch (Exception e)
            {
                GD.PushError(e.ToString());
            }
        }

        protected virtual async Task LaunchAsync()
        {
            await InitResourcesAsync();
            await InitConfigAsync();
            await InitGameSystemsAsync();
            await EnterGameAsync();
        }
        protected abstract Task InitResourcesAsync();
        protected abstract Task InitConfigAsync();
        protected abstract Task InitGameSystemsAsync();
        protected abstract Task EnterGameAsync();
    }
}