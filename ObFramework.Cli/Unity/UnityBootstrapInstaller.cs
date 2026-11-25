namespace ObFramework.Cli.Unity;

internal static class UnityBootstrapInstaller
{
    internal const string LauncherFile = "Assets/Scripts/Unity/GameLauncher.cs";
    internal const string PrefabFile = "Assets/ObFramework/Resources/ObFramework/UIRoot.prefab";

    public static void Install(string root)
    {
        CopyIfAbsent(root, PrefabFile);
        CopyIfAbsent(root, PrefabFile + ".meta");
        if (!File.Exists(Path.Combine(root, LauncherFile)) && !UnityTestScaffold.HasDefaultApplication(root))
        {
            CopyIfAbsent(root, LauncherFile + ".txt");
            Console.WriteLine($"Init: Integrate '{LauncherFile}.txt' with your existing GameApplication before attaching it to a scene.");
            return;
        }
        CopyIfAbsent(root, LauncherFile);
        Console.WriteLine($"Init: Attach '{LauncherFile}' to a GameObject in the startup scene.");
    }

    private static void CopyIfAbsent(string root, string relativePath)
    {
        var destination = Path.Combine(root, relativePath);
        if (File.Exists(destination)) return;

        var resourceFile = Path.GetFileName(relativePath).Replace(".cs.txt", ".cs", StringComparison.Ordinal);
        var resourceName = "ObFramework.Cli.UnityBootstrap/" + resourceFile;
        using var source = typeof(UnityBootstrapInstaller).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Unity bootstrap resource '{resourceName}' is missing.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(temporary)) source.CopyTo(file);
            File.Move(temporary, destination);
        }
        catch (IOException) when (File.Exists(destination))
        {
            // Another initialization already installed the file.
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
