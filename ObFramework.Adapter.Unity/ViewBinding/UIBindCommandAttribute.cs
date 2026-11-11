using System;

namespace ObFramework.Adapter.Unity.ViewBinding
{
    /// <summary>
    /// Binds a serialized Unity control field to a command nested in the business View. The
    /// argument is the generated or hand-written command type name as seen from the View, for
    /// example <c>[UIBindCommand(nameof(MyView.SubmitCommand))]</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class UIBindCommandAttribute : Attribute
    {
        public UIBindCommandAttribute(string commandTypeName)
        {
            CommandTypeName = commandTypeName ?? throw new ArgumentNullException(nameof(commandTypeName));
        }

        /// <summary>Nested command type name inside the business View (<c>SubmitCommand</c>).</summary>
        public string CommandTypeName { get; }

        /// <summary>
        /// Optional CanExecute View method name override. Reserved for hand-written commands whose
        /// command class does not expose a parameterless CanExecute; currently unused by generated
        /// bindings because they call the command instance's CanExecute directly.
        /// </summary>
        public string? CanExecute { get; set; }
    }
}
