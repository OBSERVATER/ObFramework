using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;

namespace ObFramework.Integrated.Save
{
    public sealed class SavePlugin : IPlugin
    {
        private readonly ISaveStore _store;
        private readonly int _version;
        private readonly Dictionary<int, ISaveMigration>? _migrations;
        public SavePlugin(ISaveStore store, int version = 1, Dictionary<int, ISaveMigration>? migrations = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _version = version;
            _migrations = migrations == null ? null : new Dictionary<int, ISaveMigration>(migrations);
        }
        public void Register(PluginRegistration registration)
        {
            registration.Export<ISaveManager, SaveManager>(_ => new SaveManager(_store, _version, _migrations));
            registration.Export<ISaveBinding, SaveManager>();
        }
        public Task InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            plugins.Get<ISaveManager>();
            return Task.CompletedTask;
        }
        public Task StopAsync(IPluginContext plugins) => ((SaveManager)plugins.Get<ISaveManager>()).StopAsync();
    }
}
