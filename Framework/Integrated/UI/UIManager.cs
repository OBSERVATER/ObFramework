using ObFramework.Utility.Collection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Integrated.UI
{
    internal sealed class UIManager : IDisposable
    {
        private readonly IUIAdapter _adapter;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<Type, List<UIKey>> _active = new();
        private readonly LRUCache<int, UIKey> _cache;
        private readonly Dictionary<Type, List<int>> _idCacheMap = new();
        private readonly Stack<UIStackEntry> _stack = new();
        private int _serialId;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private bool _stopping;

        public UIManager(IUIAdapter adapter, int cacheCapacity = 16)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _cache = new LRUCache<int, UIKey>(cacheCapacity, OnCacheRemoved);
        }

        private int NextSerialId => checked(++_serialId);

        private void EnsureRunning()
        {
            if (_stopping)
                throw new ObjectDisposedException(nameof(UIManager));
        }

        internal async Task StopAsync()
        {
            if (_stopping)
                return;
            _stopping = true;
            var errors = new List<Exception>();
            try { _shutdown.Cancel(); }
            catch (Exception error) { errors.Add(error); }
            await _gate.WaitAsync();
            try
            {
                var windows = _active.Values.SelectMany(x => x).ToArray();
                _active.Clear();
                _stack.Clear();
                foreach (var key in windows)
                {
                    try { _adapter.Close(key.Instance); }
                    catch (Exception error) { errors.Add(error); }
                    try { _adapter.Release(key.Instance); }
                    catch (Exception error) { errors.Add(error); }
                }
                while (_cache.Count > 0)
                {
                    try { _cache.Clear(); }
                    catch (Exception error) { errors.Add(error); }
                }
                _idCacheMap.Clear();
            }
            finally { _gate.Release(); }
            if (errors.Count > 0)
                throw new AggregateException("UI shutdown failed.", errors);
        }

        public void Dispose() => _shutdown.Dispose();

        #region Open

        public async Task<T?> OpenAsync<T>(EUIOpenMode mode = EUIOpenMode.Single, IUIData? data = null, CancellationToken cancellationToken = default) where T : class, IUIWindow
            => await OpenAsync(typeof(T), mode, cancellationToken, data) as T;

        private async Task<IUIWindow?> OpenAsync(Type type, EUIOpenMode mode = EUIOpenMode.Single, CancellationToken cancellationToken = default, IUIData? data = null)
        {
            EnsureRunning();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            cancellationToken = linked.Token;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (mode != EUIOpenMode.Single && mode != EUIOpenMode.Multiple)
                    throw new ArgumentOutOfRangeException(nameof(mode));

                if (mode == EUIOpenMode.Single && TryGetLatestActive(type, out var activeKey))
                {
                    try
                    {
                        _adapter.Open(activeKey.Instance, data);
                    }
                    catch
                    {
                        RemoveActive(activeKey);
                        _adapter.Release(activeKey.Instance);
                        throw;
                    }

                    _active[type][^1] = new UIKey(activeKey.Type, activeKey.Instance, activeKey.SerialId, activeKey.Mode, data);
                    return activeKey.Instance;
                }

                int serialId = NextSerialId;
                IUIWindow instance;

                if (TryTakeCached(type, out var cachedKey))
                {
                    instance = cachedKey.Instance;
                }
                else
                {
                    instance = await _adapter.CreateAsync(type, cancellationToken);
                }

                if (instance == null)
                    throw new InvalidOperationException($"Failed to create UI: {type.FullName}");

                var key = new UIKey(type, instance, serialId, mode, data);
                bool added = false;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddActive(key);
                    added = true;
                    _adapter.Open(instance, data);
                    return instance;
                }
                catch
                {
                    if (added)
                        RemoveActive(key);

                    _adapter.Release(instance);
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        #endregion

        #region Close

        public bool Close(IUIWindow? instance)
        {
            EnsureRunning();
            if (instance == null)
                return false;

            return TryFindActive(instance, out var key) && MoveToCache(key);
        }

        public bool Close<T>() where T : class, IUIWindow
        {
            EnsureRunning();
            return TryGetLatestActive(typeof(T), out var key) && MoveToCache(key);
        }

        public int CloseAll<T>() where T : class, IUIWindow
        {
            EnsureRunning();
            if (!_active.TryGetValue(typeof(T), out var keys))
                return 0;

            var snapshot = keys.ToArray();
            return snapshot.Count(MoveToCache);
        }

        #endregion

        #region Query

        public bool TryGet<T>(out T? instance) where T : class, IUIWindow
        {
            EnsureRunning();
            instance = null;
            if (!TryGetLatestActive(typeof(T), out var key))
                return false;

            instance = key.Instance as T;
            return instance != null;
        }

        private bool TryGetLatestActive(Type type, out UIKey key)
        {
            if (_active.TryGetValue(type, out var list) && list.Count > 0)
            {
                key = list[^1];
                return true;
            }

            key = default;
            return false;
        }

        private bool TryFindActive(IUIWindow instance, out UIKey key)
        {
            foreach (var pair in _active)
            {
                foreach (var item in pair.Value)
                {
                    if (!ReferenceEquals(item.Instance, instance))
                        continue;

                    key = item;
                    return true;
                }
            }

            key = default;
            return false;
        }

        #endregion

        #region Active

        private void AddActive(UIKey key)
        {
            if (key.Type == null || key.Instance == null || key.SerialId <= 0)
                throw new ArgumentException("Invalid UIKey.");

            foreach (var pair in _active)
            {
                foreach (var item in pair.Value)
                {
                    if (item.SerialId == key.SerialId || ReferenceEquals(item.Instance, key.Instance))
                        throw new InvalidOperationException("Duplicate active UI instance.");
                }
            }

            if (!_active.TryGetValue(key.Type, out var list))
            {
                list = new List<UIKey>();
                _active.Add(key.Type, list);
            }

            list.Add(key);
        }

        private bool RemoveActive(UIKey key)
        {
            if (!_active.TryGetValue(key.Type, out var list))
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item.SerialId != key.SerialId || !ReferenceEquals(item.Instance, key.Instance))
                    continue;

                list.RemoveAt(i);

                if (list.Count == 0)
                    _active.Remove(key.Type);

                return true;
            }

            return false;
        }

        #endregion

        #region Cache

        private bool MoveToCache(UIKey key)
        {
            if (!RemoveActive(key))
                return false;

            try
            {
                _adapter.Close(key.Instance);
                AddCachedIndex(key);
                _cache.Add(key.SerialId, key);
                return true;
            }
            catch
            {
                _cache.Remove(key.SerialId, false);
                RemoveCachedIndex(key.SerialId, key.Type);
                _adapter.Release(key.Instance);
                throw;
            }
        }

        private bool TryTakeCached(Type type, out UIKey key)
        {
            key = default;
            if (!_idCacheMap.TryGetValue(type, out var ids))
                return false;

            while (ids.Count > 0)
            {
                int id = ids[^1];

                if (_cache.TryTake(id, out key))
                {
                    RemoveCachedIndex(id, type);
                    return true;
                }

                RemoveCachedIndex(id, type);
            }

            return false;
        }

        private void AddCachedIndex(UIKey key)
        {
            if (!_idCacheMap.TryGetValue(key.Type, out var list))
            {
                list = new List<int>();
                _idCacheMap.Add(key.Type, list);
            }

            list.Add(key.SerialId);
        }

        private void RemoveCachedIndex(int id, Type type)
        {
            if (!_idCacheMap.TryGetValue(type, out var list))
                return;

            list.Remove(id);

            if (list.Count == 0)
                _idCacheMap.Remove(type);
        }

        private void OnCacheRemoved(int id, UIKey key)
        {
            RemoveCachedIndex(id, key.Type);
            _adapter.Release(key.Instance);
        }

        #endregion

        #region Stack

        public void Push<T>(T ui) where T : IUIWindow
        {
            EnsureRunning();
            if (ui.Level != EUILevel.Common)
                throw new InvalidOperationException("Only Common windows can be pushed onto the stack.");

            if (!TryFindActive(ui, out var key))
                return;

            var entry = new UIStackEntry(key.Type, key.Data, key.Mode);

            if (MoveToCache(key))
                _stack.Push(entry);
        }

        public async Task<T?> Pop<T>() where T : class, IUIWindow
        {
            EnsureRunning();
            if (!_stack.TryPeek(out var entry))
                return null;

            if (!typeof(T).IsAssignableFrom(entry.PanelType))
                throw new InvalidOperationException($"Stack top is {entry.PanelType.Name}, not {typeof(T).Name}.");

            var panel = await OpenAsync(entry.PanelType, entry.Mode, data: entry.Data);
            if (panel == null)
                return null;

            EnsureRunning();
            _stack.Pop();
            return (T)panel;
        }

        #endregion
    }
}