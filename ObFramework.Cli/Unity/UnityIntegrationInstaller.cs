using System.Text;
using System.Text.Json.Nodes;
using ObFramework.Cli.Deployment;

namespace ObFramework.Cli.Unity;

internal static class UnityIntegrationInstaller
{
    public const string IntegrationRoot = "Assets/ObFramework/Integrations/Addressables";

    public static void Install(string root, string choice)
    {
        if (choice == "custom") return;
        var source = new EmbeddedDeployableFileSource(resourcePrefix: "ObFramework.Cli.Addressables");
        var files = source.ListFiles();
        if (files.Count == 0) throw new DeploymentException("Addressables source payload is missing; rebuild the CLI.");
        var plan = new DeploymentManifest
        {
            ManifestPath = IntegrationRoot + "/obf-integration.json", TargetRoot = IntegrationRoot,
            Deployment = "unity-addressables", AutoDeployResources = true, RequireComplete = true
        };
        var result = new UnityEditorDeployer(source, plan).Deploy(root);
        if (result.Warnings.Count != 0) throw new DeploymentException(string.Join(Environment.NewLine, result.Warnings));
        Console.WriteLine("Init: Addressables integration source ready.");
    }

    public static void SaveSelection(string root, string choice, string? version, bool skipLuban = false)
    {
        var path = Path.Combine(root, "setup.json");
        var setup = UnityProject.LoadObject(path);
        var modules = setup["modules"] as JsonObject;
        if (modules == null) setup["modules"] = modules = new JsonObject();
        var installation = modules["installation"] as JsonObject;
        if (installation == null) modules["installation"] = installation = new JsonObject();
        installation["resources"] = choice;
        installation["skipLubanDownload"] = skipLuban;
        if (version != null) installation["frameworkVersion"] = version;
        UnityProject.SaveObject(path, setup);
    }

    public static void InstallGuide(string root)
    {
        var path = Path.Combine(root, "Assets/ObFramework/README.md");
        if (File.Exists(path)) return;
        using var stream = typeof(UnityIntegrationInstaller).Assembly.GetManifestResourceStream("ObFramework.Cli.UnityGuide.md")
            ?? throw new InvalidOperationException("Unity quick start resource is missing.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new StreamReader(stream, Encoding.UTF8).ReadToEnd());
    }
}
