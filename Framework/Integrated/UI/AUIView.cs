using System;
using System.Collections.Generic;
using System.Threading;
using ObFramework.Core;

namespace ObFramework.Integrated.UI
{
    /// <summary>
    /// Engine-independent UI business View. It carries the managed business lifecycle
    /// (<see cref="OnInit"/>, <see cref="OnOpen"/>, <see cref="OnClose"/>, <see cref="OnDestroy"/>),
    /// the reusable child data entry point <see cref="Bind"/> and the notification channels used by
    /// generated engine bindings. Engine-specific rendering objects stay on the engine VC
    /// (<see cref="IUIWindow"/>); this type must never reference an engine type.
    /// </summary>
    public abstract class AUIView : IController, ICanSetArchitecture, IDisposable, IViewEnabled, IViewDisabled
    {
        private readonly Dictionary<string, CommandExecutionState> _executions =
            new Dictionary<string, CommandExecutionState>(StringComparer.Ordinal);

        private IArchitecture? _architecture;
        private IUIData? _data;
        private bool _initialized;
        private bool _destroyed;

        /// <summary>True after the managed <see cref="OnInit"/> ran once.</summary>
        public bool IsInitialized => _initialized;

        /// <summary>Data supplied by the latest managed open.</summary>
        public IUIData? Data => _data;

        /// <summary>Gets the Architecture this View belongs to. Assigned by the engine binding layer.</summary>
        public IArchitecture GetArchitecture()
        {
            return _architecture
                ?? throw new InvalidOperationException($"UI view '{GetType().FullName}' is not attached to an architecture.");
        }

        void ICanSetArchitecture.SetArchitecture(IArchitecture architecture)
        {
            var value = architecture ?? throw new ArgumentNullException(nameof(architecture));
            if (_architecture != null && !ReferenceEquals(_architecture, value))
                throw new InvalidOperationException($"UI view '{GetType().FullName}' already belongs to another architecture.");
            _architecture = value;
        }

        #region Lifecycle driven by UIManager

        internal void InitView(IUIData? data)
        {
            if (_initialized || _destroyed)
                return;
            OnInit(data);
            _initialized = true;
        }

        internal void OpenView(IUIData? data)
        {
            if (_destroyed)
                return;
            _data = data;
            OnOpen(data);
        }

        internal void CloseView()
        {
            if (_destroyed)
                return;
            var errors = new List<Exception>();
            try { OnClose(); }
            catch (Exception error) { errors.Add(error); }
            try { CancelCommandsOnClose(); }
            catch (Exception error) { errors.Add(error); }
            _data = null;
            if (errors.Count == 1)
                throw errors[0];
            if (errors.Count > 1)
                throw new AggregateException("UI view close failed.", errors);
        }

        internal void DestroyView()
        {
            if (_destroyed)
                return;
            _destroyed = true;
            try { OnDestroy(); }
            finally
            {
                PropertyChanged = null;
                CommandStateChanged = null;
                lock (_commandGate)
                {
                    foreach (var execution in _executions.Values)
                        execution.Clear();
                    _executions.Clear();
                }
                _data = null;
            }
        }

        #endregion

        #region Reusable child data

        /// <summary>
        /// Reassigns data to an existing reusable child View. Does not re-run <see cref="OnInit"/>
        /// or the managed open/close lifecycle, and does not cancel running business tasks.
        /// </summary>
        public void Bind(IUIData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (_destroyed)
                throw new ObjectDisposedException(GetType().FullName);
            OnBind(data);
        }

        protected virtual void OnInit(IUIData? data)
        {
        }

        protected virtual void OnOpen(IUIData? data)
        {
        }

        protected virtual void OnClose()
        {
        }

        protected virtual void OnBind(IUIData data)
        {
        }

        protected virtual void OnDestroy()
        {
        }

        public virtual void OnViewEnabled()
        {
        }

        public virtual void OnViewDisabled()
        {
        }

        #endregion

        #region Notifications

        /// <summary>
        /// Raised with the generated property name whenever observable state changes. Generated
        /// engine bindings subscribe once per display cycle and refresh the bound controls.
        /// </summary>
        public event Action<string>? PropertyChanged;

        /// <summary>
        /// Raised with the command name whenever its Running/CanExecute-visible state changes.
        /// </summary>
        public event Action<string>? CommandStateChanged;

        /// <summary>Raises <see cref="PropertyChanged"/> for a generated or View-authored property.</summary>
        protected void NotifyPropertyChanged(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                return;
            PropertyChanged?.Invoke(propertyName);
        }

