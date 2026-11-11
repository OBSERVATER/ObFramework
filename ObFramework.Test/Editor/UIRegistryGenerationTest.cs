using ObFramework.Editor.Core;

namespace ObFramework.Test.Editor;

[TestFixture]
public class UIRegistryGenerationTest
{
    private static UIRegistryGenerationInput Input(params UIRegistryEntry[] entries)
    {
        var input = new UIRegistryGenerationInput { Setup = EditorFixtures.Snapshot() };
        foreach (var entry in entries)
            input.Entries.Add(entry);
        return input;
    }

    private static UIRegistryEntry Entry(string viewType, string windowType, string? resourceKeyOverride = null)
    {
        return new UIRegistryEntry
        {
            ViewTypeFullName = viewType,
            WindowTypeFullName = windowType,
            ResourceKeyOverride = resourceKeyOverride,
        };
    }

    [Test]
    public void RegisterAllCallsTheUnityAdapterWithFormatBasedResourceKeys()
    {
        var input = Input(
            Entry("Game.UI.ShopView", "Game.UI.ShopWindow"),
            Entry("Game.UI.SettingsView", "Game.UI.SettingsWindow"));

        var output = UIRegistrySourceGenerator.Generate(input);

        Assert.That(output.Success, Is.True, EditorFixtures.FormatDiagnostics(output.Diagnostics));
        Assert.That(output.File, Is.Not.Null);
        Assert.That(output.File!.Path, Is.EqualTo("Assets/ObFramework/Generated/UI/GeneratedUIRegistry.g.cs"));
        Assert.That(output.File.Content, Does.Contain("namespace Game.Generated.UI"));
        Assert.That(output.File.Content, Does.Contain("public static class GeneratedUIRegistry"));
        Assert.That(output.File.Content, Does.Contain(
            "public static void RegisterAll(global::ObFramework.Adapter.Unity.UI.UnityUIAdapter adapter)"));
        Assert.That(output.File.Content, Does.Contain(
            "adapter.Register<global::Game.UI.ShopView, global::Game.UI.ShopWindow>(\"UI/ShopView\");"));
        Assert.That(output.File.Content, Does.Contain(
            "adapter.Register<global::Game.UI.SettingsView, global::Game.UI.SettingsWindow>(\"UI/SettingsView\");"));
        Assert.That(output.Registrations, Has.Count.EqualTo(2));
        Assert.That(output.File.Content, Does.Not.Contain("D:/GameProject"));
    }

    [Test]
    public void SetupOverridesWinOverTheFormatRule()
    {
        var input = new UIRegistryGenerationInput
        {
            Setup = EditorFixtures.Snapshot(("ShopView", "UI/Shop")),
        };
        input.Entries.Add(Entry("Game.UI.ShopView", "Game.UI.ShopWindow"));
        input.Entries.Add(Entry("Game.UI.SettingsView", "Game.UI.SettingsWindow"));

        var output = UIRegistrySourceGenerator.Generate(input);

        Assert.That(output.Success, Is.True, EditorFixtures.FormatDiagnostics(output.Diagnostics));
        Assert.That(output.Registrations.Single(r => r.ViewTypeShortName == "ShopView").ResourceKey, Is.EqualTo("UI/Shop"));
        Assert.That(output.Registrations.Single(r => r.ViewTypeShortName == "SettingsView").ResourceKey, Is.EqualTo("UI/SettingsView"));
        Assert.That(output.File!.Content, Does.Contain("(\"UI/Shop\");"));
    }

    [Test]
    public void EntryOverrideWinsOverSetupOverridesAndFormat()
    {
        var input = new UIRegistryGenerationInput
        {
            Setup = EditorFixtures.Snapshot(("ShopView", "UI/SetupShop")),
        };
        input.Entries.Add(Entry("Game.UI.ShopView", "Game.UI.ShopWindow", "UI/EntryShop"));

        var output = UIRegistrySourceGenerator.Generate(input);

        Assert.That(output.Success, Is.True, EditorFixtures.FormatDiagnostics(output.Diagnostics));
        Assert.That(output.Registrations.Single().ResourceKey, Is.EqualTo("UI/EntryShop"));
    }

