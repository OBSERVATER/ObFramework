namespace ObFramework.Cli.Commands.Init
{
    internal static class EngineProject
    {
        public static EngineKind Resolve(string? requested, string root)
        {
            switch (requested?.Trim().ToLowerInvariant())
            {
                case null:
                case "":
                case "auto":
                    if (Directory.Exists(Path.Combine(root, "Assets")) &&
                        Directory.Exists(Path.Combine(root, "ProjectSettings")))
                        return EngineKind.Unity;
                    if (File.Exists(Path.Combine(root, "project.godot")))
                        return EngineKind.Godot;
                    if (Directory.Exists(root) && Directory.EnumerateFiles(root, "*.uproject").Any())
                        return EngineKind.UnrealSharp;
                    return EngineKind.None;
                case "unity":
                    if (!Directory.Exists(Path.Combine(root, "Assets")) ||
                        !Directory.Exists(Path.Combine(root, "ProjectSettings")))
                        throw new ArgumentException($"Not a Unity project: {root}");
                    return EngineKind.Unity;
                case "godot":
                    return !File.Exists(Path.Combine(root, "project.godot")) ? 
                        throw new ArgumentException($"Not a Godot project: {root}") : EngineKind.Godot;
                case "unreal":
                case "unrealsharp":
                    if (!Directory.Exists(root) || !Directory.EnumerateFiles(root, "*.uproject").Any())
                        throw new ArgumentException($"Not an Unreal project: {root}");
                    return EngineKind.UnrealSharp;
                default:
                    throw new ArgumentException($"Unknown engine '{requested}'. Expected unity, godot, unreal or auto.");
            }
        }
    }
}
