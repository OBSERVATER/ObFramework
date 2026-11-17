using System.Text.Json.Nodes;

namespace ObFramework.Cli.Unity;

internal static class UnityProject
{
    public const string NuGetForUnityVersion = "4.5.0";
    public const string NuGetForUnityId = "com.github-glitchenzo.nugetforunity";
    public const string AddressablesId = "com.unity.addressables";
    public const string AddressablesVersion = "1.21.21";

    public static void Validate(string root)
    {
        if (!Directory.Exists(Path.Combine(root, "Assets")) || !Directory.Exists(Path.Combine(root, "ProjectSettings")))
            throw new ArgumentException($"Not a Unity project: {root}");
        var versionFile = Path.Combine(root, "ProjectSettings/ProjectVersion.txt");
        if (!File.Exists(versionFile)) throw new InvalidOperationException("ProjectSettings/ProjectVersion.txt is missing.");
        var version = File.ReadLines(versionFile).FirstOrDefault(x => x.StartsWith("m_EditorVersion: "))?.Split(' ').Last();
        if (version == null || !Version.TryParse(string.Join('.', version.Split('.').Take(2)), out var parsed) ||
            parsed < new Version(2022, 3))
            throw new InvalidOperationException($"Unity {version ?? "unknown"} is unsupported; Unity 2022.3 or newer is required.");
    }

    public static void ValidateSdk(string root, CancellationToken ct)
    {
        var sdks = ToolProcess.Run("dotnet", new[] { "--list-sdks" }, root, ct, TimeSpan.FromSeconds(15));
        if (!sdks.Split('\n').Any(x => Version.TryParse(x.Trim().Split(' ')[0], out var version) && version.Major >= 8))
            throw new InvalidOperationException("Install .NET SDK 8 or newer to resolve NuGet packages and install project-local tools.");
    }

    public static void RequireClosed(string root)
    {
        var lockFile = Path.Combine(root, "Temp/UnityLockfile");
        if (!File.Exists(lockFile)) return;
        try { using var handle = File.Open(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new InvalidOperationException("Close this project's Unity Editor before installing packages or running --unity."); }
    }

    public static JsonObject LoadObject(string path) => File.Exists(path)
        ? JsonNode.Parse(File.ReadAllText(path), documentOptions: new() { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip }) as JsonObject
            ?? throw new InvalidOperationException($"Expected a JSON object: {path}")
        : new JsonObject();

    public static void SaveObject(string path, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = value.ToJsonString(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerOptions.Default) { WriteIndented = true }) + Environment.NewLine;
        if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text);
    }

    public static void ConfigureUpm(string root, string resources)
    {
        var path = Path.Combine(root, "Packages/manifest.json");
        var manifest = LoadObject(path);
        var dependencies = manifest["dependencies"] as JsonObject;
        if (manifest["dependencies"] != null && dependencies == null)
            throw new InvalidOperationException("Packages/manifest.json dependencies must be an object.");
        if (dependencies == null) manifest["dependencies"] = dependencies = new JsonObject();
        if (!dependencies.ContainsKey(NuGetForUnityId) && !dependencies.ContainsKey("com.glitchenzo.nugetforunity"))
        {
            var registries = manifest["scopedRegistries"] as JsonArray;
            if (manifest["scopedRegistries"] != null && registries == null)
                throw new InvalidOperationException("Packages/manifest.json scopedRegistries must be an array.");
            if (registries == null) manifest["scopedRegistries"] = registries = new JsonArray();
            var registry = registries.OfType<JsonObject>().FirstOrDefault(x => x["url"]?.GetValue<string>()?.TrimEnd('/') == "https://package.openupm.com");
            if (registry == null)
            {
                registry = new JsonObject { ["name"] = "OpenUPM", ["url"] = "https://package.openupm.com", ["scopes"] = new JsonArray(NuGetForUnityId) };
                registries.Add(registry);
            }
            else
            {
                var scopes = registry["scopes"] as JsonArray ?? throw new InvalidOperationException("OpenUPM registry scopes must be an array.");
                if (!scopes.Any(x => x?.GetValue<string>() == NuGetForUnityId)) scopes.Add(NuGetForUnityId);
            }
            dependencies[NuGetForUnityId] = NuGetForUnityVersion;
        }
        if (resources == "addressables")
        {
            if (!dependencies.ContainsKey(AddressablesId)) dependencies[AddressablesId] = AddressablesVersion;
            var existing = dependencies[AddressablesId]!.GetValue<string>();
            if (!Version.TryParse(existing, out var version) || version.Major != 1 || version.Minor != 21)
                throw new InvalidOperationException($"Addressables '{existing}' is outside the supported 1.21.x range. Existing selection was preserved.");
        }
        SaveObject(path, manifest);
    }

    public static string ResourceChoice(string root, string? requested)
    {
        var choice = requested ?? LoadObject(Path.Combine(root, "setup.json"))["modules"]?["installation"]?["resources"]?.GetValue<string>() ?? "custom";
        if (choice is not ("custom" or "addressables"))
            throw new ArgumentException("--resources must be custom or addressables.");
        var installed = LoadObject(Path.Combine(root, "setup.json"))["modules"]?["installation"]?["resources"]?.GetValue<string>();
        if (installed != null && choice != installed)
            throw new InvalidOperationException($"Resource backend is already '{installed}'. Remove the previous integration and update modules.installation.resources explicitly before switching.");
        return choice;
    }
}
