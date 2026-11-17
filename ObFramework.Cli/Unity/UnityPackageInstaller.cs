using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace ObFramework.Cli.Unity;

internal static class UnityPackageInstaller
{
    public static string DefaultVersion => typeof(UnityPackageInstaller).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    public static void Install(string root, string version, string? source, CancellationToken ct)
    {
        var layout = new NuGetLayout(root);
        layout.Configure(source);
        var existing = File.Exists(layout.Packages) ? XDocument.Load(layout.Packages) : new XDocument(new XElement("packages"));
        if (existing.Root?.Name != "packages") throw new InvalidOperationException("packages.config must contain a packages element.");
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in existing.Root.Elements("package"))
            references.Add((string)entry.Attribute("id")!, (string)entry.Attribute("version")!);
        references["ObFramework"] = version;
        references["ObFramework.Adapter.Unity"] = version;

        // Let the SDK's official NuGet resolver compute the full dependency closure. This project
        // lives outside Assets and has no Directory.Build imports from the user's game/repository.
        Directory.CreateDirectory(Path.Combine(root, ".obf"));
        var ignore = Path.Combine(root, ".obf/.gitignore");
        if (!File.Exists(ignore)) File.WriteAllText(ignore, "*\n");
        var work = Path.Combine(root, ".obf/resolve");
        Directory.CreateDirectory(work);
        var project = Path.Combine(work, "Packages.csproj");
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "netstandard2.1"),
                new XElement("NuGetAudit", "false"), new XElement("RestorePackagesPath", Path.Combine(root, ".obf/cache"))),
            new XElement("ItemGroup", references.Select(x => new XElement("PackageReference",
                new XAttribute("Include", x.Key), new XAttribute("Version", $"[{x.Value}]")))))).Save(project);
        ToolProcess.Run("dotnet", new[] { "restore", project, "--configfile", layout.Config,
            "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false",
            "-p:DirectoryPackagesPropsPath=", "-p:ManagePackageVersionsCentrally=false", "-p:ArtifactsPath=", "--verbosity", "minimal" }, root, ct);
        using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(work, "obj/project.assets.json")));
        var resolved = assets.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(x => x.Value.GetProperty("type").GetString() == "package")
            .Select(x => x.Name.Split('/')).ToDictionary(x => x[0], x => x[1], StringComparer.OrdinalIgnoreCase);
        foreach (var package in resolved.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entry = existing.Root.Elements("package").FirstOrDefault(x => string.Equals((string?)x.Attribute("id"), package.Key, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new XElement("package", new XAttribute("id", package.Key));
                existing.Root.Add(entry);
                if (package.Key is not ("ObFramework" or "ObFramework.Adapter.Unity")) entry.SetAttributeValue("manuallyInstalled", "false");
            }
            entry.SetAttributeValue("version", package.Value);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(layout.Packages)!);
        existing.Save(layout.Packages);

        var tools = Path.Combine(root, ".obf/tools/nugetforunity", UnityProject.NuGetForUnityVersion, "net8.0");
        var executable = Path.Combine(tools, OperatingSystem.IsWindows() ? "nugetforunity.exe" : "nugetforunity");
        if (!File.Exists(executable))
            ToolProcess.Run("dotnet", new[] { "tool", "install", "NuGetForUnity.Cli", "--version", UnityProject.NuGetForUnityVersion, "--framework", "net8.0",
                "--tool-path", tools, "--configfile", layout.Config }, root, ct);
        ToolProcess.Run(executable, new[] { "restore", root }, root, ct);
        Console.WriteLine($"Init: restored {resolved.Count} NuGet packages (ObFramework {version}).");
    }
}
