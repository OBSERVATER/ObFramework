using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Integrated.Resource;
using ObFramework.Integrated.Resource.Handle;
using ObFramework.Integrated.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ObFramework.Adapter.Unity.UI
{
    public class UnityUIAdapter : IUIAdapter
    {
        public UIRoot Root;
        private readonly ResourcePlugin _assets;
        public UnityUIAdapter(ResourcePlugin assets) => _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        private readonly Dictionary<IUIWindow, IHandle> _handles = new();
        public async Task<T?> CreateAsync<T>(CancellationToken ct = default) where T : class, IUIWindow 
            => await CreateAsync(typeof(T), ct) as T;
        public async Task<IUIWindow> CreateAsync(Type type, CancellationToken ct = default)
        {
            GameObject? inst = null;
            IHandle handle = null;
            try
            {
                handle = await _assets.LoadAssetAsync<GameObject>(type.Name, ct);
                ct.ThrowIfCancellationRequested();
                var o = handle.Get<GameObject>();
                inst = Object.Instantiate(o);

                if (inst?.GetComponent(type) is not IUIWindow component)
                {
                    throw new InvalidOperationException($"UI Prefab does not contain {type.FullName}");
                }
                // ReSharper disable once Unity.InstantiateWithoutParent
                inst.transform.SetParent(GetParent(component.Level));
                inst.SetActive(false);
                component.Init();
                _handles.Add(component, handle);
                return component;
            }
            catch (Exception)
            {
                Object.Destroy(inst);
                handle?.Dispose();
                throw;
            }
        }
        public void Open(IUIWindow instance, IUIData? data = null)
        {
            var go = ((Component)instance).gameObject;
            go.transform.SetParent(GetParent(instance.Level));
            go.SetActive(true);
            instance.Open(data);
        }

        public void Close(IUIWindow instance)
        {
            if (instance is not Component component || !component)
                return;
            instance.Close();
            var go = component.gameObject;
            go.SetActive(false);
        }
        
        public void Release(IUIWindow instance)
        {
            if (!_handles.Remove(instance, out var handle))
                return;

            if (instance is Component component && component)
                Object.Destroy(component.gameObject);

            handle.Dispose();
        }
        private Transform GetParent(EUILevel level) => level switch
        {
            EUILevel.Scene => Root.SceneRoot,
            EUILevel.Common => Root.CommonRoot,
            EUILevel.Overlay => Root.PopupRoot,
            _ => Root.CommonRoot
        };
    }
}