using Microsoft.CodeAnalysis;

namespace ObFramework.Test.Generator;

/// <summary>
/// Behavior and diagnostic tests for the engine-neutral CDE source generator (T07/T09/T10/T11).
/// Each behavior test compiles representative C#9 business Views with the real CDE generator,
/// emits a real assembly against the Framework runtime and executes the generated surface.
/// </summary>
[TestFixture]
public class CDEGeneratorTests
{
    #region Observable properties

    private const string ObservablePropertySource = """
        using System;
        using System.Collections.Generic;
        using ObFramework.Integrated.UI;

        namespace CdeSample
        {
            public sealed partial class ProfileView : AUIView
            {
                [ObservableProperty] private int _count = 5;
                [ObservableProperty] private string? _title = "initial";

                [ObservableProperty]
                [NotifyPropertyChangedFor(nameof(DisplayText))]
                [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
                private bool _isDirty;

                public string DisplayText => Title + ":" + Count;

                [RelayCommand(CanExecute = nameof(CanSave))]
                private void Save() => SaveCount++;

                private bool CanSave() => IsDirty;

                public int SaveCount;
            }

            public static class Exercise
            {
                public static void Run()
                {
                    var view = new ProfileView();
                    var changes = new List<string>();
                    view.PropertyChanged += name => changes.Add(name);
                    var commandChanges = new List<string>();
                    view.CommandStateChanged += name => commandChanges.Add(name);

                    var lastCount = -1;
                    view.CountChanged += value => lastCount = value;
                    string? lastTitle = "sentinel";
                    view.TitleChanged += value => lastTitle = value;

                    Require(view.Count == 5, "the field initializer value must be preserved by the generated property");
                    Require(view.Title == "initial", "reference type field initializer must be preserved");
                    Require(changes.Count == 0, "no PropertyChanged notification may fire before assignment");
                    Require(lastCount == -1 && lastTitle == "sentinel", "typed change events must not fire before assignment");

                    view.Count = 7;
                    Require(view.Count == 7, "the generated property must expose the same backing storage");
                    Require(lastCount == 7, "the typed CountChanged event must carry the new value");
                    Require(Has(changes, "Count"), "PropertyChanged must fire with the generated property name");

                    view.Count = 7;
                    Require(CountOf(changes, "Count") == 1, "assigning an equal value must not raise notifications");
                    Require(lastCount == 7, "assigning an equal value must not raise the typed event");

                    view.Title = "next";
                    Require(view.Title == "next" && lastTitle == "next", "reference type properties must expose a typed event");
                    view.Title = null;
                    Require(view.Title == null && lastTitle == null, "nullable reference properties must accept null");

                    view.IsDirty = true;
                    Require(Has(changes, "IsDirty"), "PropertyChanged must fire for the changed property");
                    Require(Has(changes, "DisplayText"), "NotifyPropertyChangedFor must notify the display property");
                    Require(Has(commandChanges, "SaveCommand"), "NotifyCanExecuteChangedFor must raise CommandStateChanged for the command");
                    Require(!Has(changes, "SaveCommand"), "command dependency notifications must use CommandStateChanged, not PropertyChanged");

                    var command = new ProfileView.SaveCommand(view);
                    Require(command.CanExecute(), "CanExecute must be wired to the View CanExecute method");
                    Require(ReferenceEquals(command.View, view), "the command must capture the owning View instance");
                    Require(command.CommandName == "SaveCommand", "the command name must be the generated command type name");
                    command.Execute();
                    Require(view.SaveCount == 1, "Execute must invoke the attributed View method");
                    Require(!view.IsCommandRunning("SaveCommand"), "a finished synchronous command must not stay Running");

                    view.IsDirty = false;
                    Require(!command.CanExecute(), "CanExecute must re-evaluate the current View state");
                    command.Execute();
                    Require(view.SaveCount == 1, "Execute must not run when CanExecute is false at execution time");

                    view.IsDirty = true;
                    var other = new ProfileView();
                    new ProfileView.SaveCommand(other).Execute();
                    Require(other.SaveCount == 0, "a command constructed for another View instance must not execute");
                }

                private static void Require(bool condition, string message)
                {
                    if (!condition) throw new InvalidOperationException(message);
                }

                private static bool Has(List<string> values, string name) => values.Contains(name);

                private static int CountOf(List<string> values, string name)
                {
                    var count = 0;
                    foreach (var value in values)
                        if (value == name) count++;
                    return count;
                }
            }
        }
        """;

