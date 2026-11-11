using System;

namespace ObFramework.Cli.Deployment
{
    /// <summary>
    /// Raised when a Unity Editor deployment plan, project layout or owned-file manifest is invalid.
    /// The CLI catches this type and prints a concise diagnostic.
    /// </summary>
    public sealed class DeploymentException : Exception
    {
        public DeploymentException(string message)
            : base(message)
        {
        }

        public DeploymentException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
