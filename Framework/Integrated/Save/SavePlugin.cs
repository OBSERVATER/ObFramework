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
        private SaveManager? _manager;
        private SaveManager Manager => _manager ?? throw new InvalidOperationException("Save plugin is not initialized.");

        public SavePlugin(ISaveStore store, int version = 1, Dictionary<int, ISaveMigration>? migrations = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _version = version;
            _migrations = migrations == null ? null : new Dictionary<int, ISaveMigration>(migrations);
        }

        public void Bind(IArchitecture architecture) => Manager.Bind(architecture);
        public Task UnbindAsync() => Manager.UnbindAsync();
        public Task LoadAsync(string slotId) => Manager.LoadAsync(slotId);
        public Task SaveAsync(string slotId) => Manager.SaveAsync(slotId);

        void IPlugin.Register(PluginRegistration registration) { }
        Task IPlugin.InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _manager = new SaveManager(_store, _version, _migrations);
            return Task.CompletedTask;
        }
        Task IPlugin.StopAsync(IPluginContext plugins) => _manager?.StopAsync() ?? Task.CompletedTask;
    }
}
