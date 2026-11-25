using System;
using System.Threading;
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
        protected virtual string? UIRootResourceKey => null;
        public abstract Task StartGameAsync(CancellationToken cancellationToken = default);

        public override IArchitecture GetArchitecture() => _runtime!.Application!.Architecture;

        private void Awake()
        {
            if (!enabled)
                return;
            try
            {
                if (UIRootResourceKey is { } key)
                    PrepareRuntime(key);
            }
            catch (Exception error)
            {
                enabled = false;
                _ = HandleStartupFailureAsync(error);
            }
        }

        private void Start() => _ = StartApplicationAsync();

        private async Task StartApplicationAsync()
        {
            try
            {
                if (!_runtime)
                    PrepareRuntime(UIRootResourceKey);
                var runtime = _runtime;
                await runtime!.StartAsync(CreateApplication(),
                    new UnityFrameLoopBridge(views => runtime.Prepare(Array.Empty<GameObject>(), views)),
                    StartGameAsync);
            }
            catch (Exception error)
            {
                await HandleStartupFailureAsync(error);
            }
        }

        private void PrepareRuntime(string? uiRootKey)
        {
            _runtime = UnityRuntimeHost.Reserve(gameObject, manager: null);
            if (uiRootKey != null)
                _runtime.LoadUIRoot(uiRootKey);
        }

        private async Task HandleStartupFailureAsync(Exception error)
        {
            if (error is not OperationCanceledException || _runtime?.IsStopping != true)
                Debug.LogError(error);
            if (_runtime is { IsStopping: false } runtime)
                await runtime.StopAndObserveAsync();
        }

        private void OnDestroy()
        {
            if (_runtime is { Application: null } runtime)
                _ = runtime.StopAndObserveAsync();
        }
    }
}
