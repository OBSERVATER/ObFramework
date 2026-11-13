using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ObFramework.Integrated.Resource;

namespace ObFramework.Integrated.Config
{
    internal sealed class ConfigManager
    {
        private readonly ResourcePlugin _assets;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private IReadOnlyDictionary<Type, IReadOnlyDictionary<int, IConfig>> _configMap =
            new Dictionary<Type, IReadOnlyDictionary<int, IConfig>>();
        private object? _tables;
        private bool _stopped;

        public ConfigManager(ResourcePlugin assets) => _assets = assets;

        public async Task LoadAllAsync(IConfigCatalog catalog, CancellationToken ct = default)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            await _gate.WaitAsync(ct);
            try
            {
                EnsureRunning();
                var map = new Dictionary<Type, IReadOnlyDictionary<int, IConfig>>();
                object? tables = null;
                if (catalog is ILubanConfigCatalog luban)
                {
                    var json = new Dictionary<string, JArray>();
                    foreach (var entry in catalog.Entries)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var handle = await _assets.LoadRawFileAsync(entry.Location, ct);
                        json.Add(entry.Name ?? throw new InvalidOperationException("Luban config entry needs a table name."),
                            JArray.Parse(handle.Get<string>()!));
                    }
                    tables = luban.CreateTables(name => json[name]);
                    foreach (var entry in luban.CreateConfigMap(tables))
                        map.Add(entry.Key, entry.Value);
                }
                else
                {
                    foreach (var entry in catalog.Entries)
                    {
                        ct.ThrowIfCancellationRequested();
                        var values = new Dictionary<int, IConfig>();
                        using var handle = await _assets.LoadRawFileAsync(entry.Location, ct);
                        var list = JsonConvert.DeserializeObject(handle.Get<string>()!, typeof(List<>).MakeGenericType(entry.Type)) as IEnumerable<IConfig>
                            ?? throw new JsonSerializationException($"Invalid configuration: {entry.Location}");
                        foreach (var value in list)
                            values.Add(value.Id, value);
                        map.Add(entry.Type, values);
                    }
                }
                ct.ThrowIfCancellationRequested();
                EnsureRunning();
                _configMap = map;
                _tables = tables;
            }
            finally { _gate.Release(); }
        }

        public T GetTables<T>() where T : class
        {
            EnsureRunning();
            return _tables as T ?? throw new InvalidOperationException($"Config tables are not {typeof(T).FullName}.");
        }

        public IReadOnlyList<T> Get<T>() where T : class, IConfig
        {
            EnsureRunning();
            return _configMap.TryGetValue(typeof(T), out var values) ? values.Values.Cast<T>().ToList() : Array.Empty<T>();
        }

        public T? Get<T>(int id) where T : class, IConfig
        {
            EnsureRunning();
            return _configMap.TryGetValue(typeof(T), out var values) && values.TryGetValue(id, out var value) ? (T)value : null;
        }

        internal async Task StopAsync()
        {
            _stopped = true;
            await _gate.WaitAsync();
            try
            {
                _configMap = new Dictionary<Type, IReadOnlyDictionary<int, IConfig>>();
                _tables = null;
            }
            finally { _gate.Release(); }
        }

        private void EnsureRunning()
        {
            if (_stopped)
                throw new ObjectDisposedException(nameof(ConfigManager));
        }
    }
}
