using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ObFramework.Cli.Commands.Config;
using ObFramework.Integrated.GAS.Attributes.Core;
using ObFramework.Integrated.GAS.Config;
using ObFramework.Integrated.GAS.GameplayEffect;

namespace ObFramework.Cli.Commands.Table;

internal static class GasTableExport
{
    internal static async Task<int> RunAsync(string dll, string root, string conf, string codeDir,
        string dataDir, bool code, bool data, Func<Task<int>>? runPrimary, CancellationToken ct)
    {
        var scratch = Directory.CreateTempSubdirectory("ob-gas-");
        try
        {
            var exit = await LubanRunner.StartAsync(dll, root, ct, "--conf", conf, "-t", "client", "--strict",
                "-d", "json", "-x", "outputDataDir=" + scratch.FullName);
            if (exit != 0) return exit;
            var files = Prepare(scratch.FullName, codeDir, dataDir, code, data);
            if (runPrimary != null)
            {
                exit = await runPrimary();
                if (exit != 0) return exit;
            }
            ct.ThrowIfCancellationRequested();
            foreach (var file in files) Write(file.Directory, file.Name, file.Content);
            return 0;
        }
        finally { scratch.Delete(true); }
    }

    private sealed record Output(string Directory, string Name, string Content);

    private static List<Output> Prepare(string source, string codeDir, string dataDir, bool code, bool data)
    {
        JArray Read(string name) => JArray.Parse(File.ReadAllText(Path.Combine(source, name + ".json")));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var fields = new StringBuilder();
        var members = new List<string>();
        var library = new GASConfigLibrary();
        foreach (var row in Read("attributes"))
        {
            var key = (string)row["key"]!;
            if (!Regex.IsMatch(key, @"\A[A-Za-z_][A-Za-z0-9_]*\z") || key is "All" or "CombatAttributes" || !keys.Add(key))
                throw new SetupConfigException($"Attribute.xlsx/Attributes: invalid or duplicate key '{key}'.");
            library.RegisterAttribute(new AttributeKey<float>(key));
            var comment = System.Security.SecurityElement.Escape((string?)row["comment"] ?? "");
            foreach (var line in comment!.ReplaceLineEndings("\n").Split('\n'))
                fields.AppendLine("    /// " + line);
            fields.AppendLine($"    public static readonly AttributeKey<float> @{key} = new AttributeKey<float>(\"{key}\");");
            members.Add("@" + key);
        }

        var effects = new Dictionary<int, EffectConfig>();
        var parts = new Dictionary<int, List<JObject>>();
        foreach (var row in Read("effects"))
        {
            var id = (int)row["id"]!;
            if (!Enum.TryParse<EEffectDurationType>((string?)row["durationType"], out var duration) || !Enum.IsDefined(duration))
                throw new SetupConfigException($"Effect.xlsx/Effects id={id}: invalid durationType '{row["durationType"]}'.");
            var effect = new EffectConfig { Id = id, Name = (string)row["name"]!, DurationType = duration,
                DurationTicks = (int)row["durationTicks"]!, PeriodTicks = (int)row["periodTicks"]! };
            if (id <= 0 || !effects.TryAdd(id, effect))
                throw new SetupConfigException($"Effect.xlsx/Effects: invalid or duplicate id={id}.");
            parts.Add(id, new List<JObject>());
        }
        foreach (var row in Read("components"))
        {
            var id = (int)row["effectId"]!;
            var key = (string)row["attribute"]!;
            var type = (string)row["type"]!;
            if (!parts.TryGetValue(id, out var list))
                throw new SetupConfigException($"Effect.xlsx/Components: unknown effectId={id}.");
            if (!keys.Contains(key))
                throw new SetupConfigException($"Effect.xlsx/Components effectId={id}: unknown attribute '{key}'.");
            if (type is not ("Cost" or "Base"))
                throw new SetupConfigException($"Effect.xlsx/Components effectId={id}: unsupported type '{type}'.");
            list.Add(new JObject { ["Type"] = type, ["Attribute"] = key, ["Value"] = row["value"],
                ["Periodic"] = row["periodic"] });
        }
        foreach (var effect in effects.Values) effect.Components = parts[effect.Id].ToArray();
        var abilities = new Dictionary<int, AbilityConfig>();
        foreach (var row in Read("abilities"))
        {
            var id = (int)row["id"]!;
            int effect = (int)row["effectId"]!, cost = (int)row["commitEffectId"]!;
            if (!effects.ContainsKey(effect))
                throw new SetupConfigException($"Ability.xlsx/Abilities id={id}: unknown effectId={effect}.");
            if (cost != 0 && !effects.ContainsKey(cost))
                throw new SetupConfigException($"Ability.xlsx/Abilities id={id}: unknown commitEffectId={cost}.");
            var ability = new AbilityConfig { Id = id, Name = (string)row["name"]!, Behavior = "ApplyEffect",
                Parameters = new JObject { ["EffectId"] = effect }, CommitEffectId = cost == 0 ? null : cost };
            if (id <= 0 || !abilities.TryAdd(id, ability))
                throw new SetupConfigException($"Ability.xlsx/Abilities: invalid or duplicate id={id}.");
        }

        library.Load(effects.Values, abilities.Values);
        var files = new List<Output>();
        if (code)
        {
            var attributes = "// <auto-generated/>\nusing ObFramework.Integrated.GAS.Attributes.Core;\n\n" +
                "public static class CombatAttributes\n{\n" + fields +
                "    public static readonly AttributeKey<float>[] All = { " + string.Join(", ", members) + " };\n}\n";
            files.Add(new(codeDir, "CombatAttributes.cs", attributes));
            using var stream = typeof(GasTableExport).Assembly.GetManifestResourceStream("GasStarter/GasConfigCatalog.txt")!;
            using var reader = new StreamReader(stream);
            files.Add(new(codeDir, "GasConfigCatalog.cs", reader.ReadToEnd()));
        }
        if (data)
        {
            files.Add(new(dataDir, "GasEffects.json", JsonConvert.SerializeObject(effects.Values, Formatting.Indented)));
            files.Add(new(dataDir, "GasAbilities.json", JsonConvert.SerializeObject(abilities.Values, Formatting.Indented)));
        }
        return files;
    }

    private static void Write(string directory, string name, string content)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, name);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, content);
            File.Move(temp, target, true);
        }
        finally { File.Delete(temp); }
    }
}
