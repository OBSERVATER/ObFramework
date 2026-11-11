using ObFramework.Editor.Core;

namespace ObFramework.Test.Editor;

[TestFixture]
public class EditorViewGenerationTest
{
    [Test]
    public void FirstGenerationEmitsEditableObservableViewAndRelayCommandMembers()
    {
        var request = EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Button, "BtnSubmit", "submitButton", "SubmitCommand"),
            EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"),
            EditorFixtures.Row(UIComponentKind.Toggle, "Enabled", "enabledToggle", "Enabled"),
            EditorFixtures.Row(UIComponentKind.Slider, "Volume", "volumeSlider", "Volume"));

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        Assert.That(result.Files, Has.Count.EqualTo(3));
        var view = EditorFixtures.File(result, GeneratedFileKind.View);
        Assert.That(view.Path, Is.EqualTo("Assets/Generated/UI/ShopView.cs"));
        Assert.That(view.Content, Does.Contain("namespace Game.UI"));
        Assert.That(view.Content, Does.Contain("public partial class ShopView : global::ObFramework.Integrated.UI.AUIView"));
        Assert.That(view.Content, Does.Contain("[global::ObFramework.Integrated.UI.ObservableProperty]"));
        Assert.That(view.Content, Does.Contain("private string _title = string.Empty;"));
        Assert.That(view.Content, Does.Contain("private bool _enabled;"));
        Assert.That(view.Content, Does.Contain("private float _volume;"));
        Assert.That(view.Content, Does.Contain("[global::ObFramework.Integrated.UI.RelayCommand]"));
        Assert.That(view.Content, Does.Contain("private void Submit()"));
        Assert.That(view.Content, Does.Contain("protected override void OnInit(global::ObFramework.Integrated.UI.IUIData? data)"));
        Assert.That(view.Content, Does.Contain("protected override void OnClose()"));
        Assert.That(view.Content, Does.Not.Contain("private void SubmitCommand("));

        var window = EditorFixtures.File(result, GeneratedFileKind.Window);
        var contract = EditorFixtures.File(result, GeneratedFileKind.Contract);
        Assert.That(contract.Content, Does.Contain("public partial interface IShopContract"));
        Assert.That(window.Content, Does.Contain("public partial class ShopWindow"));
    }

    [Test]
    public void RegenerationPreservesUserAuthoredMembersAndAttributesAndOnlyAddsMissingDeclarations()
    {
        const string existing = """
            namespace Game.UI
            {
                public partial class ShopView : global::ObFramework.Integrated.UI.AUIView
                {
                    public string DisplayText => _title;

                    [global::ObFramework.Integrated.UI.ObservableProperty]
                    private string _title = "initial";

                    // user authored business method
                    private void CustomHook()
                    {
                    }
                }
            }
            """;

        var request = EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"),
            EditorFixtures.Row(UIComponentKind.Text, "Caption", "captionText", "Caption"),
            EditorFixtures.Row(UIComponentKind.Button, "BtnSubmit", "submitButton", "SubmitCommand"));
        request.ExistingViewSource = existing;

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        var view = EditorFixtures.File(result, GeneratedFileKind.View);
        Assert.That(view.IsModified, Is.True);
        Assert.That(view.Content, Does.Contain("public string DisplayText => _title;"));
        Assert.That(view.Content, Does.Contain("private void CustomHook()"));
        Assert.That(view.Content, Does.Contain("private string _title = \"initial\";"));
        Assert.That(CountOccurrences(view.Content, "private string _title"), Is.EqualTo(1));
        Assert.That(view.Content, Does.Contain("private string _caption = string.Empty;"));
        Assert.That(view.Content, Does.Contain("private void Submit()"));
    }

    [Test]
    public void RegenerationWithoutChangesReportsUnmodifiedFileAndNeverReformats()
    {
        var first = EditorTemplateGenerator.Generate(EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title")));
        Assert.That(first.Success, Is.True, EditorFixtures.FormatDiagnostics(first.Diagnostics));
        var view = EditorFixtures.File(first, GeneratedFileKind.View);

        var request = EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"));
        request.ExistingViewSource = view.Content;

        var second = EditorTemplateGenerator.Generate(request);

        Assert.That(second.Success, Is.True, EditorFixtures.FormatDiagnostics(second.Diagnostics));
        var regenerated = EditorFixtures.File(second, GeneratedFileKind.View);
        Assert.That(regenerated.IsModified, Is.False);
        Assert.That(regenerated.Content, Is.EqualTo(view.Content));
    }

    [Test]
    public void ExistingNonPartialViewSourceIsRejectedInsteadOfOverwritten()
    {
        var request = EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"));
        request.ExistingViewSource = "public class ShopView : object { }";

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Files, Is.Empty);
        var diagnostic = EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.ExistingSourceNotEditable);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Message, Does.Contain("partial"));
    }

    [TestCase("ShopView", "ShopWindow", "IShopContract")]
    [TestCase("SettingsView", "SettingsWindow", "ISettingsContract")]
    [TestCase("Hud", "HudWindow", "IHudContract")]
    public void DefaultNamesAreDerivedFromTheViewName(string viewName, string windowName, string contractName)
    {
        Assert.That(EditorNaming.DefaultWindowName(viewName), Is.EqualTo(windowName));
        Assert.That(EditorNaming.DefaultContractName(viewName), Is.EqualTo(contractName));
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
