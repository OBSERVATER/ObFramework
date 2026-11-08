using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core.Async;

namespace ObFramework.Core
{
    public abstract class PluginApplication
    {
        private readonly ObExecutionScope _work = new ObExecutionScope();
        private readonly CancellationTokenSource _startup = new CancellationTokenSource();
        private PluginHost? _host;
        private IArchitecture? _architecture;
        private Task? _startTask;
        private Task? _stopTask;
        private bool _stopping;
        private bool _cleaned;
        public IArchitecture Architecture => _architecture ?? throw new InvalidOperationException("Application is not initialized.");

        protected abstract IPlugin[] CreatePlugins();
        protected abstract IArchitecture CreateArchitecture(IPluginContext plugins);
        protected abstract Task EnterGameAsync(CancellationToken ct);
        protected virtual Task StopGameAsync() => Task.CompletedTask;
        protected virtual void ReleaseAdapters() { }

        public Task StartAsync()
        {
            if (_startTask != null || _stopping)
                throw new InvalidOperationException("Application can only be started once.");
            return _startTask = StartCoreAsync();
        }

        private async Task StartCoreAsync()
        {
            try
            {
                _host = new PluginHost(CreatePlugins());
                await _host.StartAsync(_startup.Token);
                _startup.Token.ThrowIfCancellationRequested();
                _architecture = CreateArchitecture(_host.Context);
                _startup.Token.ThrowIfCancellationRequested();
                await EnterGameAsync(_startup.Token);
                _startup.Token.ThrowIfCancellationRequested();
            }
            catch (Exception error)
            {
                _stopping = true;
                try { await CleanupAsync(); }
                catch (Exception cleanupError) { error.Data["ApplicationCleanupError"] = cleanupError; }
                throw;
            }
        }

        public Task RunAsync(Func<CancellationToken, Task> work)
        {
            if (_architecture == null || _stopping)
                throw new InvalidOperationException("Application is not running.");
            return _work.Run(work);
        }

        public Task StopAsync()
        {
            if (_stopTask != null)
                return _stopTask;
            _stopping = true;
            return _stopTask = StopCoreAsync();
        }

        private async Task StopCoreAsync()
        {
            var errors = new List<Exception>();
            try { if (!_cleaned) _startup.Cancel(); }
            catch (Exception error) { errors.Add(error); }
            _ = _work.StopAsync();
            if (_startTask != null)
            {
                try { await _startTask; }
                catch (Exception error)
                {
                    if (error.Data["ApplicationCleanupError"] is Exception cleanupError)
                        errors.Add(cleanupError);
                }
            }
            try { await CleanupAsync(); }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0)
                throw new AggregateException("Application shutdown failed.", errors);
        }

        private async Task CleanupAsync()
        {
            if (_cleaned)
                return;
            _cleaned = true;
            var errors = new List<Exception>();
            try { await _work.StopAsync(); }
            catch (Exception error) { errors.Add(error); }
            if (_architecture != null)
            {
                try { await StopGameAsync(); }
                catch (Exception error) { errors.Add(error); }
                try { _architecture.Deinit(); }
                catch (Exception error) { errors.Add(error); }
                _architecture = null;
            }
            if (_host != null)
            {
                try { await _host.StopAsync(); }
                catch (Exception error) { errors.Add(error); }
            }
            try { ReleaseAdapters(); }
            catch (Exception error) { errors.Add(error); }
            _startup.Dispose();
            if (errors.Count > 0)
                throw new AggregateException("Application cleanup failed.", errors);
        }
    }
}
