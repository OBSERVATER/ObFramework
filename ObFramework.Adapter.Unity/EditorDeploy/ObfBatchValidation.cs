using UnityEditor;
using UnityEngine;

namespace ObFramework.Adapter.Unity.EditorTools
{
    /// <summary>Entry point for ob doctor --unity. Validation happens after Unity imports scripts.</summary>
    public static class ObfBatchValidation
    {
        public static void Run()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Run;
                return;
            }
            if (EditorUtility.scriptCompilationFailed)
            {
                Debug.LogError("OBF doctor: Unity script compilation failed.");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log("OBF doctor: Unity import and compilation succeeded.");
            EditorApplication.Exit(0);
        }
    }
}
