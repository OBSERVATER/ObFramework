using System;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;
using ObFramework.Integrated.Resource.Handle;

namespace ObFramework.Integrated.Resource
{
    public sealed class ResourcePlugin : IPlugin
    {
        private readonly Func<IAssetManager> _createManager;
        private IAssetManager? _manager;
        private IAssetManager Manager => _manager ?? throw new InvalidOperationException("Resource plugin is not initialized.");

        private ResourcePlugin(Func<IAssetManager> createManager) => _createManager = createManager;

        public static ResourcePlugin Create<TNativeHandle>(IAssetAdapter<TNativeHandle> adapter)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));
            return new ResourcePlugin(() => new AssetManager<TNativeHandle>(adapter));
        }

        public IHandle LoadAsset<T>(string location) where T : class => Manager.LoadAsset<T>(location);
        public Task<IHandle> LoadAssetAsync<T>(string location, CancellationToken ct = default) where T : class => Manager.LoadAssetAsync<T>(location, ct);
        public Task<RawFileHandle> LoadRawFileAsync(string location, CancellationToken ct = default) => Manager.LoadRawFileAsync(location, ct);

        void IPlugin.Register(PluginRegistration registration) { }
        Task IPlugin.InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _manager = _createManager();
            return Task.CompletedTask;
        }
        Task IPlugin.StopAsync(IPluginContext plugins) => _manager?.StopAsync() ?? Task.CompletedTask;
    }
}
