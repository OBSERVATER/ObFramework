using Cysharp.Text;
using Serilog.Core;
using Serilog.Events;
using UnityEngine;

namespace ObFramework.Adapter.Unity.Diagnostics
{
    public class UnityLogSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            string source = "Global";
            if (logEvent.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue scalar)
                source = scalar.Value?.ToString() ?? "Global";

            string message = ZString.Format("[{0:HH:mm:ss.fff}][{1}][{2}]{3}",
                logEvent.Timestamp,
                logEvent.Level,
                source,
                logEvent.RenderMessage());

            if (logEvent.Exception != null)
                message = ZString.Format("{0}\n{1}", message, logEvent.Exception);

            switch (logEvent.Level)
            {
                case LogEventLevel.Verbose:
                case LogEventLevel.Debug:
                case LogEventLevel.Information:
                    Debug.Log(message);
                    break;
                case LogEventLevel.Warning:
                    Debug.LogWarning(message);
                    break;
                case LogEventLevel.Error:
                case LogEventLevel.Fatal:
                    Debug.LogError(message);
                    break;
            }
        }
    }
}