    [Test]
    public void ObservablePropertiesExposeSingleStorageTypedEventsAndDependencies()
    {
        var compilation = CompileAndAssertClean(ObservablePropertySource);
        CdeTestHarness.RunExercise(compilation);
    }

    #endregion

    #region Synchronous relay commands

    private const string SyncCommandSource = """
        using System;
        using ObFramework.Integrated.UI;

        namespace CdeSample
        {
            public sealed partial class FormView : AUIView
            {
                public int SubmitCount;
                public int PickedValue;
                public int LastValue;
                public string? LastText;
                public bool CanClear = true;

                [RelayCommand] private void Submit() => SubmitCount++;

                [RelayCommand] private void Pick(int value) => PickedValue = value;

                [RelayCommand(CanExecute = "CanChoose")] private void Choose(int value) => LastValue = value;
                private bool CanChoose(int value) => value > 0;

                [RelayCommand(CanExecute = "CanReset")] private void Reset() => LastValue = -1;
                private bool CanReset() => CanClear;

                [RelayCommand] private void Clear(string? text) => LastText = text;
            }

            public static class Exercise
            {
                public static void Run()
                {
                    var view = new FormView();
                    var other = new FormView();
                    new FormView.SubmitCommand(view).Execute();
                    new FormView.SubmitCommand(other).Execute();
                    new FormView.SubmitCommand(view).Execute();
                    Require(view.SubmitCount == 2 && other.SubmitCount == 1, "commands must capture their own View instance");

                    var submit = new FormView.SubmitCommand(view);
                    Require(ReferenceEquals(submit.View, view), "the command must expose the captured View instance");
                    Require(submit.CommandName == "SubmitCommand", "the command name must be the generated command type name");
                    Require(submit.Parameter == null, "a command without an event value must not carry a parameter");

                    var pick = new FormView.PickCommand(view, 42);
                    Require(Equals(pick.Parameter, 42), "a value command must expose the captured event value");
                    pick.Execute();
                    Require(view.PickedValue == 42, "a value command must pass the captured value to the View method");

                    var choose = new FormView.ChooseCommand(view, 5);
                    Require(choose.CanExecute(), "parameterized CanExecute must receive the captured value");
                    Require(choose.CanExecute(5), "the CanExecute(object) overload must receive the supplied value");
                    Require(!choose.CanExecute(-1), "parameterized CanExecute must reject invalid values");
                    choose.Execute();
                    Require(view.LastValue == 5, "a command whose CanExecute is true must execute");

                    new FormView.ChooseCommand(view, -1).Execute();
                    Require(view.LastValue == 5, "the execution-time CanExecute check must reject invalid values");

                    var reset = new FormView.ResetCommand(view);
                    Require(reset.CanExecute(), "zero-parameter CanExecute must be wired to the View method");
                    view.CanClear = false;
                    Require(!reset.CanExecute(), "zero-parameter CanExecute must re-evaluate View state");

                    var clear = new FormView.ClearCommand(view, "hello");
                    clear.Execute();
                    Require(view.LastText == "hello", "nullable reference event values must be passed through");
                }

                private static void Require(bool condition, string message)
                {
                    if (!condition) throw new InvalidOperationException(message);
                }
            }
        }
        """;

    [Test]
    public void SyncRelayCommandsCaptureViewPassValuesAndCheckCanExecute()
    {
        var compilation = CompileAndAssertClean(SyncCommandSource);
        CdeTestHarness.RunExercise(compilation);
    }

    #endregion

    #region Asynchronous relay commands

    private const string AsyncDuplicateSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using ObFramework.Integrated.UI;

        namespace CdeSample
        {
            public sealed partial class LoadView : AUIView
            {
                public TaskCompletionSource<bool> Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public int Entered;
                public int Completed;
                public CancellationToken LastToken;
                public bool Fail;

                [RelayCommand]
                private async Task LoadAsync(CancellationToken ct)
                {
                    Entered++;
                    LastToken = ct;
                    Started.TrySetResult(true);
                    await Gate.Task.WaitAsync(ct).ConfigureAwait(false);
                    if (Fail) throw new InvalidOperationException("business failure");
                    Completed++;
                }
            }

