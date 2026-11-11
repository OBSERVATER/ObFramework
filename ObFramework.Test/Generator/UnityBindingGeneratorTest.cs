using System.Reflection;
using Microsoft.CodeAnalysis;

namespace ObFramework.Test.Generator;

/// <summary>
/// End-to-end tests for the generated Unity engine bindings (T07/T08/T09/T10/T11). Each test runs
/// BOTH generators over representative C#9 View/VC source against a stubbed UnityEngine +
/// ObFramework.Adapter.Unity.ViewBinding surface, then emits and executes the final compilation so
/// cross-generator naming, subscription lifecycle and command dispatch are verified as code, not as
/// generated strings.
/// </summary>
[TestFixture]
public class UnityBindingGeneratorTest
{
    private const string BehaviorSource = """
        using System;
        using System.Reflection;
        using System.Threading.Tasks;
        using ObFramework.Adapter.Unity.ViewBinding;
        using ObFramework.Core;
        using ObFramework.Integrated.UI;
        using UnityEngine;

        namespace UnityBindingSample
        {
            public interface IShopView { }

            public sealed class ShopArchitecture : AArchitecture<ShopArchitecture>
            {
                protected override void Init() { }
            }

            public partial class ShopView : AUIView
            {
                [ObservableProperty] private string _title = "Shop";
                [ObservableProperty] private int _price = 10;
                [ObservableProperty] private bool _enabled = true;
                [ObservableProperty] private float _volume = 0.5f;
                [ObservableProperty]
                [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
                private bool _submitAllowed = true;

                public int SubmitCount;
                public int AsyncCount;
                public TaskCompletionSource<bool> Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                public TaskCompletionSource<bool> Finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                public bool CanSubmit() => SubmitAllowed;

                [RelayCommand(CanExecute = nameof(CanSubmit))]
                private void Submit() { SubmitCount++; }

                [RelayCommand]
                private async Task SendAsync()
                {
                    await Gate.Task;
                    AsyncCount++;
                    Finished.TrySetResult(true);
                }
            }

            [BindView(typeof(ShopView), typeof(IShopView))]
            public partial class ShopController : MonoBehaviour, IShopView
            {
                [UIBindData(nameof(ShopView.Title))]
                public UnityEngine.UI.Text TitleText;

                [UIBindData(nameof(ShopView.Volume), Format = "{0:F1}")]
                public UnityEngine.UI.Text VolumeText;

                [UIBindData(nameof(ShopView.Price), Mode = UIBindMode.TwoWay)]
                public UnityEngine.UI.InputField PriceInput;

                [UIBindData(nameof(ShopView.Enabled), Mode = UIBindMode.TwoWay)]
                public UnityEngine.UI.Toggle EnabledToggle;

                [UIBindData(nameof(ShopView.Volume), Mode = UIBindMode.TwoWay)]
                public UnityEngine.UI.Slider VolumeSlider;

                [UIBindCommand(nameof(ShopView.SubmitCommand))]
                public UnityEngine.UI.Button SubmitButton;

                [UIBindCommand(nameof(ShopView.SendCommand))]
                public UnityEngine.UI.Button SendButton;
            }

            public static class Exercise
            {
                public static bool Run()
                {
                    var architecture = ShopArchitecture.Interface;
                    try
                    {
                        var first = CreateController(architecture, out var firstView);
                        var second = CreateController(architecture, out var secondView);

                        Require(first.TitleText.text == "Shop", "initial OneWay refresh must display the current View value");
                        Require(first.PriceInput.text == "10", "initial TwoWay refresh must display the numeric View value");
                        Require(first.EnabledToggle.isOn, "initial TwoWay refresh must display the boolean View value");
                        Require(first.VolumeSlider.value == 0.5f, "initial TwoWay refresh must display the float View value");
                        Require(first.VolumeText.text == "0.5", "Format must be applied through string.Format");
                        Require(first.SubmitButton.interactable, "interactable must follow the generated command CanExecute");

                        firstView.Title = "Hello";
                        Require(first.TitleText.text == "Hello", "PropertyChanged must refresh the bound OneWay Text");
                        Require(second.TitleText.text == "Shop", "another View instance must keep its own displayed values");

                        first.PriceInput.onValueChanged.Invoke("42");
                        Require(firstView.Price == 42, "TwoWay InputField input must write the parsed View property");
                        Require(first.PriceInput.text == "42", "TwoWay refresh must not corrupt the control value");

                        firstView.Price = 7;
                        Require(first.PriceInput.text == "7", "View property changes must flow back to the input control");

                        first.EnabledToggle.onValueChanged.Invoke(false);
                        Require(!firstView.Enabled, "TwoWay Toggle input must write the bool View property");

                        first.VolumeSlider.onValueChanged.Invoke(0.25f);
                        Require(firstView.Volume == 0.25f, "TwoWay Slider input must write the float View property");

                        firstView.SubmitAllowed = false;
                        Require(!first.SubmitButton.interactable, "a NotifyCanExecuteChangedFor dependency must refresh interactable");
                        firstView.SubmitAllowed = true;
                        Require(first.SubmitButton.interactable, "CanExecute must re-enable the control when the dependency changes");

                        first.SubmitButton.onClick.Invoke();
                        second.SubmitButton.onClick.Invoke();
                        second.SubmitButton.onClick.Invoke();
                        Require(firstView.SubmitCount == 1, "a button click must dispatch the command of its own View");
                        Require(secondView.SubmitCount == 2, "a second controller must dispatch into its own View instance");
                        Require(GeneratedUIBindingRuntime.SyncSent == 3, "button handlers must dispatch through GeneratedUIBindingRuntime.Send");

                        ((IGeneratedUIBinding)first).RefreshBindings();
                        ((IGeneratedUIBinding)first).RefreshBindings();
                        first.SubmitButton.onClick.Invoke();
                        Require(firstView.SubmitCount == 2, "RefreshBindings must stay idempotent and not duplicate control subscriptions");

                        var disable = typeof(ShopController).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
                        var enable = typeof(ShopController).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
                        Require(disable != null && enable != null, "generated OnEnable/OnDisable must exist");
                        var titleWhileEnabled = firstView.Title;
                        disable.Invoke(first, null);
                        firstView.Title = "WhileDisabled";
                        Require(first.TitleText.text == titleWhileEnabled, "OnDisable must unsubscribe display bindings");
                        enable.Invoke(first, null);
                        Require(first.TitleText.text == "WhileDisabled", "OnEnable must resubscribe and refresh current values");

                        Require(GeneratedUIBindingRuntime.Errors.Count == 0, "no binding handler may report an exception");
                        return true;
                    }
                    finally
                    {
                        architecture.Deinit();
                    }
                }

                private static ShopController CreateController(IArchitecture architecture, out ShopView view)
                {
                    var controller = new ShopController
                    {
                        TitleText = new UnityEngine.UI.Text(),
                        VolumeText = new UnityEngine.UI.Text(),
                        PriceInput = new UnityEngine.UI.InputField(),
                        EnabledToggle = new UnityEngine.UI.Toggle(),
                        VolumeSlider = new UnityEngine.UI.Slider(),
                        SubmitButton = new UnityEngine.UI.Button(),
                        SendButton = new UnityEngine.UI.Button(),
                    };
                    view = (ShopView)((IGeneratedViewBinding)controller).CreateView(architecture);
                    ((ICanSetArchitecture)view).SetArchitecture(architecture);
                    return controller;
                }

                private static void Require(bool condition, string message)
                {
                    if (!condition)
                        throw new InvalidOperationException(message);
                }
            }

            public static class AsyncExercise
            {
                public static async Task<bool> Run()
                {
                    var architecture = ShopArchitecture.Interface;
                    try
                    {
                        var controller = new ShopController
                        {
                            TitleText = new UnityEngine.UI.Text(),
                            VolumeText = new UnityEngine.UI.Text(),
                            PriceInput = new UnityEngine.UI.InputField(),
                            EnabledToggle = new UnityEngine.UI.Toggle(),
                            VolumeSlider = new UnityEngine.UI.Slider(),
                            SubmitButton = new UnityEngine.UI.Button(),
                            SendButton = new UnityEngine.UI.Button(),
                        };
                        var view = (ShopView)((IGeneratedViewBinding)controller).CreateView(architecture);
                        ((ICanSetArchitecture)view).SetArchitecture(architecture);

                        Require(controller.SendButton.interactable, "async command must start interactable");
                        var command = new ShopView.SendCommand(view);
                        var execution = architecture.SendCommandAsync(command);
                        Require(!controller.SendButton.interactable, "CommandStateChanged must disable the control while the async command runs");
                        Require(view.IsCommandRunning("SendCommand"), "the generated command name must be running while executing");
                        view.Gate.TrySetResult(true);
                        await execution;
                        Require(controller.SendButton.interactable, "CommandStateChanged must re-enable the control when the command finishes");
                        Require(view.AsyncCount == 1, "the async command must invoke the business method once");

                        view.Finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        controller.SendButton.onClick.Invoke();
                        Require(GeneratedUIBindingRuntime.AsyncSent == 1, "async handlers must dispatch through GeneratedUIBindingRuntime.SendAsync");
                        await view.Finished.Task;
                        Require(view.AsyncCount == 2, "a generated async handler must execute the business method");
                        Require(GeneratedUIBindingRuntime.Errors.Count == 0, "async exceptions must not be reported for successful commands");
                        return true;
                    }
                    finally
                    {
                        architecture.Deinit();
                    }
                }

                private static void Require(bool condition, string message)
                {
                    if (!condition)
                        throw new InvalidOperationException(message);
                }
            }
        }
        """;

