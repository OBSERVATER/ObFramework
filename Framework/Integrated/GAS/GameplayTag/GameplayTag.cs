using System;

namespace ObFramework.Integrated.GAS.GameplayTag
{
    public readonly struct GameplayTag : IEquatable<GameplayTag>
    {
        public string Name { get; }

        public GameplayTag(string name)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                name.StartsWith(".") ||
                name.EndsWith(".") ||
                name.Contains(".."))
            {
                throw new ArgumentException("Invalid gameplay tag.", nameof(name));
            }

            Name = name;
        }

        public bool IsValid => !string.IsNullOrEmpty(Name);

        public bool Matches(GameplayTag other)
        {
            if (!IsValid || !other.IsValid)
                return false;

            if (Equals(other))
                return true;

            return Name.Length > other.Name.Length &&
                   Name[other.Name.Length] == '.' &&
                   Name.StartsWith(other.Name,
                       StringComparison.Ordinal);
        }

        public bool Equals(GameplayTag other) => string.Equals(Name, other.Name, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is GameplayTag other && Equals(other);
        public override int GetHashCode() => Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name);
        public override string ToString() => Name ?? string.Empty;
    }
}