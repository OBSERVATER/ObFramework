using System;

namespace ObFramework.Integrated.UI
{
    internal readonly struct UIStackEntry
    {
        public Type PanelType { get; }
        public IUIData Data { get; }
        public EUIOpenMode Mode { get; }

        public UIStackEntry(Type panelType, IUIData data, EUIOpenMode mode)
        {
            PanelType = panelType ?? throw new ArgumentNullException(nameof(panelType));
            Data = data;
            Mode = mode;
        }
    }
}