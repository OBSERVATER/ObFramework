using System.Collections.Generic;

namespace ObFramework.Integrated.Config
{
    public interface IConfigCatalog
    {
        IReadOnlyList<ConfigEntry> Entries { get; }
    }
}
