using System;
using System.Threading.Tasks;
using Godot;
using ObFramework.Core;

namespace ObFramework.Adapter.Godot.Core
{
    public abstract partial class AViewController : Node, IController
    {
        private bool _quitting;
        public abstract IArchitecture GetArchitecture();
        public override void _Ready()
        {
            GetTree().AutoAcceptQuit = false;
            base._Ready();
        }
        protected virtual Task ShutdownAsync()
        {
            return Task.CompletedTask;
        }

        public override void _Notification(int what)
        {
            if (what != NotificationWMCloseRequest || _quitting)
                return;

            _quitting = true;
            _ = QuitAsync();
        }

        private async Task QuitAsync()
        {
            try
            {
                await ShutdownAsync();
            }
            catch (Exception e)
            {
                GD.PushError(e.ToString());
            }
            finally
            {
                GetTree().Quit();
            }
        }

    }
}