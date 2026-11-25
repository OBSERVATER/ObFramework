using System;
using System.Threading.Tasks;
using ObFramework.Core;
using ObFramework.Integrated.UI;
using UnityEngine;

namespace ObFramework.Adapter.Unity.ViewBinding
{
    /// <summary>
    /// Engine boundary used by generated VC binding code. It dispatches commands through the
    /// business View's Architecture and reports handler exceptions through the engine logger so
    /// failures are never silently swallowed while business Views stay engine independent.
    /// </summary>
    public static class GeneratedUIBindingRuntime
    {
        /// <summary>Dispatches a synchronous command through the View's Architecture.</summary>
        public static void Send(MonoBehaviour context, AUIView view, ACommand command)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            try
            {
                view.GetArchitecture().SendCommand(command);
            }
            catch (Exception error)
            {
                Report(context, error);
            }
        }

        /// <summary>
        /// Dispatches an asynchronous command at the engine event boundary. This boundary reports
        /// failures through <see cref="Report"/>. The command runtime restores Running state.
        /// </summary>
        public static void SendAsync(MonoBehaviour context, AUIView view, AAsyncCommand command)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            _ = ExecuteAsync(context, view, command);
        }

        private static async Task ExecuteAsync(MonoBehaviour context, AUIView view, AAsyncCommand command)
        {
            try
            {
                await view.GetArchitecture().SendCommandAsync(command);
            }
            catch (Exception error)
            {
                Report(context, error);
            }
        }

        /// <summary>Reports an exception that crossed the engine binding boundary.</summary>
        public static void Report(MonoBehaviour context, Exception error)
        {
            if (error == null)
                return;
            if (context != null)
                Debug.LogException(error, context);
            else
                Debug.LogException(error);
        }
    }
}
