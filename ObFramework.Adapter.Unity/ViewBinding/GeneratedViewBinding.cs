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
    }
}