        /// <summary>
        /// Raises <see cref="CommandStateChanged"/> for a command whose CanExecute-visible state
        /// changed. Generated View code calls this for <c>[NotifyCanExecuteChangedFor]</c>
        /// dependencies so generated engine bindings refresh control interactability.
        /// </summary>
        protected void NotifyCommandStateChanged(string commandName)
        {
            if (string.IsNullOrEmpty(commandName))
                return;
            CommandStateChanged?.Invoke(commandName);
        }

        #endregion

        #region Command execution state

        // Command completions can resume on arbitrary threads (async continuations), while close
        // can request cancellation from the UI/main thread, so the per-View execution bookkeeping is
        // guarded. Business View state itself is not made thread-safe by the framework.
        private readonly object _commandGate = new object();

        /// <summary>True while at least one execution of the named command is running.</summary>
        public bool IsCommandRunning(string commandName)
        {
            if (commandName == null)
                return false;
            lock (_commandGate)
                return _executions.TryGetValue(commandName, out var execution) && execution.Running;
        }

        internal bool TryBeginCommandExecution(string commandName, bool allowConcurrent)
        {
            if (string.IsNullOrEmpty(commandName))
                throw new ArgumentException("Command name is required.", nameof(commandName));

            CommandExecutionState execution;
            bool changed;
            lock (_commandGate)
            {
                execution = GetOrCreateExecution(commandName);
                if (!allowConcurrent && execution.Running)
                    return false;

                execution.RunningCount++;
                changed = execution.RunningCount == 1;
            }

            if (!changed)
                return true;

            try
            {
                NotifyCommandStateChanged(commandName);
            }
            catch
            {
                // A display subscriber threw while the command was starting. Restore the Running
                // state so the command is not blocked forever, then surface the binding error.
                lock (_commandGate)
                    execution.RunningCount--;
                if (execution.RunningCount < 0)
                    execution.RunningCount = 0;
                throw;
            }

            return true;
        }

        internal void EndCommandExecution(string commandName)
        {
            bool changed;
            lock (_commandGate)
            {
                if (!_executions.TryGetValue(commandName, out var execution) || execution.RunningCount == 0)
                    return;
                execution.RunningCount--;
                changed = execution.RunningCount == 0;
            }

            if (!changed)
                return;

            try
            {
                NotifyCommandStateChanged(commandName);
            }
            catch
            {
                // The command state is already consistent; a display subscriber throwing must not
                // replace the command's result or mask an exception raised by the business method.
            }
        }

        internal void RegisterCommandCancellation(string commandName, CancellationTokenSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            lock (_commandGate)
                GetOrCreateExecution(commandName).Cancellations.Add(source);
        }

        internal void ReleaseCommandCancellation(CancellationTokenSource source)
        {
            if (source == null)
                return;
            lock (_commandGate)
            {
                foreach (var execution in _executions.Values)
                {
                    if (execution.Cancellations.Remove(source))
                        break;
                }
            }
            source.Dispose();
        }

        private int CancelCommandsOnClose()
        {
            CancellationTokenSource[] pending;
            lock (_commandGate)
            {
                var sources = new List<CancellationTokenSource>();
                foreach (var execution in _executions.Values)
                    sources.AddRange(execution.Cancellations);
                pending = sources.ToArray();
            }

            var cancelled = 0;
            foreach (var source in pending)
            {
                try
                {
                    source.Cancel();
                    cancelled++;
                }
                catch (ObjectDisposedException)
                {
                    // The execution already completed and released its token source.
                }
                catch (AggregateException)
                {
                    // A cancellation callback threw; cooperative cancellation was still requested.
                    cancelled++;
                }
            }
            return cancelled;
        }

        private CommandExecutionState GetOrCreateExecution(string commandName)
        {
            if (!_executions.TryGetValue(commandName, out var execution))
            {
                execution = new CommandExecutionState();
                _executions.Add(commandName, execution);
            }
            return execution;
        }

        #endregion

        public void Dispose()
        {
            DestroyView();
        }

        private sealed class CommandExecutionState
        {
            public int RunningCount;
            public readonly List<CancellationTokenSource> Cancellations = new List<CancellationTokenSource>();

            public bool Running => RunningCount > 0;

            public void Clear()
            {
                foreach (var cancellation in Cancellations)
                {
                    try { cancellation.Dispose(); }
                    catch (ObjectDisposedException) { }
                }
                Cancellations.Clear();
                RunningCount = 0;
            }
        }
    }
}