    private const string HandWrittenCommandSource = """
        using ObFramework.Adapter.Unity.ViewBinding;
        using ObFramework.Core;
        using ObFramework.Integrated.UI;
        using UnityEngine;

        namespace UnityBindingManualSample
        {
            public interface IManualView { }

            public partial class ManualView : AUIView
            {
                public int Clicks;

                public sealed class ClickCommand : AUICommand
                {
                    public ClickCommand(ManualView view) : base(view, "ClickCommand") { }

                    protected override void ExecuteCore() { ((ManualView)View).Clicks++; }
                }
            }

            public sealed class ManualArchitecture : AArchitecture<ManualArchitecture>
            {
                protected override void Init() { }
            }

            [BindView(typeof(ManualView), typeof(IManualView))]
            public partial class ManualController : MonoBehaviour, IManualView
            {
                [UIBindCommand(nameof(ManualView.ClickCommand))]
                public UnityEngine.UI.Button ClickButton;
            }

            public static class Exercise
            {
                public static bool Run()
                {
                    var architecture = ManualArchitecture.Interface;
                    try
                    {
                        var controller = new ManualController { ClickButton = new UnityEngine.UI.Button() };
                        var view = (ManualView)((IGeneratedViewBinding)controller).CreateView(architecture);
                        ((ICanSetArchitecture)view).SetArchitecture(architecture);

                        if (!controller.ClickButton.interactable) return false;
                        controller.ClickButton.onClick.Invoke();
                        if (view.Clicks != 1) return false;
                        if (GeneratedUIBindingRuntime.SyncSent != 1) return false;
                        if (GeneratedUIBindingRuntime.Errors.Count != 0) return false;
                        return true;
                    }
                    finally
                    {
                        architecture.Deinit();
                    }
                }
            }
        }
        """;

