using ObFramework.Integrated.Resource.Handle;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ObFramework.Integrated.Resource
{
    
    public interface IAssetManager
    {
        IHandle LoadAsset<T>(string location);
        Task<IHandle> LoadAssetAsync<T>(string location);
        Task<RawFileHandle> LoadRawFileAsync(string location);
    }
    public class AssetManager : IAssetManager
    {
        private const string AssetMethod = "Asset";
        private const string RawFileMethod = "RawFile";

        private readonly Dictionary<AssetKey, AssetEntry> _handlerMap = new Dictionary<AssetKey, AssetEntry>();

        private readonly IAssetAdapter _adapter;
        private readonly object _gate = new object();

        public AssetManager(IAssetAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public IHandle LoadAsset<T>(string location)
        {
            var key = new AssetKey(location, typeof(T), AssetMethod);

            AssetEntry entry;
            TaskCompletionSource<AssetLoadResult> source;

            lock (_gate)
            {
                if (_handlerMap.TryGetValue(key, out entry))
                {
                    if (entry.LoadingTask != null)
                        throw new InvalidOperationException($"Asset is loading asynchronously: {location}");

                    entry.RefCount++;
                    return new AssetHandle(this, entry);
                }

                source = new TaskCompletionSource<AssetLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);

                entry = new AssetEntry
                {
                    Key = key,
                    RefCount = 1,
                    LoadingTask = source
                };

                _handlerMap.Add(key, entry);
            }

            AssetLoadResult result = default;

            try
            {
                result = _adapter.Read(key);

                if (result.Asset == null || result.Handle == null)
                    throw new InvalidOperationException($"Failed to load asset: {location}");

                lock (_gate)
                {
                    entry.Asset = result.Asset;
                    entry.Handle = result.Handle;
                    entry.LoadingTask = null;
                }

                source.TrySetResult(result);
                return new AssetHandle(this, entry);
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _handlerMap.Remove(key);
                    entry.LoadingTask = null;
                    entry.RefCount = 0;
                }

                try
                {
                    result.Handle?.Dispose();
                }
                catch (Exception releaseError)
                {
                    ex = new AggregateException(ex, releaseError);
                }

                source.TrySetException(ex);
                // 也观察没有其他等待者时的失败任务
                _ = source.Task.Exception;
                throw;
            }
        }

        public async Task<IHandle> LoadAssetAsync<T>(string location)
        {
            var key = new AssetKey(location, typeof(T), AssetMethod);
            var entry = await LoadEntryAsync(key, () => _adapter.ReadAsync(key));
            return new AssetHandle(this, entry);
        }

        public async Task<RawFileHandle> LoadRawFileAsync(string location)
        {
            if (_adapter is not IRawFileAdapter rawAdapter)
                throw new NotSupportedException("Current adapter does not support raw files.");

            var key = new AssetKey(location, typeof(byte[]), RawFileMethod);
            var entry = await LoadEntryAsync(key, () => rawAdapter.ReadRawFileAsync(location));
            return new RawFileHandle(this, entry);
        }

        private async Task<AssetEntry> LoadEntryAsync(AssetKey key, Func<Task<AssetLoadResult>> loader)
        {
            AssetEntry entry;
            Task<AssetLoadResult> loadingTask;
            bool needLoad = false;

            lock (_gate)
            {
                if (!_handlerMap.TryGetValue(key, out entry))
                {
                    entry = new AssetEntry
                    {
                        Key = key,
                        LoadingTask = new TaskCompletionSource<AssetLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously)
                    };

                    _handlerMap.Add(key, entry);
                    needLoad = true;
                }

                entry.RefCount++;
                loadingTask = entry.LoadingTask?.Task;
            }

            if (needLoad)
                _ = CompleteLoadAsync(entry, loader);

            if (loadingTask != null)
                await loadingTask;

            return entry;
        }

        private async Task CompleteLoadAsync(AssetEntry entry, Func<Task<AssetLoadResult>> loader)
        {
            var source = entry.LoadingTask;
            AssetLoadResult result = default;

            try
            {
                result = await loader();
                bool valid = entry.Key.Operation == RawFileMethod ? result.Asset is byte[] : result.Asset != null && result.Handle != null;

                if (!valid)
                    throw new InvalidOperationException($"Failed to load resource: {entry.Key.Location}");

                lock (_gate)
                {
                    entry.Asset = result.Asset;
                    entry.Handle = result.Handle;
                    entry.LoadingTask = null;
                }

                source.TrySetResult(result);
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _handlerMap.Remove(entry.Key);
                    entry.LoadingTask = null;
                    entry.RefCount = 0;
                }

                try
                {
                    result.Handle?.Dispose();
                }
                catch (Exception releaseError)
                {
                    ex = new AggregateException(ex, releaseError);
                }

                source.TrySetException(ex);
            }
        }

        internal void ReleaseEntry(AssetEntry entry)
        {
            IHandle source = null;

            lock (_gate)
            {
                if (!_handlerMap.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry))
                    return;

                if (--entry.RefCount == 0)
                {
                    _handlerMap.Remove(entry.Key);

                    source = entry.Handle;
                    entry.Handle = null;
                    entry.Asset = null;
                }
            }

            source?.Dispose();
        }
    }
}
