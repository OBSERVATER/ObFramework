using System.IO;
using ObFramework.Integrated.Diagnostics;
using UnityEngine;

namespace ObFramework.Adapter.Unity.Diagnostics
{
    public static class UnityDiagnostics
    {
        public static DiagnosticsPlugin Create() => new DiagnosticsPlugin(
            Path.Combine(Application.persistentDataPath, "Logs"), new UnityLogSink());
    }
}
