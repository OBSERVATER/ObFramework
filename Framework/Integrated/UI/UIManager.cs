using ObFramework.Utility.Collection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Integrated.UI
{
    /// <summary>
    /// Owns managed UI state: Single/Multiple activation, the Common-only navigation stack and the
    /// LRU cache that keeps the VC/View pair together. It drives the business lifecycle on
    /// <see cref="AUIView"/> while the adapter owns native objects and the binding layer owns the
    /// final View dispose.
    /// </summary>
    internal sealed class UIManager : IDisposable
    {
        private readonly IUIAdapter _adapter;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<Type, List<UIKey>> _active = new Dictionary<Type, List<UIKey>>();
        private readonly LRUCache<int, UIKey> _cache;
        private readonly Dictionary<Type, List<int>> _idCacheMap = new Dictionary<Type, List<int>>();
        private readonly Stack<UIStackEntry> _stack = new Stack<UIStackEntry>();
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private int _serialId;
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
                    try { key.Instance.View.CloseView(); }
                    catch (Exception error) { errors.Add(error); }
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

        public async Task<T?> OpenAsync<T>(EUIOpenMode mode = EUIOpenMode.Single, IUIData? data = null, CancellationToken cancellationToken = default)
            where T : AUIView
            => await OpenAsync(typeof(T), mode, cancellationToken, data) as T;

        private async Task<AUIView?> OpenAsync(Type viewType, EUIOpenMode mode = EUIOpenMode.Single, CancellationToken cancellationToken = default, IUIData? data = null)
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

                if (mode == EUIOpenMode.Single && TryGetLatestActive(viewType, out var activeKey))
                {
                    try
                    {
                        activeKey.Instance.View.OpenView(data);
                        _adapter.Open(activeKey.Instance, data);
                    }
                    catch
                    {
                        RemoveActive(activeKey);
                        _adapter.Release(activeKey.Instance);
                        throw;
                    }

                    _active[viewType][^1] = new UIKey(activeKey.ViewType, activeKey.Instance, activeKey.SerialId, activeKey.Mode, data);
                    return activeKey.Instance.View;
                }

                int serialId = NextSerialId;
                UIViewInstance instance;

                if (TryTakeCached(viewType, out var cachedKey))
                {
                    instance = cachedKey.Instance;
                }
                else
                {
                    instance = await _adapter.CreateAsync(viewType, cancellationToken)
                        ?? throw new InvalidOperationException($"Failed to create UI view: {viewType.FullName}");
                }

                var key = new UIKey(viewType, instance, serialId, mode, data);
                bool added = false;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    instance.View.InitView(data);
                    AddActive(key);
                    added = true;
                    instance.View.OpenView(data);
                    _adapter.Open(instance, data);
                    return instance.View;
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

        public bool Close(AUIView? view)
        {
            EnsureRunning();
            if (view == null)
                return false;

            return TryFindActive(view, out var key) && MoveToCache(key);
        }

        public bool Close<T>() where T : AUIView
        {
            EnsureRunning();
            return TryGetLatestActive(typeof(T), out var key) && MoveToCache(key);
        }

        public int CloseAll<T>() where T : AUIView
        {
            EnsureRunning();
            if (!_active.TryGetValue(typeof(T), out var keys))
                return 0;

            var snapshot = keys.ToArray();
            return snapshot.Count(MoveToCache);
        }

        #endregion

        #region Query

        public bool TryGet<T>(out T? view) where T : AUIView
        {
            EnsureRunning();
            view = null;
            if (!TryGetLatestActive(typeof(T), out var key))
                return false;

            view = key.Instance.View as T;
            return view != null;
        }

        private bool TryGetLatestActive(Type viewType, out UIKey key)
        {
            if (_active.TryGetValue(viewType, out var list) && list.Count > 0)
            {
                key = list[^1];
                return true;
            }

            key = default;
            return false;
        }

        private bool TryFindActive(AUIView view, out UIKey key)
        {
            foreach (var pair in _active)
            {
                foreach (var item in pair.Value)
                {
                    if (!ReferenceEquals(item.Instance.View, view))
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
            if (key.ViewType == null || !key.Instance.IsValid || key.SerialId <= 0)
                throw new ArgumentException("Invalid UIKey.");

            foreach (var pair in _active)
            {
                foreach (var item in pair.Value)
                {
                    if (item.SerialId == key.SerialId ||
                        ReferenceEquals(item.Instance.Window, key.Instance.Window) ||
                        ReferenceEquals(item.Instance.View, key.Instance.View))
                        throw new InvalidOperationException("Duplicate active UI instance.");
                }
            }

            if (!_active.TryGetValue(key.ViewType, out var list))
            {
                list = new List<UIKey>();
                _active.Add(key.ViewType, list);
            }

            list.Add(key);
        }

        private bool RemoveActive(UIKey key)
        {
            if (!_active.TryGetValue(key.ViewType, out var list))
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item.SerialId != key.SerialId)
                    continue;

                list.RemoveAt(i);

                if (list.Count == 0)
                    _active.Remove(key.ViewType);

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
                key.Instance.View.CloseView();
                _adapter.Close(key.Instance);
                AddCachedIndex(key);
                _cache.Add(key.SerialId, key);
                return true;
            }
            catch
            {
                _cache.Remove(key.SerialId, false);
                RemoveCachedIndex(key.SerialId, key.ViewType);
                _adapter.Release(key.Instance);
                throw;
            }
        }

        private bool TryTakeCached(Type viewType, out UIKey key)
        {
            key = default;
            if (!_idCacheMap.TryGetValue(viewType, out var ids))
                return false;

            while (ids.Count > 0)
            {
                int id = ids[^1];

                if (_cache.TryTake(id, out key))
                {
                    RemoveCachedIndex(id, viewType);
                    return true;
                }

                RemoveCachedIndex(id, viewType);
            }

            return false;
        }

        private void AddCachedIndex(UIKey key)
        {
            if (!_idCacheMap.TryGetValue(key.ViewType, out var list))
            {
                list = new List<int>();
                _idCacheMap.Add(key.ViewType, list);
            }

            list.Add(key.SerialId);
        }

        private void RemoveCachedIndex(int id, Type viewType)
        {
            if (!_idCacheMap.TryGetValue(viewType, out var list))
                return;

            list.Remove(id);

            if (list.Count == 0)
                _idCacheMap.Remove(viewType);
        }

        private void OnCacheRemoved(int id, UIKey key)
        {
            RemoveCachedIndex(id, key.ViewType);
            _adapter.Release(key.Instance);
        }

        #endregion

        #region Stack

        public void Push<T>(T view) where T : AUIView
        {
            EnsureRunning();
            var value = view ?? throw new ArgumentNullException(nameof(view));
            if (!TryFindActive(value, out var key))
                return;

            if (key.Instance.Window.Level != EUILevel.Common)
                throw new InvalidOperationException("Only Common windows can be pushed onto the stack.");

            var entry = new UIStackEntry(key.ViewType, key.Data, key.Mode);

            if (MoveToCache(key))
                _stack.Push(entry);
        }

        public async Task<T?> Pop<T>() where T : AUIView
        {
            EnsureRunning();
            if (!_stack.TryPeek(out var entry))
                return null;

            if (!typeof(T).IsAssignableFrom(entry.ViewType))
                throw new InvalidOperationException($"Stack top is {entry.ViewType.Name}, not {typeof(T).Name}.");

            var view = await OpenAsync(entry.ViewType, entry.Mode, data: entry.Data);
            if (view == null)
                return null;

            EnsureRunning();
            _stack.Pop();
            return (T)view;
        }

        #endregion
    }
}
