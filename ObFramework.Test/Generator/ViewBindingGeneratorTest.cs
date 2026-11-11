using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ObFramework.Core;
using ObFramework.Generator.Unity;

namespace ObFramework.Test.Generator;

[TestFixture]
public class ViewBindingGeneratorTest
{
    private const string AdapterSurface = """
        using System;
        using ObFramework.Core;
        using UnityEngine;
        namespace UnityEngine
        {
            public class Object { }
            public class MonoBehaviour : Object { }
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
            public static class GeneratedViewBinding
            {
                public static void OnEnable(MonoBehaviour controller) { }
                public static void OnDisable(MonoBehaviour controller) { }
                public static void OnDestroy(MonoBehaviour controller) { }
            }
        }
        """;

    [Test]
    public void GeneratedFactoryCreatesOneViewPerControllerWithExistingArchitectureDependencies()
    {
        const string source = """
            using ObFramework.Adapter.Unity.ViewBinding;
            using ObFramework.Core;
            using UnityEngine;
            public interface IMonsterView { }
            public interface IMonsterSystem : ISystem { }
            public sealed class MonsterSystem : ASystem, IMonsterSystem
            {
                protected override void OnInit() { }
            }
            public sealed class Game : AArchitecture<Game>
            {
                protected override void Init() => RegisterSystem<IMonsterSystem, MonsterSystem>();
            }
            public sealed class Monster
            {
                public readonly IMonsterView View;
                public readonly IMonsterSystem System;
                public Monster(IMonsterView view, IMonsterSystem system)
                {
                    View = view;
                    System = system;
                }
            }
            [BindView(typeof(Monster), typeof(IMonsterView))]
            public partial class MonsterController : MonoBehaviour, IMonsterView { }
            public static class Exercise
            {
                public static bool Run()
                {
                    var game = Game.Interface;
                    try
                    {
                        var first = new MonsterController();
                        var second = new MonsterController();
                        var left = (Monster)((IGeneratedViewBinding)first).CreateView(game);
                        var right = (Monster)((IGeneratedViewBinding)second).CreateView(game);
                        return !ReferenceEquals(left, right) &&
                               ReferenceEquals(left.View, first) &&
                               ReferenceEquals(right.View, second) &&
                               ReferenceEquals(left.System, game.GetSystem<IMonsterSystem>()) &&
                               ReferenceEquals(right.System, left.System);
                    }
                    finally { game.Deinit(); }
                }
            }
            """;
        var compilation = Generate(source, out var diagnostics);
        Assert.That(diagnostics, Is.Empty);

        using var assemblyBytes = new MemoryStream();
        var result = compilation.Emit(assemblyBytes);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        var assembly = Assembly.Load(assemblyBytes.ToArray());
        Assert.That(assembly.GetType("Exercise")!.GetMethod("Run")!.Invoke(null, null), Is.EqualTo(true));
    }

    [TestCase("class", ", IMonsterView", "", "", "OBFV001")]
    [TestCase("partial class", "", "", "", "OBFV002")]
    [TestCase("partial class", ", IMonsterView", "", "public Monster() { }", "OBFV003")]
    [TestCase("partial class", ", IMonsterView", ", string unsupported", "", "OBFV004")]
    [TestCase("partial class", ", IMonsterView", ", IAmbiguous ambiguous", "", "OBFV004")]
    [TestCase("partial class", ", IMonsterView", "", "", "OBFV005", "private void OnDisable() { }")]
    public void InvalidDeclarationsReportAnError(string declaration, string contract, string parameter,
        string extraConstructor, string expected, string callback = "")
    {
        var source = $$"""
            using ObFramework.Adapter.Unity.ViewBinding;
            using ObFramework.Core;
            using UnityEngine;
            public interface IMonsterView { }
            public interface IAmbiguous : ISystem, IModel { }
            public sealed class Monster
            {
                public Monster(IMonsterView view{{parameter}}) { }
                {{extraConstructor}}
            }
            [BindView(typeof(Monster), typeof(IMonsterView))]
            public {{declaration}} MonsterController : MonoBehaviour{{contract}}
            {
                {{callback}}
            }
            """;

        Generate(source, out var diagnostics);
        Assert.That(diagnostics.Select(error => error.Id), Is.EqualTo(new[] { expected }));
        Assert.That(diagnostics.Single().Severity, Is.EqualTo(DiagnosticSeverity.Error));
    }

    private static Compilation Generate(string source, out IReadOnlyList<Diagnostic> diagnostics)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
        var platform = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references = platform.Split(Path.PathSeparator).Append(typeof(IArchitecture).Assembly.Location)
            .Distinct().Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create($"ViewBinding_{Guid.NewGuid():N}",
            new[] { CSharpSyntaxTree.ParseText(AdapterSurface, parseOptions), CSharpSyntaxTree.ParseText(source, parseOptions) },
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new[] { new ViewBindingGenerator().AsSourceGenerator() }, parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var result, out var errors);
        diagnostics = errors;
        return result;
    }
}
