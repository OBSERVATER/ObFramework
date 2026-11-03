using Godot;
using ObFramework.Core;
using ObFramework.Integrated.UI;

namespace ObFramework.Adapter.Godot.UI
{
    public abstract class AUIWindow : Control,IUIWindow
    {
        public abstract IArchitecture GetArchitecture();
        public EUILevel Level { get; }
        void IUIWindow.Init(IUIData? uiData) => OnInit(uiData);
        void IUIWindow.Open(IUIData? uiData) => OnOpen(uiData);
        void IUIWindow.Close() => OnClose();
        protected virtual void OnInit(IUIData? uiData)
        {
        }
        protected virtual void OnOpen(IUIData? uiData)
        {
        }
        protected virtual void OnClose()
        {
        }
    }
}