            public static class Exercise
            {
                public static async Task Run()
                {
                    var view = new LoadView();
                    var stateChanges = new List<string>();
                    view.CommandStateChanged += name => stateChanges.Add(name);

                    var command = new LoadView.LoadCommand(view);
                    Require(!command.AllowConcurrentExecutions, "async commands must reject duplicates by default");
                    Require(command.CancelOnClose, "async commands must request cancel-on-close by default");
                    Require(command.CommandName == "LoadCommand", "the Async suffix must be dropped from the command type name");

                    var first = command.Execute(CancellationToken.None).AsTask();
                    await view.Started.Task.ConfigureAwait(false);
                    Require(view.IsCommandRunning("LoadCommand") && command.IsRunning, "Running must be observable before completion");
                    Require(view.LastToken.CanBeCanceled, "the business method must receive a cancellable command token");
                    Require(stateChanges.Count == 1 && stateChanges[0] == "LoadCommand", "starting must raise CommandStateChanged");

                    var duplicate = new LoadView.LoadCommand(view).Execute(CancellationToken.None).AsTask();
                    Require(duplicate.IsCompleted, "a duplicate execution must return immediately");
                    Require(view.Entered == 1, "a duplicate execution must not enter the business method");

                    view.Gate.TrySetResult(true);
                    await first.ConfigureAwait(false);
                    Require(view.Completed == 1 && !view.IsCommandRunning("LoadCommand"), "completion must clear Running");
                    Require(stateChanges.Count == 2 && stateChanges[1] == "LoadCommand", "completion must raise CommandStateChanged");

                    var failing = new LoadView { Fail = true };
                    var failTask = new LoadView.LoadCommand(failing).Execute(CancellationToken.None).AsTask();
                    await failing.Started.Task.ConfigureAwait(false);
                    failing.Gate.TrySetResult(true);
                    var threw = false;
                    try
                    {
                        await failTask.ConfigureAwait(false);
                    }
                    catch (InvalidOperationException)
                    {
                        threw = true;
                    }
                    Require(threw, "business exceptions must propagate out of the async command");
                    Require(!failing.IsCommandRunning("LoadCommand"), "Running must be restored after an exception");

                    failing.Fail = false;
                    failing.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    failing.Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var retry = new LoadView.LoadCommand(failing).Execute(CancellationToken.None).AsTask();
                    await failing.Started.Task.ConfigureAwait(false);
                    failing.Gate.TrySetResult(true);
                    await retry.ConfigureAwait(false);
                    Require(failing.Completed == 1, "a command must be runnable again after a failure");

                    var secondView = new LoadView();
                    var secondTask = new LoadView.LoadCommand(secondView).Execute(CancellationToken.None).AsTask();
                    await secondView.Started.Task.ConfigureAwait(false);
                    Require(secondView.Entered == 1, "an identical command on another View instance must run independently");
                    Require(secondView.IsCommandRunning("LoadCommand"), "Running state must be owned per View instance");
                    Require(!view.IsCommandRunning("LoadCommand"), "another View instance must not observe running state");
                    secondView.Gate.TrySetResult(true);
                    await secondTask.ConfigureAwait(false);
                }

