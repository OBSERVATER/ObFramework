using System;

namespace ObFramework.Adapter.Unity.ViewBinding
{
    /// <summary>
    /// Binds a serialized Unity control field to an observable business View property. The argument
    /// is the generated or View-authored property name, typically written as
    /// <c>[UIBindData(nameof(MyView.Title))]</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class UIBindDataAttribute : Attribute
    {
        public UIBindDataAttribute(string viewProperty)
        {
            ViewProperty = viewProperty ?? throw new ArgumentNullException(nameof(viewProperty));
        }

        /// <summary>Property name declared by the business View.</summary>
        public string ViewProperty { get; }

        /// <summary>Binding direction. Defaults to <see cref="UIBindMode.OneWay"/>.</summary>
        public UIBindMode Mode { get; set; } = UIBindMode.OneWay;

        /// <summary>
        /// Optional composite format string passed to <see cref="string.Format(string, object?)"/>
        /// when the bound property is displayed as text, for example <c>"{0:F1}"</c>.
        /// </summary>
        public string? Format { get; set; }
    }
}
