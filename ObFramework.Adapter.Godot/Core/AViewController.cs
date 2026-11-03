using Godot;
using ObFramework.Core;

namespace ObFramework.Adapter.Godot.Core
{
    public abstract partial class AViewController : Node, IController
    {
        public abstract IArchitecture GetArchitecture();
    }
}