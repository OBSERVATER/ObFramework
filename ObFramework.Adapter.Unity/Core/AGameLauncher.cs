using System;
using System.Threading.Tasks;
using ObFramework.Adapter.Unity.UI;
using ObFramework.Core;
using UnityEngine;

namespace ObFramework.Adapter.Unity.Core
{
    public abstract class AGameLauncher : AViewController
    {
        private PluginApplication _application = null!;
        private bool _quitting;
        private bool _quitRequested;
        protected abstract PluginApplication CreateApplication();
        public override IArchitecture GetArchitecture() => _application.Architecture;
        public Task ShutdownAsync() => _application?.StopAsync() ?? Task.CompletedTask;

        private async void Start()
        {
            Application.wantsToQuit += WantsToQuit;
            try
            {
                _application = CreateApplication();
                await _application.StartAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogError(error); }
        }

        private bool WantsToQuit()
        {
            if (_quitting)
                return true;
            if (!_quitRequested)
            {
                _quitRequested = true;
                _ = QuitAsync();
            }
            return false;
        }

        private async Task QuitAsync()
        {
            await StopAndObserveAsync();
            _quitting = true;
            await Task.Yield();
            Application.Quit();
        }

        private void OnDestroy()
        {
            Application.wantsToQuit -= WantsToQuit;
            _ = StopAndObserveAsync();
        }

        private async Task StopAndObserveAsync()
        {
            try { await ShutdownAsync(); }
            catch (Exception error) { Debug.LogError(error); }
        }
    }
}