                private static void Require(bool condition, string message)
                {
                    if (!condition) throw new InvalidOperationException(message);
                }
            }
        }
        """;

    [Test]
    public void AsyncRelayCommandsPreventDuplicatesAndRestoreRunningState()
    {
        var compilation = CompileAndAssertClean(AsyncDuplicateSource);
        CdeTestHarness.RunExercise(compilation);
    }

    private const string AsyncOptionsSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using ObFramework.Integrated.UI;

        namespace CdeSample
        {
            public sealed partial class WorkView : AUIView
            {
                public TaskCompletionSource<bool> Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public int Entered;
                public int Completed;
                public int LastValue;

                [RelayCommand(AllowConcurrentExecutions = true)]
                private async Task ParallelAsync(CancellationToken ct)
                {
                    Entered++;
                    Started.TrySetResult(true);
                    await Gate.Task.WaitAsync(ct).ConfigureAwait(false);
                    Completed++;
                }

                [RelayCommand(CancelOnClose = false)]
                private async ValueTask DurableAsync()
                {
                    Entered++;
                    Started.TrySetResult(true);
                    await Gate.Task.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    Completed++;
                }

                [RelayCommand(CanExecute = nameof(CanAccept))]
                private async ValueTask AcceptAsync(int value, CancellationToken ct)
                {
                    LastValue = value;
                    await Task.Yield();
                    Completed++;
                    _ = ct;
                }

                private bool CanAccept(int value) => value > 0;
            }

            public static class Exercise
            {
                public static async Task Run()
                {
                    var view = new WorkView();
                    var parallel = new WorkView.ParallelCommand(view);
                    Require(parallel.AllowConcurrentExecutions, "AllowConcurrentExecutions=true must be forwarded");
                    Require(parallel.CancelOnClose, "CancelOnClose must default to true");

                    var first = parallel.Execute(CancellationToken.None).AsTask();
                    var second = new WorkView.ParallelCommand(view).Execute(CancellationToken.None).AsTask();
                    Require(view.Entered == 2, "opt-in concurrency must allow overlapping executions");
                    Require(!first.IsCompleted && !second.IsCompleted, "both concurrent executions must still be waiting");
                    view.Gate.TrySetResult(true);
                    await first.ConfigureAwait(false);
                    await second.ConfigureAwait(false);
                    Require(view.Completed == 2, "both concurrent executions must complete");

                    var cancellable = new WorkView();
                    using (var source = new CancellationTokenSource())
                    {
                        var running = new WorkView.ParallelCommand(cancellable).Execute(source.Token).AsTask();
                        await cancellable.Started.Task.ConfigureAwait(false);
                        source.Cancel();
                        var cancelled = false;
                        try
                        {
                            await running.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                        }
                        Require(cancelled, "an external cancellation token must cancel the running command");
                        Require(!cancellable.IsCommandRunning("ParallelCommand"), "cancellation must clear Running state");
                    }

                    var durable = new WorkView();
                    var durableCommand = new WorkView.DurableCommand(durable);
                    Require(!durableCommand.CancelOnClose, "CancelOnClose=false must be forwarded to the generated command");
                    var durableTask = durableCommand.Execute(CancellationToken.None).AsTask();
                    await durable.Started.Task.ConfigureAwait(false);
                    durable.Gate.TrySetResult(true);
                    await durableTask.ConfigureAwait(false);
                    Require(durable.Completed == 1, "ValueTask relay commands must await the business method");

                    var acceptView = new WorkView();
                    var accept = new WorkView.AcceptCommand(acceptView, 5);
                    Require(accept.CanExecute(), "parameterized CanExecute must use the captured value");
                    Require(accept.CanExecute(5) && !accept.CanExecute(-1), "the CanExecute(object) overload must use the supplied value");
                    await accept.Execute(CancellationToken.None).AsTask().ConfigureAwait(false);
                    Require(acceptView.LastValue == 5 && acceptView.Completed == 1, "async value commands must await the business method");

                    await new WorkView.AcceptCommand(acceptView, -1).Execute(CancellationToken.None).AsTask().ConfigureAwait(false);
                    Require(acceptView.Completed == 1, "execution-time CanExecute must reject invalid command values");
                }

                private static void Require(bool condition, string message)
                {
                    if (!condition) throw new InvalidOperationException(message);
                }
            }
        }
        """;

    [Test]
    public void AsyncRelayCommandsSupportConcurrencyCancellationAndOptions()
    {
        var compilation = CompileAndAssertClean(AsyncOptionsSource);
        CdeTestHarness.RunExercise(compilation);
    }

    #endregion

    #region Diagnostics

    private static string ViewSource(string declaration, string members)
    {
        return "using System;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing ObFramework.Integrated.UI;\n\n"
            + "namespace DiagSample\n{\n"
            + declaration + "\n{\n"
            + members + "\n}\n}\n";
    }

    private static void AssertGeneratorDiagnostics(string source, params string[] expectedIds)
    {
        var compilation = CdeTestHarness.Compile(source, emit: false);
        Assert.That(
            compilation.GeneratorDiagnostics.Select(diagnostic => diagnostic.Id).ToArray(),
            Is.EqualTo(expectedIds),
            CdeTestHarness.FormatDiagnostics(compilation.GeneratorDiagnostics));
    }