    [Test]
    public void DuplicateViewMappingIsAnErrorAndPreventsEmission()
    {
        var input = Input(
            Entry("Game.UI.ShopView", "Game.UI.ShopWindow"),
            Entry("Game.UI.ShopView", "Game.UI.OtherShopWindow"));

        var output = UIRegistrySourceGenerator.Generate(input);

        Assert.That(output.Success, Is.False);
        Assert.That(output.File, Is.Null);
        var diagnostic = EditorFixtures.SingleCode(output.Diagnostics, EditorDiagnosticCodes.DuplicateViewMapping);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(EditorDiagnosticSeverity.Error));
    }

    [TestCase("Game.UI.ShopView", "Game.UI.ShopView")]
    [TestCase("Game.UI.ShopView<T>", "Game.UI.ShopWindow")]
    [TestCase("ShopView", "")]
    public void InvalidRegistryEntriesAreErrors(string viewType, string windowType)
    {
        var output = UIRegistrySourceGenerator.Generate(Input(Entry(viewType, windowType)));

        Assert.That(output.Success, Is.False);
        Assert.That(output.File, Is.Null);
        Assert.That(EditorFixtures.SingleCode(output.Diagnostics, EditorDiagnosticCodes.InvalidRegistryEntry), Is.Not.Null);
    }

    [Test]
    public void DuplicateResourceKeyIsAWarningButAllEntriesAreEmitted()
    {
        var input = Input(
            Entry("Game.UI.ShopView", "Game.UI.ShopWindow", "UI/Shared"),
            Entry("Game.UI.SettingsView", "Game.UI.SettingsWindow", "UI/Shared"));

        var output = UIRegistrySourceGenerator.Generate(input);

        Assert.That(output.Success, Is.True, EditorFixtures.FormatDiagnostics(output.Diagnostics));
        Assert.That(output.Registrations, Has.Count.EqualTo(2));
        var diagnostic = EditorFixtures.SingleCode(output.Diagnostics, EditorDiagnosticCodes.DuplicateResourceKey);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(EditorDiagnosticSeverity.Warning));
    }

    [Test]
    public void EmptyEntryListEmitsAValidEmptyRegistryWithWarning()
    {
        var output = UIRegistrySourceGenerator.Generate(Input());

        Assert.That(output.Success, Is.True, EditorFixtures.FormatDiagnostics(output.Diagnostics));
        Assert.That(output.File, Is.Not.Null);
        Assert.That(output.File!.Content, Does.Contain("No generated View/window pairs were found"));
        Assert.That(EditorFixtures.SingleCode(output.Diagnostics, EditorDiagnosticCodes.RegistryHasNoEntries), Is.Not.Null);
    }

    [Test]
    public void UnusedSetupOverrideIsAWarning()
    {
        var input = new UIRegistryGenerationInput
        {
            Setup = EditorFixtures.Snapshot(("MissingView", "UI/Missing")),
        };
        input.Entries.Add(Entry("Game.UI.ShopView", "Game.UI.ShopWindow"));

        var output = UIRegistrySourceGenerator.Generate(input);

        Assert.That(output.Success, Is.True, EditorFixtures.FormatDiagnostics(output.Diagnostics));
        Assert.That(EditorFixtures.SingleCode(output.Diagnostics, EditorDiagnosticCodes.UnusedResourceKeyOverride), Is.Not.Null);
    }

    [Test]
    public void CustomNamespaceClassNameMethodAdapterTypeAndOutputPathAreRespected()
    {
        var input = new UIRegistryGenerationInput
        {
            Setup = EditorFixtures.Snapshot(),
            Namespace = "Game.Registry",
            ClassName = "MyUiRegistry",
            MethodName = "Install",
            AdapterTypeName = "global::My.Adapter",
            OutputPath = "Assets/Generated/MyUiRegistry.g.cs",
        };
        input.Entries.Add(Entry("Game.UI.ShopView", "Game.UI.ShopWindow"));

        var output = UIRegistrySourceGenerator.Generate(input);

        Assert.That(output.Success, Is.True, EditorFixtures.FormatDiagnostics(output.Diagnostics));
        Assert.That(output.File!.Path, Is.EqualTo("Assets/Generated/MyUiRegistry.g.cs"));
        Assert.That(output.File.Content, Does.Contain("namespace Game.Registry"));
        Assert.That(output.File.Content, Does.Contain("public static class MyUiRegistry"));
        Assert.That(output.File.Content, Does.Contain("public static void Install(global::My.Adapter adapter)"));
    }

    [Test]
    public void EscapingOutputPathIsAnError()
    {
        var input = Input(Entry("Game.UI.ShopView", "Game.UI.ShopWindow"));
        input.OutputPath = "../Outside/Registry.g.cs";

        var output = UIRegistrySourceGenerator.Generate(input);

        Assert.That(output.Success, Is.False);
        Assert.That(output.File, Is.Null);
        Assert.That(EditorFixtures.SingleCode(output.Diagnostics, EditorDiagnosticCodes.InvalidOutputPath), Is.Not.Null);
    }

    [Test]
    public void UnchangedExistingSourceIsReportedAsUnmodified()
    {
        var input = Input(Entry("Game.UI.ShopView", "Game.UI.ShopWindow"));
        var first = UIRegistrySourceGenerator.Generate(input);
        Assert.That(first.Success, Is.True, EditorFixtures.FormatDiagnostics(first.Diagnostics));

        input.ExistingSource = first.File!.Content;
        var second = UIRegistrySourceGenerator.Generate(input);

        Assert.That(second.Success, Is.True, EditorFixtures.FormatDiagnostics(second.Diagnostics));
        Assert.That(second.File!.IsModified, Is.False);
        Assert.That(second.File.Content, Is.EqualTo(first.File.Content));
    }

    [Test]
    public void MissingSetupSnapshotIsAnError()
    {
        var input = new UIRegistryGenerationInput { Setup = null };
        input.Entries.Add(Entry("Game.UI.ShopView", "Game.UI.ShopWindow"));

        var output = UIRegistrySourceGenerator.Generate(input);

        Assert.That(output.Success, Is.False);
        Assert.That(output.File, Is.Null);
        Assert.That(output.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void InvalidAdapterTypeNameIsAnError()
    {
        var input = Input(Entry("Game.UI.ShopView", "Game.UI.ShopWindow"));
        input.AdapterTypeName = "My Adapter";

        var output = UIRegistrySourceGenerator.Generate(input);

        Assert.That(output.Success, Is.False);
        Assert.That(output.File, Is.Null);
    }
}
