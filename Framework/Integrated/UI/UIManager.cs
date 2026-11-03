using ObFramework.Utility.Collection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Integrated.UI
{
    public interface IUIManager
    {
        Task<T?> OpenAsync<T>(EUIOpenMode mode = EUIOpenMode.Single,EUILevel lvl = EUILevel.Common, CancellationToken cancellationToken = default)
            where T : class, IUIWindow;
        bool TryGet<T>(out T? instance) where T : class, IUIWindow;
        bool Close(IUIWindow instance);
        bool Close<T>() where T : class, IUIWindow;
        int CloseAll<T>() where T : class, IUIWindow;
        void Push<T>(T ui) where T : IUIWindow;
        Task<T?> Pop<T>() where T : class,IUIWindow;
    }

    internal sealed class UIManager : IUIManager
    {
        private readonly IUIAdapter _adapter;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        // 当前活跃实例
        private readonly Dictionary<Type, List<UIKey>> _active = new();

        // 全局 LRU 缓存
        private readonly LRUCache<int, UIKey> _cache;
        private readonly Dictionary<Type, List<int>> _idCacheMap = new();
        private int _serialId;
        private readonly Stack<UIStackEntry> _stack;

        public UIManager(IUIAdapter adapter, int cacheCapacity = 16)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _cache = new LRUCache<int, UIKey>(cacheCapacity, OnCacheRemoved);
            _stack = new Stack<UIStackEntry>();
        }

        #region SerialId

        private int NextSerialId
        {
            get
            {
                checked
                {
                    return ++_serialId;
                }
            }
        }

        #endregion

        #region Open

        public async Task<T?> OpenAsync<T>(EUIOpenMode mode = EUIOpenMode.Single, EUILevel lvl = EUILevel.Common,
            CancellationToken cancellationToken = default) where T : class, IUIWindow 
            => await OpenAsync(typeof(T),mode,lvl,cancellationToken) as T;
        private async Task<IUIWindow?> OpenAsync(Type type,EUIOpenMode mode = EUIOpenMode.Single,EUILevel lvl = EUILevel.Common,
            CancellationToken cancellationToken = default,IUIData? data = null)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                IUIWindow? instance;
                cancellationToken.ThrowIfCancellationRequested();

                if (mode != EUIOpenMode.Single && mode != EUIOpenMode.Multiple)
                {
                    throw new ArgumentOutOfRangeException(nameof(mode));
                }
                // Single：优先返回当前活跃实例
                if (mode == EUIOpenMode.Single && TryGetLatestActive(type, out var activeKey))
                {
                    instance = activeKey.Instance;
                    var newKey = new UIKey(activeKey.Type, instance,activeKey.SerialId, activeKey.Mode, data);
                    _active[type][^1] = newKey;
                    _adapter.Open(instance);
                    return instance;
                }

                // 每次开启新的使用周期分配新 ID
                int serialId = NextSerialId;

                // 优先从缓存获取实例
                if (TryTakeCached(type, out UIKey cachedKey))
                {
                    instance = cachedKey.Instance;

                    if (instance == null)
                    {
                        _adapter.Release(cachedKey.Instance);
                        throw new InvalidOperationException("Cached UI instance has an invalid type.");
                    }
                }
                else
                {
                    // 缓存未命中，创建实例
                    instance = await _adapter.CreateAsync(type,cancellationToken);

                    if (instance == null)
                    {
                        throw new InvalidOperationException($"Failed to create UI: {type.FullName}");
                    }
                }

                var key = new UIKey(type, instance, serialId,mode,data);
                bool added = false;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddActive(key);
                    added = true;
                    _adapter.Open(instance,data);
                    return instance;
                }
                catch
                {
                    if (added) RemoveActive(key);
                    // Release 必须能够处理尚未成功显示的实例
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
            if (instance == null)
                return false;

            return TryFindActive(instance, out var key) && MoveToCache(key);
        }

        public bool Close<T>() where T : class, IUIWindow => TryGetLatestActive(typeof(T), out var key) && MoveToCache(key);

        public int CloseAll<T>() where T : class, IUIWindow
        {
            var type = typeof(T);

            if (!_active.TryGetValue(type, out var keys))
                return 0;
            var snapshot = keys.ToArray();
            return snapshot.Count(MoveToCache);
        }

        #endregion

        #region Query

        public bool TryGet<T>(out T? instance) where T : class, IUIWindow
        {
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
                var list = pair.Value;

                foreach (var t in list)
                {
                    if (!ReferenceEquals(t.Instance, instance)) continue;
                    key = t;
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
            {
                throw new ArgumentException("Invalid UIKey.");
            }

            // 检查所有类型，避免同一实例被重复登记
            foreach (var pair in _active)
            {
                foreach (var item in pair.Value)
                {
                    if (item.SerialId == key.SerialId || ReferenceEquals(item.Instance, key.Instance))
                    {
                        throw new InvalidOperationException("Duplicate active UI instance.");
                    }
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
                {
                    continue;
                }

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
            // 先结束 UI 生命周期
            _adapter.Close(key.Instance);

            if (!RemoveActive(key))
            {
                _adapter.Open(key.Instance);
                throw new InvalidOperationException("Failed to remove active UI.");
            }

            try
            {
                // 先更新辅助索引
                AddCachedIndex(key);
                _cache.Add(key.SerialId, key);

                return true;
            }
            catch
            {
                // 缓存登记失败，恢复 Active
                RemoveCachedIndex(key.SerialId, key.Type);
                AddActive(key);
                _adapter.Open(key.Instance);

                throw;
            }
        }

        private bool TryTakeCached(Type type, out UIKey key)
        {
            key = default;

            if (!_idCacheMap.TryGetValue(type, out var ids))
                return false;

            // 最近关闭的实例优先复用
            while (ids.Count > 0)
            {
                int index = ids.Count - 1;
                int id = ids[index];

                if (_cache.TryTake(id, out key))
                {
                    // TryTake 不触发 LRU 回调
                    RemoveCachedIndex(id, type);

                    return true;
                }

                // 清理失效索引
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

        // 只清理辅助索引，不释放实例
        private void RemoveCachedIndex(int id, Type type)
        {
            if (!_idCacheMap.TryGetValue(type, out var list))
                return;

            list.Remove(id);

            if (list.Count == 0)
                _idCacheMap.Remove(type);
        }

        // LRU 真正移除缓存条目时调用
        private void OnCacheRemoved(int id, UIKey key)
        {
            RemoveCachedIndex(id, key.Type);
            _adapter.Release(key.Instance);
        }

        #endregion

        #region Stack

        public void Push<T>(T ui) where T : IUIWindow
        {
            if (ui.Level != EUILevel.Common)
            {
                throw new InvalidOperationException("only common panel can push to stack");
            }
            if (!TryFindActive(ui, out var key)) return;
            var entry = new UIStackEntry(key.Type, key.Data, key.Mode);
            if (Close(ui)) _stack.Push(entry);
        }
        public async Task<T?> Pop<T>() where T : class, IUIWindow
        {
            if (!_stack.TryPeek(out var entry)) return null;
            var panel = await OpenAsync(entry.PanelType, entry.Mode, data:entry.Data);
            if (panel == null) return null;
            _stack.Pop();
            return panel as T;
        }

        #endregion

    }
}