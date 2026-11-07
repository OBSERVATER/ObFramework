using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;
using ObFramework.Integrated.Resource;

namespace ObFramework.Integrated.UI
{
    public sealed class UIPlugin : IPlugin
    {
        private IUIAdapter? _adapter;
        private readonly Func<IAssetManager, IUIAdapter>? _createAdapter;
        private readonly Func<IUIAdapter, CancellationToken, Task>? _initialize;
        private readonly Func<IUIAdapter, Task>? _shutdown;
        private readonly int _cacheCapacity;
        private UIManager? _manager;
        public UIPlugin(IUIAdapter adapter, int cacheCapacity = 10, Func<IUIAdapter, CancellationToken, Task>? initialize = null, Func<IUIAdapter, Task>? shutdown = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _cacheCapacity = cacheCapacity;
            _initialize = initialize;
            _shutdown = shutdown;
        }
        public UIPlugin(Func<IAssetManager, IUIAdapter> createAdapter, int cacheCapacity = 10, Func<IUIAdapter, CancellationToken, Task>? initialize = null, Func<IUIAdapter, Task>? shutdown = null)
        {
            _createAdapter = createAdapter ?? throw new ArgumentNullException(nameof(createAdapter));
            _cacheCapacity = cacheCapacity;
            _initialize = initialize;
            _shutdown = shutdown;
        }
        public void Register(PluginRegistration registration)
        {
            if (_createAdapter != null)
                registration.Require<IAssetManager>();
            registration.Export<IUIManager, UIManager>(_ => new UIManager(_adapter ?? throw new InvalidOperationException("UI adapter is not ready."), _cacheCapacity));
        }
        public async Task InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_createAdapter != null)
                _adapter = _createAdapter(plugins.Get<IAssetManager>()) ?? throw new InvalidOperationException("UI adapter factory returned null.");
            _manager = (UIManager)plugins.Get<IUIManager>();
            if (_initialize != null)
                await _initialize(_adapter!, ct);
            ct.ThrowIfCancellationRequested();
        }
        public async Task StopAsync(IPluginContext plugins)
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
            _manager = null;
            _adapter = null;
            if (errors.Count > 0)
                throw new AggregateException("UI plugin shutdown failed.", errors);
        }
    }
}
