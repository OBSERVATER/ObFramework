using Serilog.Core;
using Serilog.Events;
using UnrealSharp.Log;

namespace ObFramework.Adapter.UnrealSharp.Diagnostics
{
    [CustomLog]
    public static partial class ObFLog
    {
    }
    public sealed class UnrealLogSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            var message = logEvent.RenderMessage();

            if (logEvent.Exception != null)
                message += "\n" + logEvent.Exception;

            switch (logEvent.Level)
            {
                case LogEventLevel.Warning:
                    ObFLog.LogWarning(message);
                    break;
                case LogEventLevel.Error:
                case LogEventLevel.Fatal:
                    ObFLog.LogError(message);
                    break;
                default:
                    ObFLog.Log(message);
                    break;
            }
        }
    }
}