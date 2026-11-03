using System;
using System.Threading.Tasks;
using ObFramework.Adapter.Unity.UI;
using ObFramework.Integrated.Diagnostics;
using Serilog;
using UnityEngine;

namespace ObFramework.Adapter.Unity.Core
{
    public abstract class AGameLauncher : AViewController
    {
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
        private async void Start()
        {
            try
            {
                await LaunchAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameLauncher] Launch failed:\n{e}", this);
            }
        }
    }
}