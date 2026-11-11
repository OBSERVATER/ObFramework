using System;

namespace ObFramework.Integrated.UI
{
    /// <summary>Managed window entry: the business View type plus the associated VC/View pair.</summary>
    internal readonly struct UIKey
    {
        public Type ViewType { get; }
        public UIViewInstance Instance { get; }
        public int SerialId { get; }
        public EUIOpenMode Mode { get; }
        public IUIData? Data { get; }

        public UIKey(Type viewType, UIViewInstance instance, int serialId, EUIOpenMode mode, IUIData? data)
        {
            ViewType = viewType;
            Instance = instance;
            SerialId = serialId;
            Mode = mode;
            Data = data;
        }

        public bool Equals(UIKey other)
        {
            return other.ViewType == ViewType && other.SerialId == SerialId;
        }

        public override bool Equals(object obj) => obj is UIKey key && Equals(key);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((ViewType != null ? ViewType.GetHashCode() : 0) * 397) ^ SerialId;
            }
        }

        public static bool operator ==(UIKey left, UIKey right) => left.Equals(right);
        public static bool operator !=(UIKey left, UIKey right) => !left.Equals(right);
    }
}
