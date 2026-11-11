using System;

namespace ObFramework.Cli.Config
{
    /// <summary>
    /// Marks a POCO configuration property as a module-relative path.
    /// <para>
    /// Path values are validated against the absolute setup.json <c>root</c> and normalized to
    /// forward-slash relative form whenever a module section is written. A value may never escape
    /// the root. Only <see cref="string"/> and <c>string[]</c> members are supported so the JSON
    /// shape stays simple and predictable.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class SetupPathAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets whether an empty value is accepted. Defaults to <see langword="false"/>:
        /// a module path member must be configured.
        /// </summary>
        public bool AllowEmpty { get; set; }

        /// <summary>Optional human readable purpose of the path, shown by CLI help/diagnostics.</summary>
        public string? Description { get; set; }
    }
}
