using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;
using ObFramework.Integrated.Resource;

namespace ObFramework.Integrated.Config
{
    public sealed class ConfigPlugin : IPlugin
    {
        private readonly IConfigCatalog _catalog;
        private ConfigManager? _manager;
        private ConfigManager Manager => _manager ?? throw new InvalidOperationException("Config plugin is not initialized.");

        public ConfigPlugin(IConfigCatalog catalog) => _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        public Task LoadAllAsync(IConfigCatalog catalog, CancellationToken ct = default) => Manager.LoadAllAsync(catalog, ct);
        public T GetTables<T>() where T : class => Manager.GetTables<T>();
        public IReadOnlyList<T> Get<T>() where T : class, IConfig => Manager.Get<T>();
        public T? Get<T>(int id) where T : class, IConfig => Manager.Get<T>(id);

        void IPlugin.Register(PluginRegistration registration) => registration.Require<ResourcePlugin>();
        Task IPlugin.InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _manager = new ConfigManager(plugins.Get<ResourcePlugin>());
            return _manager.LoadAllAsync(_catalog, ct);
        }
        Task IPlugin.StopAsync(IPluginContext plugins) => _manager?.StopAsync() ?? Task.CompletedTask;
    }
}
