using Godot;
using ObFramework.Core;
using ObFramework.Integrated.UI;

namespace ObFramework.Adapter.Godot.UI
{
    /// <summary>
    /// Godot engine window (VC) for one UI instance. It carries only engine identity: the native
    /// <see cref="Control"/> node, the <see cref="EUILevel"/> that selects the UIRoot layer and the
    /// owning architecture. Business state and lifecycle live on the paired engine-independent
    /// <see cref="AUIView"/>; UIManager drives that lifecycle and the adapter must not forward
    /// <c>Init/Open/Close</c> calls to the Control.
    /// </summary>
    public abstract class AUIWindow : Control, IUIWindow
    {
        /// <summary>Architecture of the running application. Used by the adapter to construct the paired business View.</summary>
        public abstract IArchitecture GetArchitecture();

        /// <summary>UIRoot layer used to parent this window.</summary>
        public abstract EUILevel Level { get; }
    }
}
