using ObFramework.Core;

namespace ObFramework.Integrated.UI
{
    /// <summary>
    /// Engine-side window identity: the native VC type and its UI level. Business lifecycle and
    /// data live on the paired engine-independent <see cref="AUIView"/>; UIManager owns managed
    /// open/close state.
    /// </summary>
    public interface IUIWindow : IController
    {
        EUILevel Level { get; }
    }
}