    [Test]
    public void ViewThatIsNotPartialReportsObf100()
    {
        AssertGeneratorDiagnostics(
            ViewSource("public sealed class NotPartialView : AUIView", "[ObservableProperty] private int _count;"),
            "OBF100");
    }

    [Test]
    public void ViewThatDoesNotDeriveFromAUIViewReportsObf101()
    {
        AssertGeneratorDiagnostics(
            ViewSource("public partial class PlainView", "[ObservableProperty] private int _count;"),
            "OBF101");
    }

    [Test]
    public void NestedViewReportsObf102()
    {
        var source = """
            using ObFramework.Integrated.UI;

            namespace DiagSample
            {
                public partial class Outer
                {
                    public sealed partial class InnerView : AUIView
                    {
                        [ObservableProperty] private int _count;
                    }
                }
            }
            """;
        AssertGeneratorDiagnostics(source, "OBF102");
    }

    [Test]
    public void GenericViewReportsObf103()
    {
        AssertGeneratorDiagnostics(
            ViewSource("public sealed partial class GenericView<T> : AUIView", "[ObservableProperty] private int _count;"),
            "OBF103");
    }

    [Test]
    public void AttributeOnStructReportsObf104()
    {
        AssertGeneratorDiagnostics(
            ViewSource("public partial struct StructView", "[ObservableProperty] private int _count;"),
            "OBF104");
    }

    [Test]
    public void StaticObservableFieldReportsObf110()
    {
        AssertGeneratorDiagnostics(
            ViewSource("public sealed partial class StaticFieldView : AUIView", "[ObservableProperty] private static int _count;"),
            "OBF110");
    }

    [Test]
    public void ConstObservableFieldReportsObf111()
    {
        AssertGeneratorDiagnostics(
            ViewSource("public sealed partial class ConstFieldView : AUIView", "[ObservableProperty] private const int _max = 1;"),
            "OBF111");
    }

    [Test]
    public void ReadOnlyObservableFieldReportsObf112()
    {
        AssertGeneratorDiagnostics(
            ViewSource("public sealed partial class ReadOnlyFieldView : AUIView", "[ObservableProperty] private readonly int _count;"),
            "OBF112");
    }

    [Test]
    public void UnsupportFieldTypeReportsObf113()
    {
        var source = """
            using ObFramework.Integrated.UI;

            namespace DiagSample
            {
                public sealed unsafe partial class PointerView : AUIView
                {
                    [ObservableProperty] private int* _count;
                }
            }
            """;
        AssertGeneratorDiagnostics(source, "OBF113");
    }

    [Test]
    public void ObservableMemberCollisionReportsObf114()
    {
        var source = ViewSource(
            "public sealed partial class CollisionView : AUIView",
            "[ObservableProperty] private int _count;\npublic int Count => 0;");
        AssertGeneratorDiagnostics(source, "OBF114");
    }

    [Test]
    public void DuplicateGeneratedPropertyReportsObf115()
    {
        var source = ViewSource(
            "public sealed partial class DuplicateView : AUIView",
            "[ObservableProperty] private int _count;\n[ObservableProperty] private int count;");
        AssertGeneratorDiagnostics(source, "OBF115");
    }

    [Test]
    public void EmptyNotificationTargetReportsObf120()
    {
        var source = ViewSource(
            "public sealed partial class EmptyTargetView : AUIView",
            "[ObservableProperty]\n[NotifyCanExecuteChangedFor(\"\")]\nprivate int _count;");
        AssertGeneratorDiagnostics(source, "OBF120");
    }

    [Test]
    public void StaticRelayCommandReportsObf131()
    {
        AssertGeneratorDiagnostics(
            ViewSource("public sealed partial class StaticMethodView : AUIView", "[RelayCommand] private static void Go() { }"),
            "OBF131");
    }

    [Test]
    public void GenericRelayCommandReportsObf132()
    {
        AssertGeneratorDiagnostics(
            ViewSource("public sealed partial class GenericMethodView : AUIView", "[RelayCommand] private void Go<T>() { }"),
            "OBF132");
    }

