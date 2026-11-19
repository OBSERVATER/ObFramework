using System.Text.Json;
using System.Text.Json.Nodes;

namespace ObFramework.Cli.Unity;

internal static class UnityEditorLocator
{
    internal static string Resolve(string root)
    {
        var configured = Environment.GetEnvironmentVariable("UNITY_EDITOR_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured)) return Path.GetFullPath(configured);
            throw new InvalidOperationException("UNITY_EDITOR_PATH must point to a Unity Editor executable.");
        }

        var version = UnityProject.EditorVersion(root)
            ?? throw new InvalidOperationException("The Unity project does not specify an Editor version.");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var hubData = Path.IsPathFullyQualified(appData) ? Path.Combine(appData, "UnityHub") : null;
        var defaultRoot = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor")
            : OperatingSystem.IsMacOS()
                ? "/Applications/Unity/Hub/Editor"
                : Path.Combine(home, "Unity", "Hub", "Editor");

        return FindInstalled(version, hubData, defaultRoot)
            ?? throw new InvalidOperationException(
                $"Unity {version} was not found. Add that Editor to Unity Hub or set UNITY_EDITOR_PATH to its executable.");
    }

    internal static string? FindInstalled(string version, string? hubData, string defaultRoot)
    {
        if (version.Length == 0 || !char.IsAsciiDigit(version[0]) ||
            !version.All(character => char.IsAsciiLetterOrDigit(character) || character == '.'))
            return null;

        if (hubData != null)
        {
            var installs = ReadHubFile(Path.Combine(hubData, "editors-v2.json")) as JsonObject;
            if (installs?["data"] is JsonArray editors)
            {
                foreach (var editor in editors.OfType<JsonObject>())
                {
                    if (GetString(editor["version"]) != version || editor["location"] is not JsonArray locations)
                        continue;

                    foreach (var location in locations)
                    {
                        var executable = FindExecutable(GetString(location));
                        if (executable != null) return executable;
                    }
                }
            }

            var customRoot = GetString(ReadHubFile(Path.Combine(hubData, "secondaryInstallPath.json")));
            var customEditor = FindAtInstallRoot(customRoot, version);
            if (customEditor != null) return customEditor;
        }

        return FindAtInstallRoot(defaultRoot, version);
    }

    private static string? FindAtInstallRoot(string? root, string version)
    {
        if (root == null || !Path.IsPathFullyQualified(root)) return null;

        var install = Path.Combine(root, version);
        var editor = OperatingSystem.IsMacOS()
            ? Path.Combine(install, "Unity.app")
            : Path.Combine(install, "Editor", OperatingSystem.IsWindows() ? "Unity.exe" : "Unity");
        return FindExecutable(editor);
    }

    private static string? FindExecutable(string? path)
    {
        if (path == null || !Path.IsPathFullyQualified(path)) return null;
        if (OperatingSystem.IsMacOS() && path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(path, "Contents", "MacOS", "Unity");

        var executableName = OperatingSystem.IsWindows() ? "Unity.exe" : "Unity";
        return string.Equals(Path.GetFileName(path), executableName, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    private static JsonNode? ReadHubFile(string path)
    {
        try { return JsonNode.Parse(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }

    private static string? GetString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
