
using System;

namespace ObFramework.Integrated.Resource
{
    public readonly struct AssetKey : IEquatable<AssetKey>
    {
        public readonly string Location;
        public readonly Type Type;
        public readonly string Operation;

        public AssetKey(string location, Type type, string operation = "Asset")
        {
            Location = location;
            Type = type;
            Operation = operation;
        }

        public bool Equals(AssetKey other) => Location == other.Location && Type == other.Type && Operation == other.Operation;
        public override bool Equals(object obj) => obj is AssetKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Location?.GetHashCode() ?? 0;
                hash = hash * 31 + (Type?.GetHashCode() ?? 0);
                hash = hash * 31 + (Operation?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
