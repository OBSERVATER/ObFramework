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
        private readonly Dictionary<IUIWindow, IHandle> _handles = new();
        public async Task<T?> CreateAsync<T>(CancellationToken ct = default, IUIData? data = null) where T : class, IUIWindow 
            => await CreateAsync(typeof(T), ct, data) as T;
        public async Task<IUIWindow> CreateAsync(Type type, CancellationToken ct = default, IUIData? data = null)
        {
            GameObject inst = null;
            IHandle handle = null;
            try
            {
                handle = await ResModule.LoadAssetAsync<GameObject>(type.Name);
                ct.ThrowIfCancellationRequested();
                var o = handle.Get<GameObject>();
                inst = Object.Instantiate(o);

                if (inst.GetComponent(type) is not IUIWindow component)
                {
                    throw new InvalidOperationException($"UI Prefab does not contain {type.FullName}");
                }
                inst.SetActive(false);
                component.Init(data);
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
            go.SetActive(true);
            instance.Open(data);
        }

        public void Close(IUIWindow instance)
        {
            instance.Close();
            var go = ((Component)instance).gameObject;
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

    }
}