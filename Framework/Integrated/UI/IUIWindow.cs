using ObFramework.Core;

namespace ObFramework.Integrated.UI
{
    public interface IUIWindow : IController
    {
        void Init(IUIData? uiData = null);
        void Open(IUIData? uiData = null);
        void Close();
    }

    public interface IUIPanel : IUIWindow
    {
    }

    public interface IUIPopup : IUIWindow
    {
    }

    public interface IUIScene : IUIWindow
    {
    }
}
