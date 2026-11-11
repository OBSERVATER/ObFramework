using ObFramework.Editor.Core;

namespace ObFramework.Test.Editor;

[TestFixture]
public class UISetupSnapshotTest
{
    [Test]
    public void ValidSectionMapsRootModulePathsAndResourceKeys()
    {
        var result = UISetupSnapshotFactory.Create(EditorFixtures.Section(overrides: new[] { ("ShopView", "UI/Shop") }));

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        var snapshot = result.Snapshot!;
        Assert.That(snapshot.Root, Is.EqualTo("D:/GameProject"));
        Assert.That(snapshot.ScriptOutputRoot, Is.EqualTo("Assets/ObFramework/Generated/UI"));
        Assert.That(snapshot.UIAssetRoot, Is.EqualTo("Assets/ObFramework/UI"));
        Assert.That(snapshot.ResourceKeyFormat, Is.EqualTo("UI/{view}"));
        Assert.That(snapshot.ScriptOutputDirectory, Is.EqualTo("D:/GameProject/Assets/ObFramework/Generated/UI"));
        Assert.That(snapshot.UIAssetDirectory, Is.EqualTo("D:/GameProject/Assets/ObFramework/UI"));
        Assert.That(snapshot.ResolvePath("Assets/ObFramework/UI/Shop.prefab"),
            Is.EqualTo("D:/GameProject/Assets/ObFramework/UI/Shop.prefab"));
        Assert.That(snapshot.ResolveResourceKey("ShopView"), Is.EqualTo("UI/Shop"));
        Assert.That(snapshot.ResolveResourceKey("SettingsView"), Is.EqualTo("UI/SettingsView"));
    }

    [Test]
    public void MissingOptionalFieldsUseConfigCliDefaults()
    {
        var result = UISetupSnapshotFactory.Create(EditorFixtures.Section(
            root: "D:\\GameProject",
            scriptOutputRoot: null,
            uiAssetRoot: null,
            resourceKeyFormat: null));

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        var snapshot = result.Snapshot!;
        Assert.That(snapshot.Root, Is.EqualTo("D:/GameProject"));
        Assert.That(snapshot.ScriptOutputRoot, Is.EqualTo(UISetupSnapshot.DefaultScriptOutputRoot));
        Assert.That(snapshot.UIAssetRoot, Is.EqualTo(UISetupSnapshot.DefaultUIAssetRoot));
        Assert.That(snapshot.ResourceKeyFormat, Is.EqualTo(UISetupSnapshot.DefaultResourceKeyFormat));
    }

    [Test]
    public void RootTrailingSeparatorIsNormalized()
    {
        var result = UISetupSnapshotFactory.Create(EditorFixtures.Section(root: "D:/GameProject/"));

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        Assert.That(result.Snapshot!.Root, Is.EqualTo("D:/GameProject"));
        Assert.That(result.Snapshot.ScriptOutputDirectory, Is.EqualTo("D:/GameProject/Assets/ObFramework/Generated/UI"));
    }

    [Test]
    public void BackslashModulePathsAreNormalizedToForwardSlashes()
    {
        var result = UISetupSnapshotFactory.Create(EditorFixtures.Section(
            uiAssetRoot: "Assets\\ObFramework\\UI",
            scriptOutputRoot: "Assets\\Generated//UI\\"));

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        Assert.That(result.Snapshot!.UIAssetRoot, Is.EqualTo("Assets/ObFramework/UI"));
        Assert.That(result.Snapshot.ScriptOutputRoot, Is.EqualTo("Assets/Generated/UI"));
    }

