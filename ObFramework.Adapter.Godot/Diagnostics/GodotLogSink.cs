using Godot;
using Serilog.Core;
using Serilog.Events;

namespace ObFramework.Adapter.Godot.Diagnostics
{
    public sealed class GodotLogSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            string message = logEvent.RenderMessage();
            switch (logEvent.Level)
            {
                case >= LogEventLevel.Error:
                    GD.PushError(message);
                    break;
                case >= LogEventLevel.Warning:
                    GD.PushWarning(message);
                    break;
                default:
                    GD.Print(message);
                    break;
            }
        }
    }
}