    private const string ParameterizedCommandSource = """
        using System;
        using System.Threading.Tasks;
        using ObFramework.Adapter.Unity.ViewBinding;
        using ObFramework.Core;
        using ObFramework.Integrated.UI;
        using UnityEngine;

        namespace UnityBindingValueSample
        {
            public interface IValueView { }

            public sealed class ValueArchitecture : AArchitecture<ValueArchitecture>
            {
                protected override void Init() { }
            }

            public partial class ValueView : AUIView
            {
                [ObservableProperty] private string _lastName = "";
                [ObservableProperty] private bool _lastToggle;
                [ObservableProperty] private int _lastIndex;
                public int AsyncCount;
                public TaskCompletionSource<bool> Finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                [RelayCommand] private void Rename(string name) { LastName = name; }
                [RelayCommand] private void Toggle(bool value) { LastToggle = value; }
                [RelayCommand] private void Adjust(int value) { LastIndex = value; }

                [RelayCommand]
                private async Task SubmitAsync(string text)
                {
                    LastName = text;
                    await Task.Yield();
                    AsyncCount++;
                    Finished.TrySetResult(true);
                }
            }

            [BindView(typeof(ValueView), typeof(IValueView))]
            public partial class ValueController : MonoBehaviour, IValueView
            {
                [UIBindData(nameof(ValueView.LastName))]
                public TMPro.TMP_Text LastNameText;

                [UIBindData(nameof(ValueView.LastName), Mode = UIBindMode.TwoWay)]
                public TMPro.TMP_InputField NameInput;

                [UIBindCommand(nameof(ValueView.RenameCommand))]
                public UnityEngine.UI.InputField RenameInput;

                [UIBindCommand(nameof(ValueView.ToggleCommand))]
                public UnityEngine.UI.Toggle ToggleControl;

                [UIBindCommand(nameof(ValueView.AdjustCommand))]
                public UnityEngine.UI.Slider AdjustSlider;

                [UIBindCommand(nameof(ValueView.SubmitCommand))]
                public UnityEngine.UI.InputField SubmitInput;
            }

            public static class Exercise
            {
                public static async Task<bool> Run()
                {
                    var architecture = ValueArchitecture.Interface;
                    try
                    {
                        var controller = new ValueController
                        {
                            LastNameText = new TMPro.TMP_Text(),
                            NameInput = new TMPro.TMP_InputField(),
                            RenameInput = new UnityEngine.UI.InputField(),
                            ToggleControl = new UnityEngine.UI.Toggle(),
                            AdjustSlider = new UnityEngine.UI.Slider(),
                            SubmitInput = new UnityEngine.UI.InputField(),
                        };
                        var view = (ValueView)((IGeneratedViewBinding)controller).CreateView(architecture);
                        ((ICanSetArchitecture)view).SetArchitecture(architecture);

                        Require(controller.LastNameText.text == "", "TMP_Text OneWay refresh must display an empty string property");
                        controller.RenameInput.onEndEdit.Invoke("Ada");
                        Require(view.LastName == "Ada", "an InputField command must pass the submitted string as the event value");
                        Require(controller.LastNameText.text == "Ada", "TMP_Text must refresh after a command changed the View");
                        Require(controller.NameInput.text == "Ada", "TMP_InputField TwoWay refresh must display the View value");
                        controller.NameInput.onValueChanged.Invoke("Grace");
                        Require(view.LastName == "Grace", "TMP_InputField TwoWay input must write the View property");

                        controller.ToggleControl.onValueChanged.Invoke(true);
                        Require(view.LastToggle, "a Toggle command must pass the bool event value");

                        controller.AdjustSlider.onValueChanged.Invoke(2.6f);
                        Require(view.LastIndex == 3, "a Slider command must round the float event value to the int parameter");

                        controller.SubmitInput.onEndEdit.Invoke("async");
                        await view.Finished.Task;
                        Require(view.AsyncCount == 1, "an async value command must invoke the business method once");
                        Require(view.LastName == "async", "an async command must capture its event value");
                        Require(GeneratedUIBindingRuntime.Errors.Count == 0, "no binding handler may report an exception");
                        return true;
                    }
                    finally
                    {
                        architecture.Deinit();
                    }
                }

                private static void Require(bool condition, string message)
                {
                    if (!condition)
                        throw new InvalidOperationException(message);
                }
            }
        }
        """;

