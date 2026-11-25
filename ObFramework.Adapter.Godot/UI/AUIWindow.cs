using Godot;
using ObFramework.Core;
using ObFramework.Integrated.UI;

namespace ObFramework.Adapter.Godot.UI
{
    public abstract partial class AUIWindow : Control, IUIWindow
    {
        public abstract IArchitecture GetArchitecture();
        public abstract EUILevel Level { get; }
    }
}
