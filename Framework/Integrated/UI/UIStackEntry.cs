using System;

namespace ObFramework.Integrated.UI
{
    /// <summary>
    /// Navigation stack entry. The stack stores type/mode/data rather than a pinned instance so an
    /// evicted window can be reconstructed from the LRU or the engine adapter.
    /// </summary>
    internal readonly struct UIStackEntry
    {
        public Type ViewType { get; }
        public IUIData? Data { get; }
        public EUIOpenMode Mode { get; }

        public UIStackEntry(Type viewType, IUIData? data, EUIOpenMode mode)
        {
            ViewType = viewType ?? throw new ArgumentNullException(nameof(viewType));
            Data = data;
            Mode = mode;
        }
    }
}
