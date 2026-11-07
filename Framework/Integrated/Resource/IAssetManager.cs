using ObFramework.Integrated.Resource.Handle;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace ObFramework.Integrated.Resource
{
    public interface IAssetManager
    {
        IHandle LoadAsset<T>(string location) where T : class;
        Task<IHandle> LoadAssetAsync<T>(string location, CancellationToken ct = default) where T : class;
        Task<RawFileHandle> LoadRawFileAsync(string location, CancellationToken ct = default);
    }

    public class AssetManager<TNativeHandle> : IAssetManager, IAssetEntryOwner, IDisposable
    {
        private const string AssetMethod = "Asset";
        private const string RawFileMethod = "RawFile";

        private readonly Dictionary<AssetKey, AssetEntry<TNativeHandle>> _handlerMap = new();
        private readonly IAssetAdapter<TNativeHandle> _adapter;
        private readonly object _gate = new object();
        private readonly HashSet<Task> _loads = new HashSet<Task>();
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private bool _stopping;

        public AssetManager(IAssetAdapter<TNativeHandle> adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public IHandle LoadAsset<T>(string location) where T : class
        {
            var key = new AssetKey(location, typeof(T));
            AssetEntry<TNativeHandle> entry;
            TaskCompletionSource<bool> source;

            lock (_gate)
            {
                EnsureRunning();
                if (_handlerMap.TryGetValue(key, out entry))
                {
                    if (entry.LoadingTask != null || !entry.HasNativeHandle)
                        throw new InvalidOperationException($"Asset is loading asynchronously: {location}");

                    entry.RefCount++;
                    return new AssetHandle(this, entry);
                }

                source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                entry = new AssetEntry<TNativeHandle> { Key = key, RefCount = 1, LoadingTask = source };
                _handlerMap.Add(key, entry!);
                _loads.Add(source.Task);
            }

            AssetLoadResult<T, TNativeHandle> result = default;

            try
            {
                result = _adapter.Read<T>(key);

                if (result.Asset == null || !result.HasNativeHandle)
                    throw new InvalidOperationException($"Failed to load asset: {location}");

                lock (_gate)
                {
                    EnsureRunning();
                    entry.Asset = result.Asset;
                    entry.NativeHandle = result.NativeHandle;
                    entry.HasNativeHandle = result.HasNativeHandle;
                    entry.LoadingTask = null;
                }

                source.TrySetResult(true);
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

                Exception error = ex;

                if (result.HasNativeHandle)
                {
                    try
                    {
                        _adapter.Release(result.NativeHandle);
                    }
                    catch (Exception releaseError)
                    {
                        error = new AggregateException(ex, releaseError);
                    }
                }

                source.TrySetException(error);
                _ = source.Task.Exception;

                if (!ReferenceEquals(error, ex))
                    throw error;

                throw;
            }
            finally
            {
                lock (_gate)
                    _loads.Remove(source.Task);
            }
        }

        public async Task<IHandle> LoadAssetAsync<T>(string location, CancellationToken ct = default) where T : class
        {
            var key = new AssetKey(location, typeof(T), AssetMethod);
            ct.ThrowIfCancellationRequested();
            var entry = await LoadEntryAsync(key, () => _adapter.ReadAsync<T>(key, _shutdown.Token));
            CheckCancellation(entry, ct);
            return new AssetHandle(this, entry);
        }

        public async Task<RawFileHandle> LoadRawFileAsync(string location, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_adapter is not IRawFileAdapter<TNativeHandle> rawAdapter)
                throw new NotSupportedException("Current adapter does not support raw files.");

            var key = new AssetKey(location, typeof(byte[]), RawFileMethod);
            var entry = await LoadEntryAsync(key, () => rawAdapter.ReadRawFileAsync(location));
            CheckCancellation(entry, ct);
            return new RawFileHandle(this, entry);
        }

        private async Task<AssetEntry<TNativeHandle>> LoadEntryAsync<T>(AssetKey key, Func<Task<AssetLoadResult<T, TNativeHandle>>> loader) where T : class
        {
            AssetEntry<TNativeHandle> entry;
            Task loadingTask;
            bool needLoad = false;

            lock (_gate)
            {
                EnsureRunning();
                if (!_handlerMap.TryGetValue(key, out entry))
                {
                    entry = new AssetEntry<TNativeHandle>
                    {
                        Key = key,
                        LoadingTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                    };

                    _handlerMap.Add(key, entry);
                    _loads.Add(entry.LoadingTask.Task);
                    needLoad = true;
                }

                entry.RefCount++;
                loadingTask = entry.LoadingTask?.Task;
            }

            if (needLoad)
                _ = CompleteLoadAsync(entry, loader);

            if (loadingTask != null)
                await loadingTask;

            lock (_gate)
                EnsureRunning();
            return entry;
        }

        private async Task CompleteLoadAsync<T>(AssetEntry<TNativeHandle> entry, Func<Task<AssetLoadResult<T, TNativeHandle>>> loader) where T : class
        {
            var source = entry.LoadingTask;
            AssetLoadResult<T, TNativeHandle> result = default;

            try
            {
                result = await loader();
                if (result.Asset == null)
                    throw new InvalidOperationException($"Failed to load resource: {entry.Key.Location}");

                lock (_gate)
                {
                    entry.Asset = result.Asset;
                    entry.NativeHandle = result.NativeHandle;
                    entry.HasNativeHandle = result.HasNativeHandle;
                    entry.LoadingTask = null;
                }

                source.TrySetResult(true);
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _handlerMap.Remove(entry.Key);
                    entry.LoadingTask = null;
                    entry.RefCount = 0;
                }

                Exception error = ex;

                if (result.HasNativeHandle)
                {
                    try
                    {
                        _adapter.Release(result.NativeHandle);
                    }
                    catch (Exception releaseError)
                    {
                        error = new AggregateException(ex, releaseError);
                    }
                }

                source.TrySetException(error);
            }
            finally
            {
                lock (_gate)
                    _loads.Remove(source.Task);
            }
        }

        private void CheckCancellation(AssetEntry<TNativeHandle> entry, CancellationToken ct)
        {
            if (!ct.IsCancellationRequested)
                return;
            ReleaseEntry(entry);
            ct.ThrowIfCancellationRequested();
        }

        private void EnsureRunning()
        {
            if (_stopping)
                throw new ObjectDisposedException(nameof(AssetManager<TNativeHandle>));
        }

        internal async Task StopAsync()
        {
            Task[] pending;
            lock (_gate)
            {
                if (_stopping)
                    return;
                _stopping = true;
                pending = _loads.ToArray();
            }
            var errors = new List<Exception>();
            try { _shutdown.Cancel(); }
            catch (Exception error) { errors.Add(error); }
            try { await Task.WhenAll(pending); }
            catch (Exception)
            {
                foreach (var task in pending)
                    if (task.Exception != null)
                        errors.AddRange(task.Exception.Flatten().InnerExceptions.Where(x => x is not OperationCanceledException && x is not ObjectDisposedException));
            }
            AssetEntry<TNativeHandle>[] entries;
            lock (_gate)
            {
                entries = _handlerMap.Values.ToArray();
                _handlerMap.Clear();
            }
            foreach (var entry in entries)
            {
                var handle = entry.NativeHandle;
                var release = entry.HasNativeHandle;
                entry.NativeHandle = default;
                entry.HasNativeHandle = false;
                entry.Asset = null!;
                entry.RefCount = 0;
                if (release)
                {
                    try { _adapter.Release(handle); }
                    catch (Exception error) { errors.Add(error); }
                }
            }
            if (errors.Count > 0)
                throw new AggregateException("Resource shutdown failed.", errors);
        }

        public void Dispose() => _shutdown.Dispose();

        void IAssetEntryOwner.ReleaseEntry(AssetEntry entry)
        {
            if (entry is not AssetEntry<TNativeHandle> typedEntry)
                throw new ArgumentException("Invalid AssetEntry type.", nameof(entry));

            ReleaseEntry(typedEntry);
        }

        internal void ReleaseEntry(AssetEntry<TNativeHandle> entry)
        {
            TNativeHandle nativeHandle;
            bool shouldRelease;

            lock (_gate)
            {
                if (!_handlerMap.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry))
                    return;

                if (entry.RefCount <= 0)
                    throw new InvalidOperationException($"Invalid asset reference count: {entry.Key.Location}");

                if (--entry.RefCount != 0)
                    return;

                _handlerMap.Remove(entry.Key);
                nativeHandle = entry.NativeHandle;
                shouldRelease = entry.HasNativeHandle;
                entry.NativeHandle = default;
                entry.HasNativeHandle = false;
                entry.Asset = null;
            }

            if (shouldRelease)
                _adapter.Release(nativeHandle);
        }
    }
}