    [Test]
    public void BothGeneratorsEmitACompilableBindingSurface()
    {
        var compilation = UnityBindingTestHarness.Compile(BehaviorSource);
        Assert.That(
            UnityBindingTestHarness.FormatDiagnostics(compilation.GeneratorDiagnostics),
            Is.Empty,
            "valid View/VC declarations must not produce generator diagnostics");

        Assert.That(
            compilation.GeneratedHintNames.SingleOrDefault(name => name.EndsWith(".CDE.g.cs", StringComparison.Ordinal)),
            Is.Not.Null,
            "the CDE generator must emit the View surface");
        Assert.That(
            compilation.GeneratedHintNames.SingleOrDefault(name => name.EndsWith(".UIBinding.g.cs", StringComparison.Ordinal)),
            Is.Not.Null,
            "the Unity generator must emit the extra engine binding partial");
        Assert.That(
            compilation.GeneratedHintNames.SingleOrDefault(name => name.EndsWith(".ViewBinding.g.cs", StringComparison.Ordinal)),
            Is.Not.Null,
            "the existing IGeneratedViewBinding factory must keep being generated");

        var generated = string.Join(Environment.NewLine, compilation.GeneratedSources);
        Assert.That(generated, Does.Contain("IGeneratedUIBinding"), "the controller must implement IGeneratedUIBinding");
        Assert.That(generated, Does.Contain("GeneratedUIBindingRuntime.Send"), "sync commands must dispatch through the runtime boundary");
        Assert.That(generated, Does.Contain("GeneratedUIBindingRuntime.SendAsync"), "async commands must dispatch through the runtime boundary");
        Assert.That(generated, Does.Not.Contain("Activator"), "generated bindings must not use reflection");
        Assert.That(generated, Does.Not.Contain("GetProperty("), "generated bindings must not look up properties at runtime");
        Assert.That(generated, Does.Not.Contain("MethodInfo"), "generated bindings must not use reflection");

        var controllerType = compilation.Assembly!.GetType("UnityBindingSample.ShopController");
        Assert.That(controllerType, Is.Not.Null);
        Assert.That(
            controllerType!.GetInterfaces().Any(type => type.Name == "IGeneratedUIBinding"),
            Is.True,
            "the generated partial must implement IGeneratedUIBinding");
        Assert.That(
            controllerType.GetProperty("View", BindingFlags.Instance | BindingFlags.Public),
            Is.Not.Null,
            "the generated partial must expose the typed business View");
    }

