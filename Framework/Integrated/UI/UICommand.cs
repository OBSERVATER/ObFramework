using System;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;

namespace ObFramework.Integrated.UI
{
    /// <summary>
    /// Base class for generated and hand-written synchronous UI commands. It captures the owning
    /// business View, checks CanExecute at execution time and exposes the shared command surface
    /// used by generated engine bindings.
    /// </summary>
    public abstract class AUICommand : ACommand
    {
        protected AUICommand(AUIView view, string commandName, object? parameter = null)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            if (string.IsNullOrEmpty(commandName))
                throw new ArgumentException("A command name is required.", nameof(commandName));
            CommandName = commandName;
            Parameter = parameter;
        }

        public AUIView View { get; }

        public string CommandName { get; }

        /// <summary>Optional UI event value captured when the command was constructed.</summary>
        public object? Parameter { get; }

        public bool IsRunning => View.IsCommandRunning(CommandName);

        protected virtual bool CanExecuteCore() => true;

        protected virtual bool CanExecuteCore(object? parameter) => CanExecuteCore();

        protected abstract void ExecuteCore();

        public bool CanExecute() => CanExecuteCore();

        public bool CanExecute(object? parameter) => CanExecuteCore(parameter);

        public sealed override void Execute()
        {
            if (!CanExecute())
                return;
            if (!View.TryBeginCommandExecution(CommandName, allowConcurrent: true))
                return;
            try
            {
                ExecuteCore();
            }
            finally
            {
                View.EndCommandExecution(CommandName);
            }
        }
    }

    /// <summary>
    /// Base class for generated and hand-written asynchronous UI commands. Running state is owned
    /// by the View (per View instance and command name), so constructing a new command object cannot
    /// bypass duplicate protection, and a cached window reopened from the LRU keeps its Running state.
    /// </summary>
    public abstract class AUIAsyncCommand : AAsyncCommand
    {
        protected AUIAsyncCommand(AUIView view, string commandName, object? parameter = null)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            if (string.IsNullOrEmpty(commandName))
                throw new ArgumentException("A command name is required.", nameof(commandName));
            CommandName = commandName;
            Parameter = parameter;
        }

        public AUIView View { get; }

        public string CommandName { get; }

        /// <summary>Optional UI event value captured when the command was constructed.</summary>
        public object? Parameter { get; }

        /// <summary>When true, overlapping executions of this command are allowed.</summary>
        public bool AllowConcurrentExecutions { get; protected set; }

        /// <summary>When true, closing the owning window requests cooperative cancellation.</summary>
        public bool CancelOnClose { get; protected set; } = true;

        public bool IsRunning => View.IsCommandRunning(CommandName);

        protected virtual bool CanExecuteCore() => true;

        protected virtual bool CanExecuteCore(object? parameter) => CanExecuteCore();

        protected abstract ValueTask ExecuteCore(CancellationToken ct);

        public bool CanExecute() => CanExecuteCore();

        public bool CanExecute(object? parameter) => CanExecuteCore(parameter);

        public sealed override async ValueTask Execute(CancellationToken ct)
        {
            if (!CanExecute())
                return;
            if (!View.TryBeginCommandExecution(CommandName, AllowConcurrentExecutions))
                return;

            CancellationTokenSource? linked = null;
            try
            {
                var token = ct;
                if (CancelOnClose)
                {
                    linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    View.RegisterCommandCancellation(CommandName, linked);
                    token = linked.Token;
                }

                await ExecuteCore(token).ConfigureAwait(false);
            }
            finally
            {
                if (linked != null)
                    View.ReleaseCommandCancellation(linked);
                View.EndCommandExecution(CommandName);
            }
        }
    }
}
