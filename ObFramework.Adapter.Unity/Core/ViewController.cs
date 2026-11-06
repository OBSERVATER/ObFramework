using ObFramework.Core;
using UnityEngine;

namespace ObFramework.Adapter.Unity.UI
{
    public abstract class AViewController : MonoBehaviour,IController
    {
        public abstract IArchitecture GetArchitecture();
    }
}