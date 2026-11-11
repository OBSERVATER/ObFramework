using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core.Async;
using ObFramework.Core.FrameLoop;

namespace ObFramework.Core
{
    public abstract class PluginApplication
    {
        private readonly ObExecutionScope _work = new ObExecutionScope();
        private readonly CancellationTokenSource _startup = new CancellationTokenSource();
        private PluginHost? _host;
        private IArchitecture? _architecture;
        private IApplicationFrameLoopDriver? _applicationDriver;
        private IFrameLoopDriver? _boundDriver;
        private Task? _startTask;
        private Task? _stopTask;
        private Task<List<Exception>>? _shutdown;
        private Task? _cleanupTask;
        private bool _binding;
        private bool _stopping;
        public IArchitecture Architecture => _architecture ?? throw new InvalidOperationException("Application is not initialized.");

        protected abstract IPlugin[] CreatePlugins();
        protected abstract IArchitecture CreateArchitecture(IPluginContext plugins);
        protected abstract Task EnterGameAsync(CancellationToken ct);
        protected virtual Task StopGameAsync() => Task.CompletedTask;
        protected virtual void ReleaseAdapters() { }

        public Task StartAsync(IFrameLoopDriver? driver = null)
        {
            if (_startTask != null || _stopping)
                throw new InvalidOperationException("Application can only be started once.");
            var completion = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            _startTask = completion.Task.Unwrap();
            completion.SetResult(StartCoreAsync(driver));
            return _startTask;
        }

        private async Task StartCoreAsync(IFrameLoopDriver? driver)
        {
            try
            {
                _applicationDriver = driver as IApplicationFrameLoopDriver;
                _applicationDriver?.BeginStartup(_startup.Token);
                ThrowIfStopping();
                _host = new PluginHost(CreatePlugins());
                ThrowIfStopping();
                await _host.StartAsync(_startup.Token);
                ThrowIfStopping();
                _architecture = CreateArchitecture(_host.Context);
                ThrowIfStopping();
                _applicationDriver?.Prepare(_architecture);
                ThrowIfStopping();
                if (driver != null)
                {
                    _boundDriver = driver;
                    _binding = true;
                    try { driver.Bind(_architecture.Frame); }
                    finally { _binding = false; }
                }
                ThrowIfStopping();
                await EnterGameAsync(_startup.Token);
                ThrowIfStopping();
            }
            catch (Exception error)
            {
                _stopping = true;
                try { await CleanupAsync(); }
                catch (Exception cleanupError) { error.Data["ApplicationCleanupError"] = cleanupError; }
                throw;
            }
        }

        private void ThrowIfStopping()
        {
            if (_stopping)
                throw new OperationCanceledException(_startup.Token);
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
            var completion = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopTask = completion.Task.Unwrap();
            completion.SetResult(StopCoreAsync());
            return _stopTask;
        }

        private async Task StopCoreAsync()
        {
            var errors = new List<Exception>();
            if (!_binding)
                _ = RequestShutdown();
            if (_startTask != null)
            {
                try { await _startTask; }
                catch (Exception error)
                {
                    if (error.Data["PluginCleanupErrors"] is Exception pluginCleanup)
                        errors.Add(pluginCleanup);
                    if (error.Data["ArchitectureCleanupError"] is Exception architectureCleanup)
                        errors.Add(architectureCleanup);
                }
            }
            try { await CleanupAsync(); }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0)
                throw new AggregateException("Application shutdown failed.", errors);
        }

        private Task<List<Exception>> RequestShutdown()
        {
            if (_shutdown != null)
                return _shutdown;

            // Cancellation can resume startup before Cancel returns.
            var completion = new TaskCompletionSource<List<Exception>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _shutdown = completion.Task;
            var errors = new List<Exception>();
            try { _boundDriver?.Unbind(); }
            catch (Exception error) { errors.Add(error); }
            _boundDriver = null;
            try { _startup.Cancel(); }
            catch (Exception error) { errors.Add(error); }
            _ = _work.StopAsync();
            completion.SetResult(errors);
            return _shutdown;
        }

        private Task CleanupAsync() => _cleanupTask ??= CleanupCoreAsync();

        private async Task CleanupCoreAsync()
        {
            var errors = await RequestShutdown();
            try { await _work.StopAsync(); }
            catch (Exception error) { errors.Add(error); }
            if (_applicationDriver != null)
            {
                try { await _applicationDriver.ReleaseAsync(); }
                catch (Exception error) { errors.Add(error); }
                _applicationDriver = null;
            }
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