    [Test]
    public void RefParameterReportsObf133()
    {
        AssertGeneratorDiagnostics(
            ViewSource("public sealed partial class RefParameterView : AUIView", "[RelayCommand] private void Go(ref int value) { }"),
            "OBF133");
    }

    [Test]
    public void CancellationTokenOnVoidCommandReportsObf133()
    {
        AssertGeneratorDiagnostics(
            ViewSource("public sealed partial class SyncTokenView : AUIView", "[RelayCommand] private void Go(CancellationToken ct) { }"),
            "OBF133");
    }

    [Test]
    public void TwoBusinessParametersReportObf133()
    {
        AssertGeneratorDiagnostics(
            ViewSource(
                "public sealed partial class TwoParameterView : AUIView",
                "[RelayCommand] private Task GoAsync(string first, string second) => Task.CompletedTask;"),
            "OBF133");
    }

    [Test]
    public void GenericTaskReturnTypeReportsObf134()
    {
        AssertGeneratorDiagnostics(
            ViewSource(
                "public sealed partial class TaskResultView : AUIView",
                "[RelayCommand] private Task<int> GoAsync() => Task.FromResult(1);"),
            "OBF134");
    }

    [Test]
    public void CollidingCommandNamesReportObf135()
    {
        var source = ViewSource(
            "public sealed partial class CollidingCommandsView : AUIView",
            "[RelayCommand] private void Submit() { }\n[RelayCommand] private void SubmitAsync() { }");
        AssertGeneratorDiagnostics(source, "OBF135");
    }

    [Test]
    public void MissingCanExecuteReportsObf136()
    {
        AssertGeneratorDiagnostics(
            ViewSource(
                "public sealed partial class MissingCanExecuteView : AUIView",
                "[RelayCommand(CanExecute = \"Missing\")] private void Go() { }"),
            "OBF136");
    }

    [Test]
    public void InvalidCanExecuteReportsObf137()
    {
        var source = ViewSource(
            "public sealed partial class InvalidCanExecuteView : AUIView",
            "[RelayCommand(CanExecute = \"Nope\")] private void Go() { }\nprivate void Nope() { }");
        AssertGeneratorDiagnostics(source, "OBF137");
    }

    [Test]
    public void AsyncOnlyOptionOnVoidCommandReportsObf138Warning()
    {
        var source = ViewSource(
            "public sealed partial class IgnoredOptionView : AUIView",
            "[RelayCommand(AllowConcurrentExecutions = true)] private void Go() { }");
        var compilation = CdeTestHarness.Compile(source, emit: false);
        Assert.That(compilation.GeneratorDiagnostics.Select(diagnostic => diagnostic.Id).ToArray(), Is.EqualTo(new[] { "OBF138" }));
        Assert.That(compilation.GeneratorDiagnostics.Single().Severity, Is.EqualTo(DiagnosticSeverity.Warning));
    }

    [Test]
    public void ValidViewProducesNoDiagnostics()
    {
        var source = ViewSource(
            "public sealed partial class ValidView : AUIView",
            "[ObservableProperty] private int _count;\n[RelayCommand] private void Go() { }");
        var compilation = CdeTestHarness.Compile(source, emit: false);
        Assert.That(compilation.GeneratorDiagnostics, Is.Empty, CdeTestHarness.FormatDiagnostics(compilation.GeneratorDiagnostics));
    }

    #endregion

    private static CdeCompilation CompileAndAssertClean(string source)
    {
        var compilation = CdeTestHarness.Compile(source);
        Assert.That(
            compilation.GeneratorDiagnostics,
            Is.Empty,
            CdeTestHarness.FormatDiagnostics(compilation.GeneratorDiagnostics));
        Assert.That(compilation.EmitResult, Is.Not.Null);
        var emitDiagnostics = compilation.EmitResult!.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(
            emitDiagnostics,
            Is.Empty,
            "generated code did not compile:" + Environment.NewLine
                + string.Join(Environment.NewLine, emitDiagnostics.Select(diagnostic => diagnostic.ToString()))
                + Environment.NewLine + "--- generated ---" + Environment.NewLine
                + CdeTestHarness.GetGeneratedSources(compilation));
        Assert.That(compilation.Assembly, Is.Not.Null);
        return compilation;
    }
}
