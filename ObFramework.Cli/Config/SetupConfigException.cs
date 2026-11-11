using System;

namespace ObFramework.Cli.Config
{
    /// <summary>
    /// Raised when a setup.json document, module registration or module path is invalid.
    /// <para>
    /// This is a data/format exception only: the CLI command layer catches it and prints a
    /// concise diagnostic without a stack trace. The Config CLI is intentionally independent
    /// from the runtime <c>ConfigPlugin</c>; this exception never crosses that boundary.
    /// </para>
    /// </summary>
    public sealed class SetupConfigException : Exception
    {
        public SetupConfigException(string message)
            : base(message)
        {
        }

        public SetupConfigException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
