using ObFramework.Core;
using ObFramework.Integrated.UI;
using UnrealSharp.UMG;

namespace ObFramework.Adapter.UnrealSharp.UI;

/// <summary>
/// UnrealSharp engine window (VC) for one UI instance. It carries only engine identity: the native
/// <see cref="UUserWidget"/>, the <see cref="EUILevel"/> that selects the UIRoot canvas and the owning
/// architecture. Business state and lifecycle live on the paired engine-independent
/// <see cref="AUIView"/>; UIManager drives that lifecycle and the adapter must not forward
/// <c>Init/Open/Close</c> calls to the widget.
/// </summary>
public abstract partial class AUIWindow : UUserWidget, IUIWindow
{
    /// <summary>Architecture of the running application. Used by the adapter to construct the paired business View.</summary>
    public abstract IArchitecture GetArchitecture();

    /// <summary>UIRoot canvas used to parent this widget.</summary>
    public abstract EUILevel Level { get; }
}
