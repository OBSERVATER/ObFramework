using System;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;
using ObFramework.Integrated.Resource;

namespace ObFramework.Integrated.Config
{
    public sealed class ConfigPlugin : IPlugin
    {
        private readonly IConfigCatalog _catalog;
        public ConfigPlugin(IConfigCatalog catalog) => _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        public void Register(PluginRegistration registration)
        {
            registration.Require<IAssetManager>();
            registration.Export<IConfigManager, ConfigManager>();
        }
        public Task InitAsync(IPluginContext plugins, CancellationToken ct) => plugins.Get<IConfigManager>().LoadAllAsync(_catalog, ct);
        public Task StopAsync(IPluginContext plugins) => ((ConfigManager)plugins.Get<IConfigManager>()).StopAsync();
    }
}
