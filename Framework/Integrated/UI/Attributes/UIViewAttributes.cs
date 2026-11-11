using System;

namespace ObFramework.Integrated.UI
{
    /// <summary>
    /// Marks a View field as observable state. The source generator emits a public PascalCase
    /// property over the single decorated backing field, with an equality check and change
    /// notifications consumed by generated engine bindings and CanExecute dependencies.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class ObservablePropertyAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a View method as a UI command. The source generator emits a nested
    /// <see cref="AUICommand"/> or <see cref="AUIAsyncCommand"/> type that captures the View
    /// instance and invokes this method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class RelayCommandAttribute : Attribute
    {
        /// <summary>Allows overlapping executions of an async command. Defaults to false.</summary>
        public bool AllowConcurrentExecutions { get; set; }

        /// <summary>Requests cooperative cancellation when the owning window closes. Defaults to true.</summary>
        public bool CancelOnClose { get; set; } = true;

        /// <summary>Optional View method that decides whether the command can execute.</summary>
        public string? CanExecute { get; set; }
    }

    /// <summary>
    /// Declares that the generated command must re-evaluate its CanExecute state when the
    /// decorated observable property changes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public sealed class NotifyCanExecuteChangedForAttribute : Attribute
    {
        public NotifyCanExecuteChangedForAttribute(string commandName)
        {
            CommandName = commandName ?? throw new ArgumentNullException(nameof(commandName));
        }

        public string CommandName { get; }
    }

    /// <summary>
    /// Declares that the generated property change notification of the decorated observable
    /// property must also notify a View-authored display property.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public sealed class NotifyPropertyChangedForAttribute : Attribute
    {
        public NotifyPropertyChangedForAttribute(string propertyName)
        {
            PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        }

        public string PropertyName { get; }
    }
}
