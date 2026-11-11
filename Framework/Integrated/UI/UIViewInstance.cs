using System;

namespace ObFramework.Integrated.UI
{
    /// <summary>
    /// One managed window instance: the engine VC (<see cref="IUIWindow"/>) and its paired
    /// business View (<see cref="AUIView"/>). UIManager, the adapter and the binding layer all
    /// receive this pair instead of a bare engine window.
    /// </summary>
    public readonly struct UIViewInstance : IEquatable<UIViewInstance>
    {
        public IUIWindow Window { get; }
        public AUIView View { get; }
        public EUILevel Level => Window.Level;

        public UIViewInstance(IUIWindow window, AUIView view)
        {
            Window = window ?? throw new ArgumentNullException(nameof(window));
            View = view ?? throw new ArgumentNullException(nameof(view));
        }

        public bool IsValid => Window != null && View != null;

        public bool Equals(UIViewInstance other)
        {
            return ReferenceEquals(Window, other.Window) && ReferenceEquals(View, other.View);
        }

        public override bool Equals(object obj) => obj is UIViewInstance other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Window != null ? Window.GetHashCode() : 0) * 397) ^ (View != null ? View.GetHashCode() : 0);
            }
        }

        public static bool operator ==(UIViewInstance left, UIViewInstance right) => left.Equals(right);
        public static bool operator !=(UIViewInstance left, UIViewInstance right) => !left.Equals(right);
    }
}
