using Newtonsoft.Json;
using ObFramework.Cli.Commands.Reference;

namespace ObFramework.Cli.Utility;

public static class ReferenceNodeHelper
{
    public static Dictionary<string, ReferenceNode> _nameMap;

    public static bool CreateNode(string name, params string[] names)
    {
        if (_nameMap.TryGetValue(name, out _))
        {
            return false;
        }

        var node = new ReferenceNode()
        {
            Name = name,
            Reference = [.. names]
        };
        _nameMap.Add(name,node);
        Save();
        return true;
    }
    public static bool TryFindNode(string name, out string result)
    {
        result = string.Empty;
        if (!_nameMap.TryGetValue(name, out var node)) return false;

        result = JsonConvert.SerializeObject(node);
        // string indirect = FindIndirectNode(node);
        return true;
    }
    
    private static void Save()
    {
        
    }
}