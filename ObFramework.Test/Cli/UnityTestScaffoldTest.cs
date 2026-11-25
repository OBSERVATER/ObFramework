using System.Text.Json.Nodes;
using System.Xml.Linq;
using ObFramework.Cli.Unity;

namespace ObFramework.Test.Cli;

[TestFixture]
public class UnityTestScaffoldTest
{
    private string _root = null!;

    [SetUp]
    public void CreateProject()
    {
        _root = Path.Combine(Path.GetTempPath(), "obf-scaffold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Assets"));
    }

    [TearDown]
    public void RemoveProject() => Directory.Delete(_root, recursive: true);

    [Test]
    public void NewProjectUsesSharedBusinessCompilationAndPreservesEditsOnRetry()
    {
        Install("1.0.0");

        var definition = JsonNode.Parse(File.ReadAllText(At("Assets/Scripts/Core/Game.Core.asmdef")))!;
        Assert.That(definition["noEngineReferences"]!.GetValue<bool>(), Is.True);
        Assert.That(File.Exists(At("Assets/Scripts/Core/GameApplication.cs")), Is.True);
        Assert.That(File.Exists(At("Tests/Game.Core/Game.Core.csproj")), Is.True);
        Assert.That(File.Exists(At("Tests/Game.Core.Tests/Game.Core.Tests.csproj")), Is.True);

        var test = At("Tests/Game.Core.Tests/GameApplicationTests.cs");
        File.AppendAllText(test, "// Game-owned change");
        File.WriteAllText(At("Assets/NuGet.config"), "<configuration />");
        File.WriteAllText(At("Assets/packages.config"), """
            <packages><package id="ObFramework" version="2.0.0" /></packages>
            """);

        Install("9.0.0");

        Assert.That(File.ReadAllText(test), Does.EndWith("// Game-owned change"));
        var settings = XDocument.Load(At("Tests/ObFramework.Unity.props"));
        Assert.That(settings.Descendants("ObFrameworkPackageVersion").Single().Value, Is.EqualTo("[2.0.0]"));
        Assert.That(settings.Descendants("RestoreConfigFile").Single().Value, Does.EndWith("../Assets/NuGet.config"));
    }

    [Test]
    public void PreviouslyInitializedProjectUsesTheSameFixedDirectory()
    {
        File.WriteAllText(At("setup.json"), """{"modules":{"installation":{"resources":"custom"}}}""");
        var unrelatedSource = At("Assets/Other/Core");
        Directory.CreateDirectory(unrelatedSource);
        var unrelatedDefinition = Path.Combine(unrelatedSource, "Other.Core.asmdef");
        const string definition = """{"name":"Other.Core","noEngineReferences":true}""";
        File.WriteAllText(unrelatedDefinition, definition);

        Install("1.0.0");

        Assert.That(File.Exists(At("Assets/Scripts/Core/Game.Core.asmdef")), Is.True);
        Assert.That(File.Exists(At("Tests/Game.Core.Tests/GameApplicationTests.cs")), Is.True);
        Assert.That(File.ReadAllText(unrelatedDefinition), Is.EqualTo(definition));
    }

    [Test]
    public void BootstrapInstallsEditableLauncherAndPrefabWithStableMetadata()
    {
        Install("1.0.0");
        UnityBootstrapInstaller.Install(_root);
        var launcher = At(UnityBootstrapInstaller.LauncherFile);
        var prefab = At(UnityBootstrapInstaller.PrefabFile);
        var meta = prefab + ".meta";
        var launcherSource = File.ReadAllText(launcher);
        Assert.That(launcherSource, Does.Contain("new GameApplication(ConfigurePlugins())"));
        Assert.That(launcherSource, Does.Contain("IPlugin[] ConfigurePlugins()"));
        Assert.That(launcherSource, Does.Contain("new ClockPlugin(1f / 30f)"));
        Assert.That(File.ReadAllText(prefab), Does.Contain("m_Name: Popup"));
        Assert.That(File.ReadAllText(prefab), Does.Not.Contain("m_ActionsAsset"));
        Assert.That(File.ReadAllText(meta), Does.Contain("guid:"));

        foreach (var path in new[] { launcher, prefab, meta })
            File.AppendAllText(path, "\n# Project edit");
        UnityBootstrapInstaller.Install(_root);
        foreach (var path in new[] { launcher, prefab, meta })
            Assert.That(File.ReadAllText(path), Does.EndWith("# Project edit"));
    }

    [Test]
    public void BootstrapPreservesAnExistingApplicationAndOffersAnEditableLauncherTemplate()
    {
        Install("1.0.0");
        var application = At("Assets/Scripts/Core/GameApplication.cs");
        const string existingSource = "// Existing application source";
        File.WriteAllText(application, existingSource);

        Install("1.0.0");
        UnityBootstrapInstaller.Install(_root);

        Assert.That(File.ReadAllText(application), Is.EqualTo(existingSource));
        Assert.That(File.Exists(At(UnityBootstrapInstaller.LauncherFile)), Is.False);
        Assert.That(File.Exists(At(UnityBootstrapInstaller.LauncherFile + ".txt")), Is.True);
        Assert.That(File.Exists(At(UnityBootstrapInstaller.PrefabFile)), Is.True);
    }

    private void Install(string version)
    {
        UnityTestScaffold.Validate(_root);
        UnityTestScaffold.Install(_root, version);
    }

    private string At(string path) => Path.Combine(_root, path);
}
