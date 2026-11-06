using UnrealSharp.UMG;

namespace ObFramework.Adapter.UnrealSharp.UI;
public sealed class UIRoot
{
    public UUserWidget Widget { get; }

    public UCanvasPanel SceneRoot { get; }
    public UCanvasPanel CommonRoot { get; }
    public UCanvasPanel PopupRoot { get; }

    public UIRoot(UUserWidget widget)
    {
        Widget = widget;

        var lists = widget.AllWidgetsAs<UCanvasPanel>();
        SceneRoot = lists.FirstOrDefault(x => x.Name == "Scene") ?? throw new InvalidOperationException();
        CommonRoot = lists.FirstOrDefault(x => x.Name == "Common") ?? throw new InvalidOperationException();
        PopupRoot = lists.FirstOrDefault(x => x.Name == "Popup") ?? throw new InvalidOperationException();
    }
}