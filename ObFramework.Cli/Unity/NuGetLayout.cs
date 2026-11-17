using System.Xml.Linq;

namespace ObFramework.Cli.Unity;

internal sealed class NuGetLayout
{
    public string Config { get; }
    public string Packages { get; }
    public string Repository { get; }

    public NuGetLayout(string root)
    {
        var upmConfig = Path.Combine(root, "Packages/nuget-packages/NuGet.config");
        var inPackages = File.Exists(upmConfig);
        Config = inPackages ? upmConfig : Path.Combine(root, "Assets/NuGet.config");
        var document = File.Exists(Config) ? XDocument.Load(Config) : null;
        string? Setting(string key) => document?.Root?.Element("config")?.Elements("add")
            .FirstOrDefault(x => string.Equals((string?)x.Attribute("key"), key, StringComparison.OrdinalIgnoreCase))?.Attribute("value")?.Value;
        var basis = Path.GetDirectoryName(Config)!;
        Packages = inPackages ? Path.Combine(basis, "packages.config") : Path.GetFullPath(Path.Combine(basis, Environment.ExpandEnvironmentVariables(Setting("PackagesConfigDirectoryPath") ?? "."), "packages.config"));
        Repository = inPackages ? Path.Combine(basis, "InstalledPackages") : Path.GetFullPath(Path.Combine(basis, Environment.ExpandEnvironmentVariables(Setting("repositoryPath") ?? "Packages")));
    }

    public void Configure(string? source)
    {
        var document = File.Exists(Config) ? XDocument.Load(Config) : new XDocument(new XElement("configuration",
            new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"))),
            new XElement("activePackageSource", new XElement("add", new XAttribute("key", "All"), new XAttribute("value", "(Aggregate source)"))),
            new XElement("config", new XElement("add", new XAttribute("key", "repositoryPath"), new XAttribute("value", "./Packages")))));
        var configuration = document.Root ?? throw new InvalidOperationException("NuGet.config has no configuration element.");
        if (source != null)
        {
            var sources = configuration.Element("packageSources");
            if (sources == null) { sources = new XElement("packageSources"); configuration.Add(sources); }
            sources.Elements("add").Where(x => (string?)x.Attribute("key") == "ObFramework").Remove();
            sources.Add(new XElement("add", new XAttribute("key", "ObFramework"), new XAttribute("value", source)));
            var active = configuration.Element("activePackageSource");
            if (active == null) { active = new XElement("activePackageSource"); configuration.Add(active); }
            active.RemoveNodes();
            active.Add(new XElement("add", new XAttribute("key", "All"), new XAttribute("value", "(Aggregate source)")));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
        document.Save(Config);
    }
}
