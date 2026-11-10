using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ObFramework.Adapter.Unity.ViewBinding
{
    public static class ObUnity
    {
        public static T Instantiate<T>(T original, Transform? parent = null, bool instantiateInWorldSpace = false)
            where T : Object
        {
            if (!original)
                throw new ArgumentNullException(nameof(original));
            if (!(original is GameObject || original is Component))
                throw new ArgumentException("View binding requires a GameObject or Component.", nameof(original));

            var bindings = UnityViewBindings.Current;
            if (bindings == null || !bindings.IsReady)
                throw new InvalidOperationException("OBF view binding is not ready. Instantiate after application initialization.");

            var clone = Object.Instantiate(original, parent, instantiateInWorldSpace);
            if (!clone)
                throw new InvalidOperationException("The instantiated Unity object was destroyed before binding.");
            var root = clone is Component component ? component.gameObject : (GameObject)(Object)clone;
            bindings.BindClone(root);
            return clone;
        }
    }
}
