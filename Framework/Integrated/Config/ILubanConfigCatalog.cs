using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ObFramework.Integrated.Config
{
    public interface ILubanConfigCatalog : IConfigCatalog
    {
        object CreateTables(Func<string, JArray> loader);
        IReadOnlyDictionary<Type, IReadOnlyDictionary<int, IConfig>> CreateConfigMap(object tables);
    }
}
