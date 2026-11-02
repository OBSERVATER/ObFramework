using ObFramework.Core;
using ObFramework.Integrated.UI;

namespace ObFramework.Adapter.Unity.UI
{
    public abstract class AUIWindow : AViewController,IUIWindow
    {
        public abstract override IArchitecture GetArchitecture();
        public EUILevel Level { get; }
        void IUIWindow.Init(IUIData? uiData) => OnInit(uiData);
        void IUIWindow.Open(IUIData? uiData) => OnOpen(uiData);
        void IUIWindow.Close() => OnClose();
        public virtual void OnInit(IUIData? uiData)
        {
        }
        public virtual void OnOpen(IUIData? uiData)
        {
        }
        public virtual void OnClose()
        {
        }
    }
}