using System;
using ObFramework.Cli.Commands;
using ObFramework.Cli.Core;

namespace ObFramework.Cli.Deployment
{
    /// <summary>
    /// Registers the Unity Editor deployment CLI (<c>ob unity deploy|remove</c>) through the
    /// existing <c>CliModule.InjectCommand&lt;T&gt;</c> extension point. <c>CliModule</c> itself is
    /// never modified and no assembly is scanned.
    /// </summary>
    public static class UnityDeployCliModule
    {
        /// <summary>CLI command name registered by <see cref="Register"/>.</summary>
        public const string CommandName = "unity";

        /// <summary>Registers the Unity deployment commands.</summary>
        public static void Register(CliModule cli)
        {
            if (cli == null)
                throw new ArgumentNullException(nameof(cli));

            cli.InjectCommand<UnityCommand>(CommandName);
        }
    }
}
