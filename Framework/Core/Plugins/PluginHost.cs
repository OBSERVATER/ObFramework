using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VContainer;

namespace ObFramework.Core
{
    public sealed class PluginHost
    {
        private enum HostState { Created, Starting, Ready, Stopping, Closed, Failed }
        private readonly List<PluginDescriptor> _plugins = new List<PluginDescriptor>();
        private readonly Dictionary<Type, PluginDescriptor> _providers = new Dictionary<Type, PluginDescriptor>();
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private List<PluginDescriptor> _ordered = new List<PluginDescriptor>();
        private IObjectResolver? _resolver;
        private HostState _state;
        private bool _stopRequested;
        private Task? _startTask;
        private Task? _stopTask;
        public IPluginContext Context { get; }

        public PluginHost(params IPlugin[] plugins)
        {
            if (plugins == null)
                throw new ArgumentNullException(nameof(plugins));
            foreach (var plugin in plugins)
                _plugins.Add(new PluginDescriptor(plugin ?? throw new ArgumentException("Plugin list contains null.", nameof(plugins))));
            Context = new PluginContext(this, null);
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            if (_state != HostState.Created || _stopRequested)
                throw new InvalidOperationException("PluginHost can only be started once.");
            _state = HostState.Starting;
            return _startTask = StartCoreAsync(ct);
        }

        public Task StopAsync()
        {
            if (_stopTask != null)
                return _stopTask;
            _stopRequested = true;
            return _stopTask = StopCoreAsync();
        }

        internal sealed class PluginContext : IPluginContext
        {
            private readonly PluginHost _host;
            private readonly PluginDescriptor? _owner;
            internal PluginContext(PluginHost host, PluginDescriptor? owner)
            {
                _host = host;
                _owner = owner;
            }
            internal void EnsureReady()
            {
                if (_owner != null || _host._state != HostState.Ready || _host._stopRequested)
                    throw new InvalidOperationException("Plugin context is not ready or has been closed.");
            }
            public T Get<T>() where T : class
            {
                if (_owner == null)
                    EnsureReady();
                else if (!_owner.Entered || _host._resolver == null)
                    throw new InvalidOperationException("Plugin lifecycle context is not active.");
                if (!_host._providers.TryGetValue(typeof(T), out var provider))
                    throw new InvalidOperationException($"Capability {typeof(T).FullName} is not exported.");
                if (_owner != null && provider != _owner)
                {
                    if (!_owner.Dependencies.Contains(typeof(T)))
                        throw new InvalidOperationException($"Plugin {_owner.Plugin.GetType().FullName} did not declare dependency {typeof(T).FullName}.");
                    if (!provider.Ready)
                        throw new InvalidOperationException($"Dependency {typeof(T).FullName} is not ready.");
                }
                return _host._resolver!.Resolve<T>();
            }
        }

        private async Task StartCoreAsync(CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                var builder = new ContainerBuilder();
                var bindings = new Dictionary<Type, PluginDescriptor>();
                var types = new HashSet<Type>();
                foreach (var descriptor in _plugins)
                {
                    if (!types.Add(descriptor.Plugin.GetType()))
                        throw new InvalidOperationException($"Duplicate plugin {descriptor.Plugin.GetType().FullName}.");
                }
                foreach (var descriptor in _plugins)
                {
                    var registration = new PluginRegistration(builder, descriptor, bindings);
                    try { descriptor.Plugin.Register(registration); }
                    finally { registration.Freeze(); }
                    foreach (var capability in descriptor.Exports)
                        _providers.Add(capability, descriptor);
                    descriptor.Context = new PluginContext(this, descriptor);
                }
                _ordered = OrderPlugins();
                linked.Token.ThrowIfCancellationRequested();
                _resolver = builder.Build();
                foreach (var descriptor in _ordered)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    descriptor.Entered = true;
                    await descriptor.Plugin.InitAsync(descriptor.Context, linked.Token);
                    descriptor.Ready = true;
                }
                linked.Token.ThrowIfCancellationRequested();
                _state = HostState.Ready;
            }
            catch (Exception error)
            {
                var cleanupErrors = await CleanupAsync();
                _state = HostState.Failed;
                if (cleanupErrors.Count > 0)
                    error.Data["PluginCleanupErrors"] = new AggregateException(cleanupErrors);
                throw;
            }
        }

        private List<PluginDescriptor> OrderPlugins()
        {
            foreach (var descriptor in _plugins)
                foreach (var dependency in descriptor.Dependencies)
                    if (!_providers.ContainsKey(dependency))
                        throw new InvalidOperationException($"Plugin {descriptor.Plugin.GetType().FullName} requires missing capability {dependency.FullName}.");
            var ordered = new List<PluginDescriptor>();
            var remaining = new List<PluginDescriptor>(_plugins);
            while (remaining.Count > 0)
            {
                var index = remaining.FindIndex(plugin =>
                {
                    foreach (var dependency in plugin.Dependencies)
                        if (!ordered.Contains(_providers[dependency]))
                            return false;
                    return true;
                });
                if (index < 0)
                    throw new InvalidOperationException($"Plugin dependency cycle: {string.Join(", ", remaining.ConvertAll(x => x.Plugin.GetType().FullName))}.");
                ordered.Add(remaining[index]);
                remaining.RemoveAt(index);
            }
            return ordered;
        }

        private async Task StopCoreAsync()
        {
            var errors = new List<Exception>();
            try
            {
                try { _shutdown.Cancel(); }
                catch (Exception error) { errors.Add(error); }
                if (_startTask != null)
                {
                    try { await _startTask; }
                    catch (Exception)
                    {
                        // ignored
                    }
                }
                _state = HostState.Stopping;
                errors.AddRange(await CleanupAsync());
            }
            catch (Exception error) { errors.Add(error); }
            finally
            {
                _state = HostState.Closed;
                _shutdown.Dispose();
            }
            if (errors.Count > 0)
                throw new AggregateException("Plugin shutdown failed.", errors);
        }

        private async Task<List<Exception>> CleanupAsync()
        {
            var errors = new List<Exception>();
            for (var i = _ordered.Count - 1; i >= 0; i--)
            {
                var descriptor = _ordered[i];
                if (!descriptor.Entered)
                    continue;
                try { await descriptor.Plugin.StopAsync(descriptor.Context); }
                catch (Exception error) { errors.Add(error); }
                finally
                {
                    descriptor.Ready = false;
                    descriptor.Entered = false;
                }
            }
            var resolver = _resolver;
            _resolver = null;
            try { resolver?.Dispose(); }
            catch (Exception error) { errors.Add(error); }
            return errors;
        }
    }
}
