using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using ObFramework.Cli.Commands.Config;
using ObFramework.Cli.Deployment;

namespace ObFramework.Cli.Unity;

internal sealed record DoctorCheck(string Id, string Status, string Message);

internal static class UnityDoctor
{
    public static List<DoctorCheck> Check(string root)
    {
        var checks = new List<DoctorCheck>();
        void Check(string id, Action action)
        {
            try { action(); checks.Add(new(id, "pass", "OK")); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or
                System.ComponentModel.Win32Exception or UnauthorizedAccessException or SetupConfigException or DeploymentException or System.Text.Json.JsonException or System.Xml.XmlException or BadImageFormatException)
            { checks.Add(new(id, "fail", ex.Message)); }
        }
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        Check("OBD001", () => UnityProject.Validate(root));
        if (checks.Any(x => x.Status == "fail")) return checks;
        Check("OBD002", () => UnityProject.ValidateSdk(root, CancellationToken.None));
        Check("OBD010", () =>
        {
            var setup = SetupJsonDocument.Load(Path.Combine(root, "setup.json"));
            Require(string.Equals(setup.GetRootValue(), root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "setup.json root differs from this project; update it explicitly.");
            var registry = ConfigCliModule.CreateDefaultRegistry();
            foreach (var module in registry.GetModuleNames().Select(registry.Get))
            {
                var config = setup.GetModuleConfig(module);
                if (config == null) continue;
                var result = module.NormalizePaths(root, config);
                Require(result.IsValid, string.Join("; ", result.Errors));
            }
        });
        Check("OBD020", () =>
        {
            var dependencies = UnityProject.LoadObject(Path.Combine(root, "Packages/manifest.json"))["dependencies"];
            Require(dependencies?[UnityProject.NuGetForUnityId] != null || dependencies?["com.glitchenzo.nugetforunity"] != null, "NuGetForUnity is missing; run ob init.");
        });
        Check("OBD030", () =>
        {
            var layout = new NuGetLayout(root);
            Require(File.Exists(layout.Config) && File.Exists(layout.Packages), "NuGet configuration/package list is missing; run ob init.");
            var packages = XDocument.Load(layout.Packages).Root!.Elements("package").ToArray();
            foreach (var id in new[] { "ObFramework", "ObFramework.Adapter.Unity" })
            {
                var entry = packages.SingleOrDefault(x => string.Equals((string?)x.Attribute("id"), id, StringComparison.OrdinalIgnoreCase));
                Require(entry != null, $"{id} is absent from packages.config.");
                var version = (string?)entry!.Attribute("version");
                var dll = FindDll(layout.Repository, id + ".dll");
                Require(dll.Length == 1, $"Expected one installed {id}.dll; found {dll.Length}. Restore packages/remove duplicates.");
                var selected = UnityProject.LoadObject(Path.Combine(root, "setup.json"))["modules"]?["installation"]?["frameworkVersion"]?.GetValue<string>();
                Require(selected == null || version == selected, $"{id} {version} differs from selected framework version {selected}.");
                Require(!AssemblyReferences(dll[0]).Any(x => x is "Unity.Addressables" or "Unity.ResourceManager"), $"{id}.dll still depends on Addressables; rebuild/install the decoupled package.");
            }
            foreach (var package in packages)
            {
                var folder = Path.Combine(layout.Repository, $"{(string?)package.Attribute("id")}.{(string?)package.Attribute("version")}");
                Require(Directory.Exists(folder), $"Package is not restored: {folder}");
            }
        });
        Check("OBD040", () =>
        {
            var owned = OwnedDeploymentManifest.TryLoad(Path.Combine(root, DeploymentManifest.DefaultOwnedManifestPath));
            Require(owned != null, "Editor deployment manifest is missing; run ob init.");
            foreach (var file in owned!.Files)
            {
                var full = Path.Combine(root, file.Path);
                Require(File.Exists(full), $"Deployment file is missing: {file.Path}");
                Require(DeploymentHash.ComputeSha256(File.ReadAllBytes(full)) == file.Sha256, $"Deployment file changed: {file.Path}");
            }
            var source = EmbeddedDeployableFileSource.CreateDefault();
            foreach (var file in DeploymentManifest.LoadEmbeddedPlan().ResolveFiles(source))
                Require(owned.Files.Any(x => x.Path == file.Target), $"Required deployment file is absent: {file.Target}");
            foreach (var name in new[] { "ObFramework.Generator", "ObFramework.Generator.Unity" })
            {
                var dll = Path.Combine(root, "Assets/ObFramework/Analyzers", name + ".dll");
                Require(File.Exists(dll), $"Missing generator: {name}");
                Require(File.Exists(dll + ".meta") && File.ReadAllText(dll + ".meta").Contains("- RoslynAnalyzer"), $"Missing RoslynAnalyzer label: {name}");
            }
        });
        Check("OBD050", () =>
        {
            var layout = new NuGetLayout(root);
            var roots = new[] { Path.Combine(root, "Assets"), Path.Combine(root, "Packages") };
            var duplicates = roots.Where(Directory.Exists).SelectMany(x => Directory.EnumerateFiles(x, "*.dll", SearchOption.AllDirectories))
                .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1 &&
                    (x.Key!.StartsWith("ObFramework", StringComparison.Ordinal) || x.Key is "Unity.Addressables.dll" or "Unity.ResourceManager.dll"));
            Require(!duplicates.Any(), "Duplicate framework/provider DLLs: " + string.Join(", ", duplicates.Select(x => x.Key)));
            var bundledEngine = FindDll(layout.Repository, "UnityEngine*.dll").Concat(FindDll(layout.Repository, "UnityEditor*.dll"));
            Require(!bundledEngine.Any(), "NuGet packages contain Unity engine DLLs; remove the bundled copies.");
        });
        Check("OBD060", () =>
        {
            var choice = UnityProject.ResourceChoice(root);
            if (choice != "addressables") return;
            var version = UnityProject.LoadObject(Path.Combine(root, "Packages/manifest.json"))["dependencies"]?[UnityProject.AddressablesId]?.GetValue<string>();
            Require(Version.TryParse(version, out var parsed) && parsed.Major == 1 && parsed.Minor == 21, "Addressables integration requires a supported 1.21.x package.");
            var directory = Path.Combine(root, UnityIntegrationInstaller.IntegrationRoot);
            Require(File.Exists(Path.Combine(directory, "AddressablesAssetAdapter.cs")) && File.Exists(Path.Combine(directory, "ObFramework.Integrations.Addressables.asmdef")), "Addressables integration source/asmdef is missing.");
        });
        var setupPath = Path.Combine(root, "setup.json");
        if (File.Exists(setupPath)) Check("OBD070", () =>
        {
            var luban = UnityProject.LoadObject(setupPath)["modules"]?["luban"];
            foreach (var key in new[] { "lubanDll", "agentDll" })
            {
                var path = luban?[key]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(path)) Require(File.Exists(Path.Combine(root, path)), $"Luban tool missing: {path}; retry ob init when network access is available.");
            }
        });
        if (File.Exists(setupPath))
        {
            try
            {
                if (UnityProject.LoadObject(setupPath)["modules"]?["installation"]?["skipLubanDownload"]?.GetValue<bool>() == true)
                {
                    var index = checks.FindIndex(x => x.Id == "OBD070" && x.Status == "fail");
                    if (index >= 0) checks[index] = checks[index] with { Status = "warning", Message = checks[index].Message + " (Luban download was skipped.)" };
                }
            }
            catch (System.Text.Json.JsonException) { /* OBD010 already reports invalid configuration. */ }
        }
        return checks;
    }

    private static string[] FindDll(string root, string pattern) => Directory.Exists(root)
        ? Directory.GetFiles(root, pattern, SearchOption.AllDirectories) : Array.Empty<string>();

    public static string[] AssemblyReferences(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        return reader.AssemblyReferences.Select(x => reader.GetString(reader.GetAssemblyReference(x).Name)).ToArray();
    }
}
