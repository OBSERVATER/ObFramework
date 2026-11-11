using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using ObFramework.Generator.CDE;
using ObFramework.Generator.Unity;

namespace ObFramework.Test.Generator;

/// <summary>Result of running the CDE and Unity binding generators over an in-memory C#9 compilation.</summary>
internal sealed class UnityBindingCompilation
{
    public Compilation UpdatedCompilation { get; init; } = null!;

    public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; init; }

    public EmitResult? EmitResult { get; init; }

    public Assembly? Assembly { get; init; }

    public ImmutableArray<string> GeneratedSources { get; init; }

    public ImmutableArray<string> GeneratedHintNames { get; init; }
}

/// <summary>
/// Roslyn driver seam for the generated Unity engine bindings (T07/T08/T09/T10/T11).
/// It compiles a stubbed UnityEngine + ObFramework.Adapter.Unity.ViewBinding surface together with
/// representative View/VC sources, runs BOTH the engine-neutral <see cref="CdeGenerator"/> and the
/// Unity <see cref="ViewBindingGenerator"/>, and exposes the final compilation for assertions and
/// execution. The two generators cannot see each other's output, so this final compilation is the
/// cross-generator seam: it proves the emitted command/property surface is mutually compatible.
/// </summary>
internal static class UnityBindingTestHarness
{
    /// <summary>Stub engine + adapter surface used by generated code (only the members it uses).</summary>
    public const string SurfaceSource = """
        using System;
        using ObFramework.Core;
        using ObFramework.Integrated.UI;

        namespace UnityEngine
        {
            public class Object
            {
                public static bool operator ==(Object left, Object right) => ReferenceEquals(left, right);
                public static bool operator !=(Object left, Object right) => !ReferenceEquals(left, right);
                public override bool Equals(object obj) => ReferenceEquals(this, obj);
                public override int GetHashCode() => 0;
            }
            public class Component : Object { }
            public class Behaviour : Component { public bool isActiveAndEnabled = true; }
            public class MonoBehaviour : Behaviour { }
            public class GameObject : Object { }
            public static class Debug
            {
                public static void LogException(Exception exception) { }
                public static void LogException(Exception exception, Object context) { }
            }
        }

        namespace UnityEngine.Events
        {
            using System.Collections.Generic;

            public delegate void UnityAction();
            public delegate void UnityAction<T>(T value);

            public class UnityEvent
            {
                private readonly List<UnityAction> _calls = new List<UnityAction>();
                public void AddListener(UnityAction call) { if (!_calls.Contains(call)) _calls.Add(call); }
                public void RemoveListener(UnityAction call) => _calls.Remove(call);
                public void Invoke() { foreach (var call in _calls.ToArray()) call(); }
            }

            public class UnityEvent<T>
            {
                private readonly List<UnityAction<T>> _calls = new List<UnityAction<T>>();
                public void AddListener(UnityAction<T> call) { if (!_calls.Contains(call)) _calls.Add(call); }
                public void RemoveListener(UnityAction<T> call) => _calls.Remove(call);
                public void Invoke(T value) { foreach (var call in _calls.ToArray()) call(value); }
            }
        }

        namespace UnityEngine.UI
        {
            using UnityEngine.Events;

            public class Selectable : MonoBehaviour
            {
                public bool interactable;
            }

            public class Button : Selectable
            {
                public class ButtonClickedEvent : UnityEvent { }
                public ButtonClickedEvent onClick = new ButtonClickedEvent();
            }

            public class Text : MonoBehaviour
            {
                public string text;
            }

            public class InputField : Selectable
            {
                public class OnChangeEvent : UnityEvent<string> { }
                public class SubmitEvent : UnityEvent<string> { }
                private string _text;
                public string text
                {
                    get => _text;
                    set
                    {
                        if (_text == value) return;
                        _text = value;
                        onValueChanged.Invoke(value);
                    }
                }
                public OnChangeEvent onValueChanged = new OnChangeEvent();
                public SubmitEvent onEndEdit = new SubmitEvent();
            }

            public class Toggle : Selectable
            {
                public class ToggleEvent : UnityEvent<bool> { }
                private bool _isOn;
                public bool isOn
                {
                    get => _isOn;
                    set
                    {
                        if (_isOn == value) return;
                        _isOn = value;
                        onValueChanged.Invoke(value);
                    }
                }
                public ToggleEvent onValueChanged = new ToggleEvent();
            }

            public class Slider : Selectable
            {
                public class SliderEvent : UnityEvent<float> { }
                private float _value;
                public float value
                {
                    get => _value;
                    set
                    {
                        if (_value == value) return;
                        _value = value;
                        onValueChanged.Invoke(value);
                    }
                }
                public SliderEvent onValueChanged = new SliderEvent();
            }
        }

        namespace TMPro
        {
            using UnityEngine.Events;

            public class TMP_Text : UnityEngine.MonoBehaviour
            {
                public string text;
            }

            public class TMP_InputField : UnityEngine.UI.Selectable
            {
                public class OnChangeEvent : UnityEvent<string> { }
                public class SubmitEvent : UnityEvent<string> { }
                private string _text;
                public string text
                {
                    get => _text;
                    set
                    {
                        if (_text == value) return;
                        _text = value;
                        onValueChanged.Invoke(value);
                    }
                }
                public OnChangeEvent onValueChanged = new OnChangeEvent();
                public SubmitEvent onEndEdit = new SubmitEvent();
            }
        }

        namespace ObFramework.Adapter.Unity.ViewBinding
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class BindViewAttribute : Attribute
            {
                public BindViewAttribute(Type viewType, Type contractType) { }
            }

            public interface IGeneratedViewBinding
            {
                Type ControllerType { get; }
                object CreateView(IArchitecture architecture);
            }

            public interface IGeneratedUIBinding
            {
                void RefreshBindings();
            }

            public enum UIBindMode
            {
                OneWay = 0,
                TwoWay = 1,
            }

            [AttributeUsage(AttributeTargets.Field)]
            public sealed class UIBindDataAttribute : Attribute
            {
                public UIBindDataAttribute(string viewProperty) { ViewProperty = viewProperty; }
                public string ViewProperty { get; }
                public UIBindMode Mode { get; set; }
                public string Format { get; set; }
            }

            [AttributeUsage(AttributeTargets.Field)]
            public sealed class UIBindCommandAttribute : Attribute
            {
                public UIBindCommandAttribute(string commandTypeName) { CommandTypeName = commandTypeName; }
                public string CommandTypeName { get; }
                public string CanExecute { get; set; }
            }

            public static class GeneratedViewBinding
            {
                public static void OnEnable(UnityEngine.MonoBehaviour controller) { }
                public static void OnDisable(UnityEngine.MonoBehaviour controller) { }
                public static void OnDestroy(UnityEngine.MonoBehaviour controller) { }
                public static void Refresh(UnityEngine.MonoBehaviour controller)
                    => (controller as IGeneratedUIBinding)?.RefreshBindings();
            }

            /// <summary>
            /// Test double for the engine boundary. It mirrors the real implementation: dispatch through
            /// View.GetArchitecture(), count calls and capture reported exceptions instead of logging.
            /// </summary>
            public static class GeneratedUIBindingRuntime
            {
                public static int SyncSent;
                public static int AsyncSent;
                public static readonly System.Collections.Generic.List<Exception> Errors =
                    new System.Collections.Generic.List<Exception>();

                public static void Send(UnityEngine.MonoBehaviour context, AUIView view, ACommand command)
                {
                    SyncSent++;
                    try { view.GetArchitecture().SendCommand(command); }
                    catch (Exception error) { Report(context, error); }
                }

                public static void SendAsync(UnityEngine.MonoBehaviour context, AUIView view, AAsyncCommand command)
                {
                    AsyncSent++;
                    RunAsync(context, view, command);
                }

                private static async void RunAsync(UnityEngine.MonoBehaviour context, AUIView view, AAsyncCommand command)
                {
                    try { await view.GetArchitecture().SendCommandAsync(command); }
                    catch (Exception error) { Report(context, error); }
                }

                public static void Report(UnityEngine.MonoBehaviour context, Exception error) => Errors.Add(error);
            }
        }
        """;

