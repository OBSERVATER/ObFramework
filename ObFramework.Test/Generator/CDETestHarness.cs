using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using ObFramework.Generator.CDE;

namespace ObFramework.Test.Generator;

/// <summary>Result of running the CDE generator over an in-memory C#9 compilation.</summary>
internal sealed class CdeCompilation
{
    public Compilation UpdatedCompilation { get; init; } = null!;

    public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; init; }

    public EmitResult? EmitResult { get; init; }

    public Assembly? Assembly { get; init; }
}

/// <summary>
/// Roslyn driver seam for the CDE generator tests: compiles representative C#9 business Views
/// against the real Framework assembly, runs <see cref="CdeGenerator"/>, emits and loads the
/// result so generated behavior can be executed.
/// </summary>
internal static class CdeTestHarness
{
    public static CdeCompilation Compile(string source, bool emit = true)
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
            "CdeSample_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new CdeGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

        if (!emit)
            return new CdeCompilation { UpdatedCompilation = updatedCompilation, GeneratorDiagnostics = diagnostics };

        using var stream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(stream);
        var assembly = emitResult.Success ? Assembly.Load(stream.ToArray()) : null;
        return new CdeCompilation
        {
            UpdatedCompilation = updatedCompilation,
            GeneratorDiagnostics = diagnostics,
            EmitResult = emitResult,
            Assembly = assembly,
        };
    }

    /// <summary>
    /// Invokes the compiled sample's static `Run` exercise. The exercise throws an
    /// InvalidOperationException with a precise message on the first failed expectation, so the
    /// test failure points at the behavior rather than at generated whitespace.
    /// </summary>
    public static void RunExercise(CdeCompilation compilation, string typeName = "CdeSample.Exercise")
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

    public static string GetGeneratedSources(CdeCompilation compilation)
    {
        var trees = compilation.UpdatedCompilation.SyntaxTrees
            .Where(tree => tree.FilePath.EndsWith(".CDE.g.cs", StringComparison.Ordinal))
            .Select(tree => tree.ToString())
            .ToArray();
        return string.Join(Environment.NewLine + Environment.NewLine, trees);
    }

    public static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));
    }
}
