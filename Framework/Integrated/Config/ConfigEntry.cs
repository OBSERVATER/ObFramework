using System;

namespace ObFramework.Integrated.Config
{
    public readonly struct ConfigEntry
    {
        public Type Type { get; }
        public string Location { get; }

        public ConfigEntry(Type type, string location)
        {
            Type = type;
            Location = location;
        }
    }
}