    public static UnityBindingCompilation Generate(string source, bool emit = true, bool nullable = false)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is unavailable.");
        var references = trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Append(typeof(ObFramework.Integrated.UI.AUIView).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "UnityBindingSample_" + Guid.NewGuid().ToString("N"),
            new[]
            {
                CSharpSyntaxTree.ParseText(SurfaceSource, parseOptions),
                CSharpSyntaxTree.ParseText(source, parseOptions),
            },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable));

        // Both generators run in one driver. They cannot see each other's output; the emitted
        // compilation below is the cross-generator contract seam.
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[]
            {
                new ViewBindingGenerator().AsSourceGenerator(),
                new CdeGenerator().AsSourceGenerator(),
            },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

        var generatedSources = driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Select(generated => generated.SourceText.ToString())
            .ToImmutableArray();
        var generatedHintNames = driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Select(generated => generated.HintName)
            .ToImmutableArray();

        if (!emit)
        {
            return new UnityBindingCompilation
            {
                UpdatedCompilation = updatedCompilation,
                GeneratorDiagnostics = diagnostics,
                GeneratedSources = generatedSources,
                GeneratedHintNames = generatedHintNames,
            };
        }

        using var stream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(stream);
        var assembly = emitResult.Success ? Assembly.Load(stream.ToArray()) : null;
        return new UnityBindingCompilation
        {
            UpdatedCompilation = updatedCompilation,
            GeneratorDiagnostics = diagnostics,
            EmitResult = emitResult,
            Assembly = assembly,
            GeneratedSources = generatedSources,
            GeneratedHintNames = generatedHintNames,
        };
    }

    /// <summary>Compiles, emits and loads a sample; fails with the full compiler output when it does not emit.</summary>
    public static UnityBindingCompilation Compile(string source)
    {
        var compilation = Generate(source);
        if (compilation.EmitResult == null || !compilation.EmitResult.Success)
        {
            throw new InvalidOperationException(
                "Unity binding sample did not emit." + Environment.NewLine +
                "Diagnostics:" + Environment.NewLine +
                FormatDiagnostics(compilation.EmitResult?.Diagnostics ?? ImmutableArray<Diagnostic>.Empty) + Environment.NewLine +
                "Generated sources:" + Environment.NewLine +
                string.Join(Environment.NewLine + Environment.NewLine, compilation.GeneratedSources));
        }
        return compilation;
    }

    /// <summary>
    /// Invokes the compiled sample's static `Run` exercise. The exercise returns false or throws an
    /// InvalidOperationException with a precise message on the first failed expectation, so failures
    /// point at documented behavior rather than generated whitespace.
    /// </summary>
    public static void RunExercise(UnityBindingCompilation compilation, string typeName)
    {
        var assembly = compilation.Assembly ?? throw new InvalidOperationException("The compilation did not produce an assembly.");
        var type = assembly.GetType(typeName) ?? throw new InvalidOperationException($"Exercise type '{typeName}' was not found.");
        var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Exercise type '{typeName}' has no public static Run method.");
        try
        {
            var returned = method.Invoke(null, null);
            if (returned is Task task)
                task.GetAwaiter().GetResult();
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    public static string GetGeneratedSource(UnityBindingCompilation compilation, string suffix)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            compilation.GeneratedSources.Where(text => text.Contains(suffix, StringComparison.Ordinal)));
    }

    public static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));
    }
}
