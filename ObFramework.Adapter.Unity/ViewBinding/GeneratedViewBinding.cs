using System;
using System.ComponentModel;
using ObFramework.Core;
using UnityEngine;

namespace ObFramework.Adapter.Unity.ViewBinding
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IGeneratedViewBinding
    {
        Type ControllerType { get; }
        object CreateView(IArchitecture architecture);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class GeneratedViewBinding
    {
        public static void OnEnable(MonoBehaviour controller) => UnityViewBindings.Current?.ChangeEnabled(controller, true);
        public static void OnDisable(MonoBehaviour controller) => UnityViewBindings.Current?.ChangeEnabled(controller, false);
        public static void OnDestroy(MonoBehaviour controller) => UnityViewBindings.Current?.Destroy(controller);

        /// <summary>
        /// Refreshes the generated display bindings of a controller once its business View exists and
        /// is enabled. Generated <c>OnEnable</c> already refreshes on normal activation; the runtime
        /// calls this after binding a controller that was already active when the binding was created
        /// (scene roots) and the Unity adapter calls it after activating a window.
        /// </summary>
        public static void Refresh(MonoBehaviour controller)
        {
            if (!controller)
                return;
            (controller as IGeneratedUIBinding)?.RefreshBindings();
        }
    }
}
