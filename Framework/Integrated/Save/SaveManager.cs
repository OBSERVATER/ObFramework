using MessagePack;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Integrated.Save
{
    public interface ISaveManager
    {
        Task LoadAsync(string slotId);
        Task SaveAsync(string slotId);
    }
    internal class SaveManager : ISaveManager
    {
        private readonly ISaveStore _store;
        private readonly Dictionary<string, ICanSave> _targets;
        private readonly int Version = 1;
        private readonly SemaphoreSlim _gate = new(1, 1);
        readonly Dictionary<int, ISaveMigration> _migrations;
        public SaveManager(ISaveStore store, Dictionary<string, ICanSave> targets,int version = 1, Dictionary<int, ISaveMigration> migrations = null)
        {
            _store = store;
            _targets = new Dictionary<string, ICanSave>(targets);
            _migrations = migrations;
            Version = version;
        }

        public async Task LoadAsync(string slotId)
        {
            await _gate.WaitAsync();

            try
            {
                var raw = await _store.TryReadAsync(slotId);
                if (raw == null || raw.Length == 0)
                {
                    throw new FileNotFoundException($"Save slot not found or empty: {slotId}");
                }
                var slot = MessagePackSerializer.Deserialize<SaveSlot>(raw);
                if (slot.Version > Version)
                {
                    throw new InvalidDataException("future version.");
                }
                while (slot.Version < Version)
                {
                    if (!_migrations.TryGetValue(slot.Version, out var migration) || migration == null)
                    {
                        throw new InvalidDataException($"Missing migration from version {slot.Version}");
                    }
                    migration.Migrate(slot.Data);
                    slot.Version++;
                }
                // 想办法恢复数据
                var restored = new List<(ICanSave Target, ASaveData Data)>();
                foreach (var kvp in slot.Data)
                {
                    if (!_targets.TryGetValue(kvp.Key, out var target))
                        continue;

                    var data = MessagePackSerializer.Deserialize(target.SaveDataType, kvp.Value) as ASaveData
                        ?? throw new InvalidDataException($"Invalid save data: {kvp.Key}");
                    restored.Add((target, data));
                }

                foreach (var (Target, Data) in restored)
                {
                    Target.Restore(Data);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SaveAsync(string slotId)
        {
            await _gate.WaitAsync();

            try
            {
                var dict = new Dictionary<string, byte[]>();
                foreach (var kvp in _targets)
                {
                    ICanSave target = kvp.Value;
                    var o = target.Capture();
                    var bytes = MessagePackSerializer.Serialize(target.SaveDataType, o);
                    dict.Add(kvp.Key, bytes);
                }
                var slot = new SaveSlot()
                {
                    Data = dict,
                    Version = Version
                };
                var slotBytes = MessagePackSerializer.Serialize(slot);
                await _store.WriteAsync(slotBytes, slotId);
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
