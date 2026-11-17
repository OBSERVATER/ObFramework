using System.Text.Json.Nodes;
using ObFramework.Cli.Unity;
using ObFramework.Cli.Deployment;

namespace ObFramework.Test.Cli;

[TestFixture]
public sealed class UnityInstallationTest
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "obf-install-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Assets"));
        Directory.CreateDirectory(Path.Combine(_root, "ProjectSettings"));
        File.WriteAllText(Path.Combine(_root, "ProjectSettings/ProjectVersion.txt"), "m_EditorVersion: 2022.3.58f1\n");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public void DefaultInstallationDoesNotAddAddressablesAndPreservesManifest()
    {
        UnityProject.SaveObject(Path.Combine(_root, "Packages/manifest.json"), new JsonObject
        {
            ["dependencies"] = new JsonObject { ["com.example.package"] = "1.2.3" },
            ["scopedRegistries"] = new JsonArray()
        });
        UnityProject.ConfigureUpm(_root, "custom");
        UnityIntegrationInstaller.Install(_root, "custom");
        var path = Path.Combine(_root, "Packages/manifest.json");
        var first = File.ReadAllText(path);
        UnityProject.ConfigureUpm(_root, "custom");
        Assert.That(File.ReadAllText(path), Is.EqualTo(first));
        var manifest = UnityProject.LoadObject(path);
        Assert.That(manifest["dependencies"]!["com.example.package"]!.GetValue<string>(), Is.EqualTo("1.2.3"));
        Assert.That(manifest["dependencies"]![UnityProject.AddressablesId], Is.Null);
        Assert.That(Directory.Exists(Path.Combine(_root, UnityIntegrationInstaller.IntegrationRoot)), Is.False);
    }

    [Test]
    public void ScopedRegistryMergePreservesOtherScopesAndDoesNotDuplicateRegistry()
    {
        var path = Path.Combine(_root, "Packages/manifest.json");
        UnityProject.SaveObject(path, new JsonObject
        {
            ["dependencies"] = new JsonObject(),
            ["scopedRegistries"] = new JsonArray(new JsonObject
            {
                ["name"] = "Existing OpenUPM", ["url"] = "https://package.openupm.com/",
                ["scopes"] = new JsonArray("com.example")
            })
        });
        UnityProject.ConfigureUpm(_root, "custom");
        UnityProject.ConfigureUpm(_root, "custom");
        var manifest = UnityProject.LoadObject(path);
        var registries = (JsonArray)manifest["scopedRegistries"]!;
        Assert.That(registries.Count, Is.EqualTo(1));
        var scopes = ((JsonArray)registries[0]!["scopes"]!).Select(x => x!.GetValue<string>()).ToArray();
        Assert.That(scopes, Is.EquivalentTo(new[] { "com.example", UnityProject.NuGetForUnityId }));
        Assert.That(manifest["dependencies"]![UnityProject.NuGetForUnityId]!.GetValue<string>(), Is.EqualTo(UnityProject.NuGetForUnityVersion));
    }

    [Test]
    public void ExistingNugetForUnitySelectionIsPreserved()
    {
        var path = Path.Combine(_root, "Packages/manifest.json");
        UnityProject.SaveObject(path, new JsonObject { ["dependencies"] = new JsonObject { [UnityProject.NuGetForUnityId] = "file:../custom-nugetforunity" } });
        UnityProject.ConfigureUpm(_root, "custom");
        Assert.That(UnityProject.LoadObject(path)["dependencies"]![UnityProject.NuGetForUnityId]!.GetValue<string>(), Is.EqualTo("file:../custom-nugetforunity"));
    }

    [Test]
    public void OptionalIntegrationDeploysSourceAndRetainsCompatibleVersion()
    {
        UnityProject.SaveObject(Path.Combine(_root, "Packages/manifest.json"), new JsonObject
        {
            ["dependencies"] = new JsonObject { [UnityProject.AddressablesId] = "1.21.20" }
        });
        UnityProject.ConfigureUpm(_root, "addressables");
        UnityIntegrationInstaller.Install(_root, "addressables");
        UnityIntegrationInstaller.Install(_root, "addressables");
        Assert.That(UnityProject.LoadObject(Path.Combine(_root, "Packages/manifest.json"))["dependencies"]![UnityProject.AddressablesId]!.GetValue<string>(), Is.EqualTo("1.21.20"));
        Assert.That(File.Exists(Path.Combine(_root, UnityIntegrationInstaller.IntegrationRoot, "AddressablesAssetAdapter.cs")), Is.True);
        Assert.That(File.Exists(Path.Combine(_root, UnityIntegrationInstaller.IntegrationRoot, "ObFramework.Integrations.Addressables.asmdef")), Is.True);
        var source = EmbeddedDeployableFileSource.CreateDefault();
        Assert.That(source.ListFiles().Any(x => x.Contains("AddressablesAssetAdapter")), Is.False);
    }

    [Test]
    public void ExistingProviderSelectionIsPreservedAndSwitchRequiresExplicitCleanup()
    {
        UnityIntegrationInstaller.SaveSelection(_root, "addressables", "0.1.0-preview.1");
        Assert.That(UnityProject.ResourceChoice(_root, null), Is.EqualTo("addressables"));
        Assert.Throws<InvalidOperationException>(() => UnityProject.ResourceChoice(_root, "custom"));
    }

    [Test]
    public void IncompatibleAddressablesIsNotOverwritten()
    {
        var path = Path.Combine(_root, "Packages/manifest.json");
        UnityProject.SaveObject(path, new JsonObject { ["dependencies"] = new JsonObject { [UnityProject.AddressablesId] = "2.3.0" } });
        var original = File.ReadAllText(path);
        Assert.Throws<InvalidOperationException>(() => UnityProject.ConfigureUpm(_root, "addressables"));
        Assert.That(File.ReadAllText(path), Is.EqualTo(original));
    }

    [Test]
    public void MissingRequiredPayloadFailsBeforeWritingAnyFiles()
    {
        var source = EmbeddedDeployableFileSource.CreateDefault();
        var plan = DeploymentManifest.LoadEmbeddedPlan();
        Assert.That(source.ListFiles(), Does.Contain("Analyzers/ObFramework.Generator.dll"));
        Assert.That(source.ListFiles(), Does.Contain("Analyzers/ObFramework.Generator.Unity.dll"));
        Assert.That(source.ListFiles(), Does.Contain("Analyzers/ObFramework.Generator.dll.meta"));
        Assert.That(source.ListFiles(), Does.Contain("Analyzers/ObFramework.Generator.Unity.dll.meta"));
        var broken = new DeploymentManifest { RequireComplete = true, Files = new[]
        {
            new DeployableFile("Analyzers/ObFramework.Generator.dll", "Assets/a.dll"),
            new DeployableFile("missing.dll", "Assets/b.dll")
        }};
        Assert.Throws<DeploymentException>(() => new UnityEditorDeployer(source, broken).Deploy(_root));
        Assert.That(File.Exists(Path.Combine(_root, "Assets/a.dll")), Is.False);
        new UnityEditorDeployer(source, plan).Deploy(_root);
        var analyzer = Path.Combine(_root, "Assets/ObFramework/Analyzers/ObFramework.Generator.dll");
        File.WriteAllText(analyzer, "user changed content");
        Assert.Throws<DeploymentException>(() => new UnityEditorDeployer(source, plan).Deploy(_root));
        Assert.That(File.ReadAllText(analyzer), Is.EqualTo("user changed content"));
    }

    [Test]
    public void NugetLayoutHonorsCustomPackagesConfigDirectory()
    {
        File.WriteAllText(Path.Combine(_root, "Assets/NuGet.config"), "<configuration><config><add key=\"PackagesConfigDirectoryPath\" value=\"Config\"/><add key=\"repositoryPath\" value=\"Dependencies\"/></config></configuration>");
        var layout = new NuGetLayout(_root);
        Assert.That(layout.Packages, Is.EqualTo(Path.GetFullPath(Path.Combine(_root, "Assets/Config/packages.config"))));
        Assert.That(layout.Repository, Is.EqualTo(Path.GetFullPath(Path.Combine(_root, "Assets/Dependencies"))));
    }
}
