using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Core.Async
{
    public class ObExecutionScope
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _gate = new object();
        private TaskCompletionSource<bool> _drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> _stopCompletion;
        private List<Exception> _errors = new List<Exception>();
        private bool _stopping;
        private int _runningCount;

        public Task Run([NotNull] Func<CancellationToken, Task> work)
        {
            lock (_gate)
            {
                if (_stopping)
                {
                    throw new InvalidOperationException("ObExecutionScope is stopping.");
                }
                _runningCount++;
            }
            return ExecuteTrackedAsync(work);
        }
        public Task StopAsync()
        {
            Task completion;
            TaskCompletionSource<bool> source;
            lock (_gate)
            {
                if (_stopCompletion != null)
                {
                    return _stopCompletion.Task;
                }
                _stopping = true;
                source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _stopCompletion = source;
                completion = _runningCount == 0 ? Task.CompletedTask : _drained.Task;
            }

            _ = StopAsyncInternal(source, completion);
            return source.Task;
        }

        private async Task StopAsyncInternal(TaskCompletionSource<bool> source, Task completion)
        {
            try
            {
                var errors = new List<Exception>();
                try
                {
                    _cts.Cancel();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }

                await completion;

                lock (_gate)
                {
                    errors.AddRange(_errors);
                }

                if (errors.Count == 0)
                {
                    source.TrySetResult(true);
                }
                else if (errors.Count == 1)
                {
                    source.TrySetException(errors[0]);
                }
                else
                {
                    source.TrySetException(new AggregateException(errors));
                }
            }
            catch (Exception ex)
            {
                source.TrySetException(ex);
            }
        }

        private async Task ExecuteTrackedAsync(Func<CancellationToken, Task> work)
        {
            try
            {
                var task = work.Invoke(_cts.Token) ?? throw new InvalidOperationException($"{nameof(work)} is null");
                await task;
            }
            catch (OperationCanceledException e) when (_cts.IsCancellationRequested && e.CancellationToken == _cts.Token)
            {
                // Scope 发出的预期取消不记录业务错误
                throw;
            }
            catch (Exception e)
            {
                lock (_gate)
                {
                    _errors.Add(e);
                }
                throw;
            }
            finally
            {
                bool complete;
                lock (_gate)
                {
                    _runningCount--;
                    complete = _stopping && _runningCount == 0;
                }
                if (complete)
                {
                    _drained.SetResult(true);
                }
            }
        }
    }
}
