using ObFramework.Core;

namespace ObFramework.Integrated.UI
{
    /// <summary>
    /// 窗口
    /// </summary>
    public interface IUIWindow : IController
    {
        EUILevel Level { get; }
        void Init(IUIData? uiData = null);
        void Open(IUIData? uiData = null);
        void Close();
    }
}
