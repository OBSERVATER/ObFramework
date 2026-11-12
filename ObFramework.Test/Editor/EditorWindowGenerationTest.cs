using ObFramework.Editor.Core;

namespace ObFramework.Test.Editor;

[TestFixture]
public class EditorWindowGenerationTest
{
    private const string ExistingWindow = """
        namespace Game.UI
        {
            [global::ObFramework.Adapter.Unity.ViewBinding.BindView(typeof(ShopView), typeof(IShopContract))]
            public partial class ShopWindow : global::ObFramework.Adapter.Unity.UI.AUIWindow, IShopContract
            {
                public override global::ObFramework.Core.IArchitecture GetArchitecture()
                    => global::Game.GameArchitecture.Interface;

                public override global::ObFramework.Integrated.UI.EUILevel Level
                    => global::ObFramework.Integrated.UI.EUILevel.Common;

                /// <summary>User authored engine code must survive regeneration.</summary>
                public void PlaySound()
                {
                }

                [global::UnityEngine.SerializeField]
                [global::ObFramework.Adapter.Unity.ViewBinding.UIBindData(nameof(ShopView.Title))]
                private global::UnityEngine.UI.Text titleText;
            }
        }
        """;

    [Test]
    public void FirstGenerationEmitsBindViewContractArchitectureLevelAndSerializedBindings()
    {
        var request = EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"),
            EditorFixtures.Row(UIComponentKind.Toggle, "Enabled", "enabledToggle", "Enabled"),
            EditorFixtures.Row(UIComponentKind.Slider, "Volume", "volumeSlider", "Volume", format: "P0"),
            EditorFixtures.Row(UIComponentKind.Button, "BtnSubmit", "submitButton", "SubmitCommand"));
        request.Level = "Overlay";

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        var window = EditorFixtures.File(result, GeneratedFileKind.Window);
        Assert.That(window.Path, Is.EqualTo("Assets/Generated/UI/ShopWindow.cs"));
        Assert.That(window.Content, Does.Contain("[global::ObFramework.Adapter.Unity.ViewBinding.BindView(typeof(ShopView), typeof(IShopContract))]"));
        Assert.That(window.Content, Does.Contain("public partial class ShopWindow : global::ObFramework.Adapter.Unity.UI.AUIWindow, IShopContract"));
        Assert.That(window.Content, Does.Contain("=> global::Game.GameArchitecture.Interface;"));
        Assert.That(window.Content, Does.Contain("=> global::ObFramework.Integrated.UI.EUILevel.Overlay;"));
        Assert.That(window.Content, Does.Contain("[global::UnityEngine.SerializeField]"));
        Assert.That(window.Content, Does.Contain("[global::ObFramework.Adapter.Unity.ViewBinding.UIBindData(nameof(ShopView.Title))]"));
        Assert.That(window.Content, Does.Contain("private global::UnityEngine.UI.Text titleText;"));
        Assert.That(window.Content, Does.Contain(
            "[global::ObFramework.Adapter.Unity.ViewBinding.UIBindData(nameof(ShopView.Enabled), Mode = global::ObFramework.Adapter.Unity.ViewBinding.UIBindMode.TwoWay)]"));
        Assert.That(window.Content, Does.Contain(
            "[global::ObFramework.Adapter.Unity.ViewBinding.UIBindData(nameof(ShopView.Volume), Mode = global::ObFramework.Adapter.Unity.ViewBinding.UIBindMode.TwoWay, Format = \"P0\")]"));
        Assert.That(window.Content, Does.Contain("[global::ObFramework.Adapter.Unity.ViewBinding.UIBindCommand(nameof(ShopView.SubmitCommand))]"));
        Assert.That(window.Content, Does.Contain("private global::UnityEngine.UI.Button submitButton;"));
    }

    [Test]
    public void RegenerationAddsMissingSerializedFieldsAndPreservesUserCode()
    {
        var request = EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"),
            EditorFixtures.Row(UIComponentKind.Toggle, "Enabled", "enabledToggle", "Enabled"));
        request.ExistingWindowSource = ExistingWindow;

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        var window = EditorFixtures.File(result, GeneratedFileKind.Window);
        Assert.That(window.IsModified, Is.True);
        Assert.That(window.Content, Does.Contain("public void PlaySound()"));
        Assert.That(window.Content, Does.Contain("private global::UnityEngine.UI.Text titleText;"));
        Assert.That(window.Content, Does.Contain("private global::UnityEngine.UI.Toggle enabledToggle;"));
        Assert.That(CountOccurrences(window.Content, "titleText"), Is.EqualTo(1));
    }

    [Test]
    public void RegenerationUpdatesModeFormatArchitectureAndLevelAndPreservesHandWrittenAttributes()
    {
        const string existing = """
            namespace Game.UI
            {
                [global::ObFramework.Adapter.Unity.ViewBinding.BindView(typeof(ShopView), typeof(IShopContract))]
                public partial class ShopWindow : global::ObFramework.Adapter.Unity.UI.AUIWindow, IShopContract
                {
                    public override global::ObFramework.Core.IArchitecture GetArchitecture()
                        => global::Game.GameArchitecture.Interface;

                    public override global::ObFramework.Integrated.UI.EUILevel Level
                        => global::ObFramework.Integrated.UI.EUILevel.Common;

                    /// <summary>User authored engine code must survive regeneration.</summary>
                    public void PlaySound()
                    {
                    }

                    [global::System.Obsolete("legacy control kept by the user")]
                    [global::UnityEngine.SerializeField]
                    [global::ObFramework.Adapter.Unity.ViewBinding.UIBindData(nameof(ShopView.Enabled))]
                    private global::UnityEngine.UI.Toggle enabledToggle;
                }
            }
            """;

        var request = EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Toggle, "Enabled", "enabledToggle", "Enabled",
                format: "P0", mode: UIBindMode.TwoWay));
        request.ExistingWindowSource = existing;
        request.ArchitectureTypeFullName = "Game.NewArchitecture";
        request.Level = "Overlay";

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        var window = EditorFixtures.File(result, GeneratedFileKind.Window);
        Assert.That(window.IsModified, Is.True);
        Assert.That(window.Content, Does.Contain("public void PlaySound()"));
        Assert.That(window.Content, Does.Contain("[global::System.Obsolete(\"legacy control kept by the user\")]"),
            "hand-written attributes next to a generated field must be preserved");
        Assert.That(window.Content, Does.Contain("Mode = global::ObFramework.Adapter.Unity.ViewBinding.UIBindMode.TwoWay"));
        Assert.That(window.Content, Does.Contain("Format = \"P0\""));
        Assert.That(window.Content, Does.Not.Contain("GameArchitecture.Interface"),
            "the selected Architecture must be applied on regeneration");
        Assert.That(window.Content, Does.Contain("Game.NewArchitecture.Interface"));
        Assert.That(window.Content, Does.Not.Contain("EUILevel.Common"),
            "the selected Level must be applied on regeneration");
        Assert.That(window.Content, Does.Contain("EUILevel.Overlay"));
        Assert.That(CountOccurrences(window.Content, "enabledToggle"), Is.EqualTo(1),
            "the field must be updated in place, not duplicated");
    }

    [Test]
    public void DestructiveRenameIsPreviewedAndOnlyAppliedWhenConfirmed()
    {
        var request = EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Caption"));
        request.ExistingWindowSource = ExistingWindow;

        var preview = EditorTemplateGenerator.Generate(request);

        Assert.That(preview.Success, Is.True, EditorFixtures.FormatDiagnostics(preview.Diagnostics));
        Assert.That(preview.HasPendingDestructiveChanges, Is.True);
        var change = preview.DestructiveChanges.Single();
        Assert.That(change.Kind, Is.EqualTo(DestructiveChangeKind.Rename));
        Assert.That(change.Applied, Is.False);
        Assert.That(change.Id, Is.EqualTo("window-field:titleText"));
        Assert.That(change.Before, Is.EqualTo("Title"));
        Assert.That(change.After, Is.EqualTo("Caption"));
        var window = EditorFixtures.File(preview, GeneratedFileKind.Window);
        Assert.That(window.Content, Does.Contain("nameof(ShopView.Title)"));
        Assert.That(window.Content, Does.Not.Contain("nameof(ShopView.Caption)"));
        Assert.That(EditorFixtures.SingleCode(preview.Diagnostics, EditorDiagnosticCodes.DestructiveChangePending), Is.Not.Null);

        var confirmRequest = EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Caption"));
        confirmRequest.ExistingWindowSource = ExistingWindow;
        confirmRequest.ConfirmedDestructiveChangeIds.Add(change.Id);

        var applied = EditorTemplateGenerator.Generate(confirmRequest);

        Assert.That(applied.Success, Is.True, EditorFixtures.FormatDiagnostics(applied.Diagnostics));
        var appliedChange = applied.DestructiveChanges.Single();
        Assert.That(appliedChange.Applied, Is.True);
        var updated = EditorFixtures.File(applied, GeneratedFileKind.Window);
        Assert.That(updated.Content, Does.Contain("nameof(ShopView.Caption)"));
        Assert.That(updated.Content, Does.Not.Contain("nameof(ShopView.Title)"));
        Assert.That(updated.Content, Does.Contain("public void PlaySound()"));
        Assert.That(EditorFixtures.SingleCode(applied.Diagnostics, EditorDiagnosticCodes.DestructiveChangeApplied), Is.Not.Null);
    }

    [Test]
    public void DestructiveDeleteIsPreviewedAndOnlyAppliedWhenConfirmed()
    {
        var request = EditorFixtures.Request();
        request.ExistingWindowSource = ExistingWindow;

        var preview = EditorTemplateGenerator.Generate(request);

        Assert.That(preview.Success, Is.True, EditorFixtures.FormatDiagnostics(preview.Diagnostics));
        var change = preview.DestructiveChanges.Single();
        Assert.That(change.Kind, Is.EqualTo(DestructiveChangeKind.Delete));
        var window = EditorFixtures.File(preview, GeneratedFileKind.Window);
        Assert.That(window.Content, Does.Contain("private global::UnityEngine.UI.Text titleText;"));

        request.ConfirmedDestructiveChangeIds.Add(change.Id);
        var applied = EditorTemplateGenerator.Generate(request);

        Assert.That(applied.Success, Is.True, EditorFixtures.FormatDiagnostics(applied.Diagnostics));
        var updated = EditorFixtures.File(applied, GeneratedFileKind.Window);
        Assert.That(updated.Content, Does.Not.Contain("titleText"));
        Assert.That(updated.Content, Does.Contain("public void PlaySound()"));
    }

    [Test]
    public void ExistingNonPartialWindowSourceIsRejectedInsteadOfOverwritten()
    {
        var request = EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"));
        request.ExistingWindowSource = "public class ShopWindow : object { }";

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Files, Is.Empty);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.ExistingSourceNotEditable), Is.Not.Null);
    }

    [Test]
    public void MissingArchitectureSelectionEmitsWarningAndTodoImplementation()
    {
        var request = EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"));
        request.ArchitectureTypeFullName = string.Empty;

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.ArchitectureNotSelected), Is.Not.Null);
        var window = EditorFixtures.File(result, GeneratedFileKind.Window);
        Assert.That(window.Content, Does.Contain("NotImplementedException"));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
