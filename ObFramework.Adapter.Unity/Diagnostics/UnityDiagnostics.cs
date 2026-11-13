using System.IO;
using ObFramework.Integrated.Diagnostics;
using Serilog.Events;
using UnityEngine;

namespace ObFramework.Adapter.Unity.Diagnostics
{
    public static class UnityDiagnostics
    {
        public static DiagnosticsPlugin Create()
        {
#if UNITY_EDITOR || DEVELEMENT_BUILD
            var lvl = Loglevel.Information;
#else
            var lvl = LogEventLevel.Warning;
#endif
            return new DiagnosticsPlugin( Path.Combine(Application.persistentDataPath, "Logs"),lvl, new UnityLogSink());
        }
    }
}