    [TestCase("GameProject")]
    [TestCase("./GameProject")]
    [TestCase("")]
    public void RelativeOrEmptyRootIsAnError(string root)
    {
        var result = UISetupSnapshotFactory.Create(EditorFixtures.Section(root: root));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Snapshot, Is.Null);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.SetupRootRequired), Is.Not.Null);
    }

    [TestCase("D:/GameProject/Assets")]
    [TestCase("/GameProject/Assets")]
    [TestCase("../Assets")]
    [TestCase("Assets/../../Outside")]
    public void AbsoluteOrEscapingModulePathsAreErrors(string path)
    {
        var result = UISetupSnapshotFactory.Create(EditorFixtures.Section(scriptOutputRoot: path));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Snapshot, Is.Null);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.SetupPathInvalid), Is.Not.Null);
    }

    [TestCase("D:/GameProject/UI/{view}")]
    [TestCase("/UI/{view}")]
    [TestCase("Assets\\UI\\{view}")]
    public void FileSystemLikeResourceKeyFormatsAreErrors(string format)
    {
        var result = UISetupSnapshotFactory.Create(EditorFixtures.Section(resourceKeyFormat: format));

        Assert.That(result.Success, Is.False);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.SetupResourceKeyInvalid), Is.Not.Null);
    }

    [Test]
    public void FileSystemLikeResourceKeyOverrideIsAnErrorAndSuppressesSnapshot()
    {
        var result = UISetupSnapshotFactory.Create(
            EditorFixtures.Section(overrides: new[] { ("ShopView", "D:/GameProject/UI/Shop.prefab") }));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Snapshot, Is.Null);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.SetupResourceKeyInvalid), Is.Not.Null);
    }

    [Test]
    public void ResourceKeyFormatWithoutPlaceholderIsOnlyAWarning()
    {
        var result = UISetupSnapshotFactory.Create(EditorFixtures.Section(resourceKeyFormat: "UI/Constant"));

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        Assert.That(result.Snapshot, Is.Not.Null);
        var diagnostic = EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.SetupResourceKeyInvalid);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(EditorDiagnosticSeverity.Warning));
    }

    [Test]
    public void MissingUnitySectionUsesDefaultsAndWarns()
    {
        var result = UISetupSnapshotFactory.Create(null);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Snapshot, Is.Null);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.UnitySectionMissing), Is.Not.Null);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.SetupRootRequired), Is.Not.Null);
    }

    [Test]
    public void OverridesJsonReaderReadsStringMapFromRawSectionJson()
    {
        const string json = """
            {
              "scriptOutputRoot": "Assets/ObFramework/Generated/UI",
              "resourceKeyOverrides": {
                "ShopView": "UI/Shop",
                "SettingsView": "UI/Settings"
              },
              "unknownProperty": { "nested": [1, 2, 3] }
            }
            """;

        var success = UISetupOverridesJson.TryRead(json, out var overrides, out var error);

        Assert.That(success, Is.True, error);
        Assert.That(overrides, Has.Count.EqualTo(2));
        Assert.That(overrides["ShopView"], Is.EqualTo("UI/Shop"));
        Assert.That(overrides["SettingsView"], Is.EqualTo("UI/Settings"));
    }

    [Test]
    public void OverridesJsonReaderHandlesEscapes()
    {
        const string json = """
            {"resourceKeyOverrides":{"A\u0020B":"UI\\Shop","Plain":"UI/Plain"}}
            """;

        var success = UISetupOverridesJson.TryRead(json, out var overrides, out var error);

        Assert.That(success, Is.True, error);
        Assert.That(overrides["A B"], Is.EqualTo("UI\\Shop"));
        Assert.That(overrides["Plain"], Is.EqualTo("UI/Plain"));
    }

    [Test]
    public void OverridesJsonReaderReturnsEmptyMapWhenPropertyIsMissing()
    {
        var success = UISetupOverridesJson.TryRead("""{"scriptOutputRoot":"Assets/UI"}""", out var overrides, out var error);

        Assert.That(success, Is.True, error);
        Assert.That(overrides, Is.Empty);
    }

    [Test]
    public void OverridesJsonReaderRejectsNonStringValues()
    {
        var success = UISetupOverridesJson.TryRead(
            """{"resourceKeyOverrides":{"ShopView":42}}""",
            out _,
            out var error);

        Assert.That(success, Is.False);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void OverridesJsonReaderRejectsUnterminatedObjects()
    {
        var success = UISetupOverridesJson.TryRead(
            "{\"resourceKeyOverrides\":{\"ShopView\":\"UI/Shop\"",
            out _,
            out var error);

        Assert.That(success, Is.False);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void RawSectionJsonAndFixedFieldsMapToTheSameSnapshot()
    {
        const string sectionJson = """
            {
              "scriptOutputRoot": "Assets/Generated/UI",
              "uiAssetRoot": "Assets/UI",
              "resourceKeyFormat": "SCREEN/{view}",
              "resourceKeyOverrides": { "ShopView": "SCREEN/Shop" }
            }
            """;
        var jsonOk = UISetupOverridesJson.TryRead(sectionJson, out var overrides, out var error);
        Assert.That(jsonOk, Is.True, error);

        var section = new UISetupSection
        {
            Root = "D:/GameProject",
            ScriptOutputRoot = "Assets/Generated/UI",
            UIAssetRoot = "Assets/UI",
            ResourceKeyFormat = "SCREEN/{view}",
            ResourceKeyOverrides = new Dictionary<string, string>(overrides),
        };

        var result = UISetupSnapshotFactory.Create(section);

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        Assert.That(result.Snapshot!.ResolveResourceKey("ShopView"), Is.EqualTo("SCREEN/Shop"));
        Assert.That(result.Snapshot.ResolveResourceKey("SettingsView"), Is.EqualTo("SCREEN/SettingsView"));
        Assert.That(result.Snapshot.ScriptOutputDirectory, Is.EqualTo("D:/GameProject/Assets/Generated/UI"));
        Assert.That(result.Snapshot.UIAssetDirectory, Is.EqualTo("D:/GameProject/Assets/UI"));
    }
}
