using System;
using System.Runtime.CompilerServices;

namespace ObFramework.Integrated.UI
{
    internal readonly struct UIKey : IEquatable<UIKey>
    {
        public Type Type { get; }
        public EUIOpenMode Mode { get; }
        public IUIWindow Instance { get; }
        public int SerialId { get; }
        public bool IsInstanceKey => Instance != null;
        public IUIData Data { get; }
        public UIKey(Type type, IUIWindow instance, int serial, EUIOpenMode mode, IUIData? data)
        {
            Type = type;
            Instance = instance;
            SerialId = serial;
            Mode = mode;
            Data = data;
        }

        public bool Equals(UIKey other)
        {
            return other.Type == Type && ReferenceEquals(other.Instance,Instance) && other.SerialId == SerialId;
        }
        public override bool Equals(object obj)
        {
            if (obj is not UIKey key)
            {
                return false;
            }
            return Equals(key);
        }
        public override int GetHashCode()
        {
            var a = Type?.GetHashCode() ?? 0;
            var b = RuntimeHelpers.GetHashCode(Instance);
            var c = SerialId.GetHashCode();
            return HashCode.Combine(a, b, c);
        }
        public static bool operator ==(UIKey a, UIKey b) => a.Equals(b);
        public static bool operator !=(UIKey a, UIKey b) => !a.Equals(b);
    }
}
