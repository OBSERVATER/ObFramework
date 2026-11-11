using ObFramework.Editor.Core;

namespace ObFramework.Test.Editor;

[TestFixture]
public class EditorDiagnosticsTest
{
    [Test]
    public void UnsupportedControlKindIsAnErrorAndPreventsEmission()
    {
        var request = EditorFixtures.Request(EditorFixtures.Row(UIComponentKind.Unknown, "Mystery", "mystery", "Value"));

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Files, Is.Empty);
        var diagnostic = EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.UnsupportedControlKind);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(EditorDiagnosticSeverity.Error));
    }

    [Test]
    public void MissingComponentReferenceIsAnError()
    {
        var request = EditorFixtures.Request(new BindingTableRow
        {
            ComponentName = string.Empty,
            Kind = UIComponentKind.Text,
            FieldName = "titleText",
            ViewMemberName = "Title",
        });

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.False);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.InvalidBindingRow), Is.Not.Null);
    }

    [TestCase("1ShopView")]
    [TestCase("Shop-View")]
    [TestCase("class")]
    public void InvalidViewNameIsAnError(string viewName)
    {
        var request = EditorFixtures.Request(EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"));
        request.ViewName = viewName;

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.False);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.InvalidIdentifier), Is.Not.Null);
    }

    [Test]
    public void InvalidLevelIsAnError()
    {
        var request = EditorFixtures.Request(EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"));
        request.Level = "Popup";

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.False);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.InvalidLevel), Is.Not.Null);
    }

    [Test]
    public void InvalidNamespaceIsAnError()
    {
        var request = EditorFixtures.Request(EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"));
        request.Namespace = "Game..UI";

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.False);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.InvalidIdentifier), Is.Not.Null);
    }

    [Test]
    public void AbsoluteOrEscapingOutputPathsAreErrors()
    {
        var absolute = EditorFixtures.Request(EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"));
        absolute.ViewOutputPath = "D:/Outside/ShopView.cs";
        var absoluteResult = EditorTemplateGenerator.Generate(absolute);
        Assert.That(absoluteResult.Success, Is.False);
        Assert.That(EditorFixtures.SingleCode(absoluteResult.Diagnostics, EditorDiagnosticCodes.InvalidOutputPath), Is.Not.Null);

        var escaping = EditorFixtures.Request(EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"));
        escaping.OutputDirectory = "../Outside";
        var escapingResult = EditorTemplateGenerator.Generate(escaping);
        Assert.That(escapingResult.Success, Is.False);
        Assert.That(EditorFixtures.SingleCode(escapingResult.Diagnostics, EditorDiagnosticCodes.InvalidOutputPath), Is.Not.Null);
    }

    [Test]
    public void DuplicateSerializedFieldNamesAreAnError()
    {
        var request = EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"),
            EditorFixtures.Row(UIComponentKind.Text, "Caption", "titleText", "Caption"));

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.False);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.DuplicateBinding), Is.Not.Null);
    }

    [Test]
    public void ConflictingObservableStateTypesForOnePropertyAreAnError()
    {
        var request = EditorFixtures.Request(
            EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Value"),
            EditorFixtures.Row(UIComponentKind.Toggle, "Enabled", "enabledToggle", "Value"));

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.False);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.DuplicateBinding), Is.Not.Null);
    }

    [Test]
    public void InvalidPrefabPathsAreErrors()
    {
        var request = EditorFixtures.Request(EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"));
        request.Rows[0].PrefabPath = "../evil.prefab";

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.False);
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.InvalidPrefabPath), Is.Not.Null);
    }

    [Test]
    public void MissingPrefabReferencesAreWarningsAndGenerationContinues()
    {
        var request = EditorFixtures.Request(EditorFixtures.Row(UIComponentKind.Text, "Title", "titleText", "Title"));
        request.PrefabPath = null;
        request.Rows[0].PrefabPath = null;

        var result = EditorTemplateGenerator.Generate(request);

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        var diagnostic = EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.MissingPrefabReference);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(EditorDiagnosticSeverity.Warning));
    }

    [Test]
    public void EmptyBindingTableGeneratesEmptyTemplatesWithInfo()
    {
        var result = EditorTemplateGenerator.Generate(EditorFixtures.Request());

        Assert.That(result.Success, Is.True, EditorFixtures.FormatDiagnostics(result.Diagnostics));
        Assert.That(result.Files, Has.Count.EqualTo(3));
        Assert.That(EditorFixtures.SingleCode(result.Diagnostics, EditorDiagnosticCodes.EmptyBindingTable), Is.Not.Null);
    }

    [TestCase("HP", "hp")]
    [TestCase("TitleText", "titleText")]
    [TestCase("Btn Submit", "btnSubmit")]
    [TestCase("text.title", "textTitle")]
    public void DefaultFieldNamesAreSafeIdentifiers(string componentName, string expected)
    {
        Assert.That(EditorNaming.DefaultFieldName(componentName), Is.EqualTo(expected));
    }

    [TestCase("BtnSubmit", "SubmitCommand")]
    [TestCase("Submit", "SubmitCommand")]
    [TestCase("Btn", "BtnCommand")]
    public void DefaultCommandNamesStripButtonPrefixes(string componentName, string expected)
    {
        Assert.That(EditorNaming.DefaultCommandName(componentName), Is.EqualTo(expected));
    }

    [Test]
    public void CommandTargetsResolveToMethodAndCommandTypeNames()
    {
        Assert.That(EditorNaming.TryResolveCommandNames("Submit", out var method, out var command), Is.True);
        Assert.That(method, Is.EqualTo("Submit"));
        Assert.That(command, Is.EqualTo("SubmitCommand"));

        Assert.That(EditorNaming.TryResolveCommandNames("SubmitCommand", out method, out command), Is.True);
        Assert.That(method, Is.EqualTo("Submit"));
        Assert.That(command, Is.EqualTo("SubmitCommand"));

        Assert.That(EditorNaming.TryResolveCommandNames("SubmitAsync", out method, out command), Is.True);
        Assert.That(method, Is.EqualTo("SubmitAsync"));
        Assert.That(command, Is.EqualTo("SubmitCommand"));

        Assert.That(EditorNaming.TryResolveCommandNames("SaveAsyncCommand", out method, out command), Is.True);
        Assert.That(method, Is.EqualTo("SaveAsync"));
        Assert.That(command, Is.EqualTo("SaveCommand"));
    }
}
