using System;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;

namespace ObFramework.Integrated.Resource
{
    public sealed class ResourcePlugin<TNativeHandle> : IPlugin
    {
        private readonly IAssetAdapter<TNativeHandle> _adapter;
        public ResourcePlugin(IAssetAdapter<TNativeHandle> adapter) => _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        public void Register(PluginRegistration registration)
        {
            registration.RegisterInstance<IAssetAdapter<TNativeHandle>>(_adapter);
            registration.Export<IAssetManager, AssetManager<TNativeHandle>>();
        }
        public Task InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            plugins.Get<IAssetManager>();
            return Task.CompletedTask;
        }
        public Task StopAsync(IPluginContext plugins) => ((AssetManager<TNativeHandle>)plugins.Get<IAssetManager>()).StopAsync();
    }
}
