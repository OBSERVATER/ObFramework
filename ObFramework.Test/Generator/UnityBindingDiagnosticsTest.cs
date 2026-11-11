using Microsoft.CodeAnalysis;

namespace ObFramework.Test.Generator;

/// <summary>
/// Compile-time diagnostics of the generated Unity engine bindings. Every invalid declaration must
/// fail at generation time with a stable OBFU1xx id instead of breaking or silently ignoring the
/// binding at runtime.
/// </summary>
[TestFixture]
public class UnityBindingDiagnosticsTest
{
    private const string BindingSurfaceSource = """
        using ObFramework.Adapter.Unity.ViewBinding;
        using ObFramework.Integrated.UI;
        using UnityEngine;

        namespace UnityBindingDiagnostics
        {
            public interface ITestView { }

            public partial class TestView : AUIView
            {
                [ObservableProperty] private string _title = "";
                [ObservableProperty] private int _count;
                [ObservableProperty] private bool _enabled;

                [RelayCommand] private void Submit() { }
            }

            [BindView(typeof(TestView), typeof(ITestView))]
            public partial class TestController : MonoBehaviour, ITestView
            {
        __FIELDS__
            }
        }
        """;

    private const string OrphanControllerSource = """
        using ObFramework.Adapter.Unity.ViewBinding;
        using UnityEngine;

        namespace UnityBindingDiagnostics
        {
            public partial class OrphanController : MonoBehaviour
            {
                [UIBindData("Title")]
                public UnityEngine.UI.Text Text;
            }
        }
        """;

    private const string PlainViewControllerSource = """
        using ObFramework.Adapter.Unity.ViewBinding;
        using UnityEngine;

        namespace UnityBindingDiagnostics
        {
            public interface IPlainView { }

            public class PlainView { }

            [BindView(typeof(PlainView), typeof(IPlainView))]
            public partial class PlainController : MonoBehaviour, IPlainView
            {
                [UIBindData("Anything")]
                public UnityEngine.UI.Text Text;
            }
        }
        """;

    [TestCase("[UIBindData(nameof(TestView.Title))] public System.Action Broken;", "OBFU101")]
    [TestCase("[UIBindData(\"Missing\")] public UnityEngine.UI.Text Text;", "OBFU102")]
    [TestCase("[UIBindCommand(\"MissingCommand\")] public UnityEngine.UI.Button Button;", "OBFU102")]
    [TestCase("[UIBindData(nameof(TestView.Title), Mode = UIBindMode.TwoWay)] public UnityEngine.UI.Text Text;", "OBFU103")]
    [TestCase("[UIBindData(nameof(TestView.Enabled), Mode = UIBindMode.TwoWay)] public UnityEngine.UI.InputField Input;", "OBFU104")]
    [TestCase("[UIBindData(nameof(TestView.Title))]\n[UIBindCommand(nameof(TestView.SubmitCommand))]\npublic UnityEngine.UI.Button Both;", "OBFU105")]
    [TestCase("[UIBindData(\"\")] public UnityEngine.UI.Text Text;", "OBFU107")]
    [TestCase("[UIBindCommand(\"Submit\")] public UnityEngine.UI.Button Button;", "OBFU107")]
    public void InvalidBindingsReportOneErrorDiagnostic(string fields, string expectedId)
    {
        var diagnostics = RunWithFields(fields);
        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id),
            Is.EqualTo(new[] { expectedId }),
            "diagnostics: " + string.Join(", ", diagnostics));
        Assert.That(diagnostics.Single().Severity, Is.EqualTo(DiagnosticSeverity.Error));
    }

    [Test]
    public void BindingFieldsWithoutBindViewReportObfu106()
    {
        var compilation = UnityBindingTestHarness.Generate(OrphanControllerSource, emit: false);
        Assert.That(
            compilation.GeneratorDiagnostics.Select(diagnostic => diagnostic.Id),
            Is.EqualTo(new[] { "OBFU106" }));
    }

    [Test]
    public void BusinessViewThatDoesNotDeriveFromAuiViewReportsObfu108()
    {
        var compilation = UnityBindingTestHarness.Generate(PlainViewControllerSource, emit: false);
        Assert.That(
            compilation.GeneratorDiagnostics.Select(diagnostic => diagnostic.Id),
            Is.EqualTo(new[] { "OBFU108" }));
    }

    [Test]
    public void ValidBindingsReportNoDiagnostics()
    {
        var fields = """
                [UIBindData(nameof(TestView.Title))]
                public UnityEngine.UI.Text TitleText;
                [UIBindData(nameof(TestView.Count), Mode = UIBindMode.TwoWay)]
                public UnityEngine.UI.InputField CountInput;
                [UIBindData(nameof(TestView.Enabled), Mode = UIBindMode.TwoWay)]
                public UnityEngine.UI.Toggle EnabledToggle;
                [UIBindCommand(nameof(TestView.SubmitCommand))]
                public UnityEngine.UI.Button SubmitButton;
            """;
        var diagnostics = RunWithFields(fields);
        Assert.That(
            UnityBindingTestHarness.FormatDiagnostics(diagnostics),
            Is.Empty,
            "a valid binding declaration must be accepted");
    }

    private static IReadOnlyList<Diagnostic> RunWithFields(string fields)
    {
        var source = BindingSurfaceSource.Replace("__FIELDS__", fields);
        return UnityBindingTestHarness.Generate(source, emit: false).GeneratorDiagnostics;
    }
}
