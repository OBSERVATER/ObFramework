using ObFramework.Core;
using ObFramework.Integrated.UI;
using UnrealSharp.Attributes;
using UnrealSharp.UMG;

namespace ObFramework.Adapter.UnrealSharp.UI;

public abstract partial class AUIWindow : UUserWidget,IUIWindow
{
    public abstract IArchitecture GetArchitecture();
    public abstract EUILevel Level { get; }
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