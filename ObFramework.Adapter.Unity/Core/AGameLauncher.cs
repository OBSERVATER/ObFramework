using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ObFramework.Adapter.Unity.UI;
using ObFramework.Core;
using UnityEngine;

namespace ObFramework.Adapter.Unity.Core
{
    public abstract class AGameLauncher : AViewController
    {
        private UnityRuntimeHost? _runtime;

        protected abstract PluginApplication CreateApplication();
        protected virtual GameObject? GetGameManager() => null;
        protected virtual IEnumerable<GameObject> GetPersistentRoots() => Array.Empty<GameObject>();

        public override IArchitecture GetArchitecture() => _runtime!.Application!.Architecture;
        public Task ShutdownAsync() => _runtime?.ShutdownAsync() ?? Task.CompletedTask;

        private async void Start()
        {
            try
            {
                _runtime = UnityRuntimeHost.Reserve(gameObject, GetGameManager());
                var runtime = _runtime;
                await runtime.StartAsync(CreateApplication(),
                    new UnityFrameLoopBridge(views => runtime.Prepare(GetPersistentRoots(), views)));
            }
            catch (Exception error)
            {
                if (error is not OperationCanceledException || _runtime?.IsStopping != true)
                    Debug.LogError(error);
                if (_runtime is { IsStopping: false } runtime)
                    await runtime.StopAndObserveAsync();
            }
        }
    }
}
