using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;
using ObFramework.Integrated.Resource;

namespace ObFramework.Integrated.UI
{
    /// <summary>
    /// UI plugin facade. Every public operation is expressed in business View types; engine VC and
    /// adapter details stay behind <see cref="IUIAdapter"/>.
    /// </summary>
    public sealed class UIPlugin : IPlugin
    {
        private IUIAdapter? _adapter;
        private readonly Func<ResourcePlugin, IUIAdapter>? _createAdapter;
        private readonly Func<IUIAdapter, CancellationToken, Task>? _initialize;
        private readonly Func<IUIAdapter, Task>? _shutdown;
        private readonly int _cacheCapacity;
        private UIManager? _manager;
        private UIManager Manager => _manager ?? throw new ObjectDisposedException(nameof(UIPlugin), "UI plugin is not running.");

        public UIPlugin(IUIAdapter adapter, int cacheCapacity = 10, Func<IUIAdapter, CancellationToken, Task>? initialize = null, Func<IUIAdapter, Task>? shutdown = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _cacheCapacity = cacheCapacity;
            _initialize = initialize;
            _shutdown = shutdown;
        }

        public UIPlugin(Func<ResourcePlugin, IUIAdapter> createAdapter, int cacheCapacity = 10, Func<IUIAdapter, CancellationToken, Task>? initialize = null, Func<IUIAdapter, Task>? shutdown = null)
        {
            _createAdapter = createAdapter ?? throw new ArgumentNullException(nameof(createAdapter));
            _cacheCapacity = cacheCapacity;
            _initialize = initialize;
            _shutdown = shutdown;
        }

        public Task<T?> OpenAsync<T>(EUIOpenMode mode = EUIOpenMode.Single, IUIData? data = null, CancellationToken cancellationToken = default) where T : AUIView
            => Manager.OpenAsync<T>(mode, data, cancellationToken);
        public bool TryGet<T>(out T? view) where T : AUIView => Manager.TryGet(out view);
        public bool Close(AUIView? view) => Manager.Close(view);
        public bool Close<T>() where T : AUIView => Manager.Close<T>();
        public int CloseAll<T>() where T : AUIView => Manager.CloseAll<T>();
        public void Push<T>(T view) where T : AUIView => Manager.Push(view);
        public Task<T?> Pop<T>() where T : AUIView => Manager.Pop<T>();

        void IPlugin.Register(PluginRegistration registration)
        {
            if (_createAdapter != null)
                registration.Require<ResourcePlugin>();
        }

        async Task IPlugin.InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_createAdapter != null)
                _adapter = _createAdapter(plugins.Get<ResourcePlugin>()) ?? throw new InvalidOperationException("UI adapter factory returned null.");
            _manager = new UIManager(_adapter!, _cacheCapacity);
            if (_initialize != null)
                await _initialize(_adapter!, ct);
            ct.ThrowIfCancellationRequested();
        }

        async Task IPlugin.StopAsync(IPluginContext plugins)
        {
            var errors = new List<Exception>();
            if (_manager != null)
            {
                try { await _manager.StopAsync(); }
                catch (Exception error) { errors.Add(error); }
            }
            if (_adapter != null)
            {
                if (_shutdown != null)
                {
                    try { await _shutdown(_adapter); }
                    catch (Exception error) { errors.Add(error); }
                }
                if (_createAdapter != null && _adapter is IDisposable disposable)
                {
                    try { disposable.Dispose(); }
                    catch (Exception error) { errors.Add(error); }
                }
            }
            _manager?.Dispose();
            _manager = null;
            _adapter = null;
            if (errors.Count > 0)
                throw new AggregateException("UI plugin shutdown failed.", errors);
        }
    }
}
