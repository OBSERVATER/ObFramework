using Newtonsoft.Json;
using ObFramework.Cli.Commands.Reference;

namespace ObFramework.Cli.Utility;

public static class ReferenceNodeHelper
{
    private static readonly string FilePath = Path.Combine(Environment.CurrentDirectory, "references.json");

    private static readonly Dictionary<string, ReferenceNode> _nameMap = Load();

    public static string CreateNode(string name,string desc, params string[] names)
    {
        if (!ValidNames(name, names))
            return "node names cannot be empty.";
        if (string.IsNullOrWhiteSpace(desc))
            return "node description cannot be empty.";
        if (_nameMap.ContainsKey(name))
            return "node already exists.";

        _nameMap.Add(name, new ReferenceNode
        {
            Name = name,
            Reference = [.. names.Distinct()],
            Description = desc
        });

        var cycle = FindCycle();
        if (cycle != null)
        {
            _nameMap.Remove(name);
            return $"circular reference: {cycle}";
        }

        Save();
        return string.Empty;
    }

    public static string AddReference(string name, params string[] names)
    {
        if (!ValidNames(name, names))
            return "node names cannot be empty.";

        if (!_nameMap.TryGetValue(name, out var node))
            return "node not found.";

        node.Reference ??= new List<string>();

        var added = names.Distinct()
            .Except(node.Reference)
            .ToArray();

        node.Reference.AddRange(added);

        var cycle = FindCycle();
        if (cycle != null)
        {
            foreach (var reference in added)
                node.Reference.Remove(reference);

            return $"circular reference: {cycle}";
        }

        if (added.Length > 0)
            Save();

        return string.Empty;
    }

    public static bool RemoveReference(string name, string reference)
    {
        if (!_nameMap.TryGetValue(name, out var node))
            return false;

        if (node.Reference?.Remove(reference) != true)
            return false;

        Save();
        return true;
    }

    public static bool DeleteNode(string name)
    {
        if (!_nameMap.Remove(name))
            return false;

        foreach (var node in _nameMap.Values)
            node.Reference?.RemoveAll(x => x == name);

        Save();
        return true;
    }

    public static IReadOnlyList<string> ListNodes() => [.. _nameMap.Keys.OrderBy(x => x, StringComparer.Ordinal)];

    public static bool TryFindNode(string name, out string result) => TryFind(name, false, out result);

    public static bool TryFindReverseNode(string name, out string result) => TryFind(name, true, out result);

    private static bool TryFind(string name, bool reverse, out string result)
    {
        result = string.Empty;

        if (!_nameMap.ContainsKey(name))
            return false;

        var graph = _nameMap.ToDictionary(
            x => x.Key,
            x => new List<string>());

        foreach (var pair in _nameMap)
        {
            foreach (var reference in pair.Value.Reference ?? new List<string>())
            {
                if (reverse)
                {
                    if (graph.TryGetValue(reference, out var parents))
                        parents.Add(pair.Key);
                }
                else
                {
                    graph[pair.Key].Add(reference);
                }
            }
        }

        var direct = graph[name].Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var visited = new HashSet<string>(direct) { name };
        var indirect = new List<string>();
        var desc = _nameMap[name].Description;
        var queue = new Queue<string>(direct);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!graph.TryGetValue(current, out var references))
                continue;

            foreach (var reference in references)
            {
                if (!visited.Add(reference))
                    continue;

                indirect.Add(reference);
                queue.Enqueue(reference);
            }
        }

        indirect.Sort(StringComparer.Ordinal);

        result = JsonConvert.SerializeObject(new
        {
            name,
            direct,
            indirect,
            desc
        }, Formatting.Indented);

        return true;
    }

    private static string? FindCycle()
    {
        var states = new Dictionary<string, byte>();
        var path = new List<string>();

        foreach (var name in _nameMap.Keys)
        {
            var cycle = Visit(name);
            if (cycle != null)
                return cycle;
        }

        return null;

        string? Visit(string name)
        {
            if (states.TryGetValue(name, out var state))
            {
                if (state == 2)
                    return null;

                var start = path.IndexOf(name);
                return string.Join(" -> ", path.Skip(start).Append(name));
            }

            states[name] = 1;
            path.Add(name);

            foreach (var next in _nameMap[name].Reference ?? new List<string>())
            {
                if (!_nameMap.ContainsKey(next))
                    continue;

                var cycle = Visit(next);
                if (cycle != null)
                    return cycle;
            }

            path.RemoveAt(path.Count - 1);
            states[name] = 2;
            return null;
        }
    }

    private static bool ValidNames(string name, string[] references)
    {
        return !string.IsNullOrWhiteSpace(name) && !references.Any(string.IsNullOrWhiteSpace);
    }

    private static Dictionary<string, ReferenceNode> Load()
    {
        if (!File.Exists(FilePath))
            return new Dictionary<string, ReferenceNode>();

        return JsonConvert.DeserializeObject<Dictionary<string, ReferenceNode>>(File.ReadAllText(FilePath)) 
               ?? new Dictionary<string, ReferenceNode>();
    }

    private static void Save()
    {
        File.WriteAllText(FilePath, JsonConvert.SerializeObject(_nameMap, Formatting.Indented));
    }
}