    [Test]
    public void GeneratedBindingsRefreshFormatTwoWayValuesAndDispatchCommands()
    {
        var compilation = UnityBindingTestHarness.Compile(BehaviorSource);
        Assert.That(UnityBindingTestHarness.FormatDiagnostics(compilation.GeneratorDiagnostics), Is.Empty);
        UnityBindingTestHarness.RunExercise(compilation, "UnityBindingSample.Exercise");
    }

    [Test]
    public void GeneratedAsyncCommandHandlerUsesTheEngineBoundaryAndRunningState()
    {
        var compilation = UnityBindingTestHarness.Compile(BehaviorSource);
        Assert.That(UnityBindingTestHarness.FormatDiagnostics(compilation.GeneratorDiagnostics), Is.Empty);
        UnityBindingTestHarness.RunExercise(compilation, "UnityBindingSample.AsyncExercise");
    }

    [Test]
    public void HandWrittenCommandBindingDispatchesThroughTheSameAttribute()
    {
        var compilation = UnityBindingTestHarness.Compile(HandWrittenCommandSource);
        Assert.That(UnityBindingTestHarness.FormatDiagnostics(compilation.GeneratorDiagnostics), Is.Empty);
        UnityBindingTestHarness.RunExercise(compilation, "UnityBindingManualSample.Exercise");
    }

    [Test]
    public void ParameterizedCommandsReceiveControlEventValuesAndSupportTmpControls()
    {
        var compilation = UnityBindingTestHarness.Compile(ParameterizedCommandSource);
        Assert.That(UnityBindingTestHarness.FormatDiagnostics(compilation.GeneratorDiagnostics), Is.Empty);
        UnityBindingTestHarness.RunExercise(compilation, "UnityBindingValueSample.Exercise");
    }
}
