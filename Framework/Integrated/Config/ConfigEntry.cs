using System;

namespace ObFramework.Integrated.Config
{
    public readonly struct ConfigEntry
    {
        public Type Type { get; }
        public string Location { get; }
        public string? Name { get; }

        public ConfigEntry(Type type, string location, string? name = null)
        {
            Type = type;
            Location = location;
            Name = name;
        }
    }
}
