using System.Reflection;
using Microsoft.CodeAnalysis;
using ObFramework.Generator.Integrated.CDE;
using ObFramework.Generator.Unity;

namespace ObFramework.Test.Generator;

[TestFixture]
public sealed class GeneratorContractTest
{
    [Test]
    public void ExerciseReturningFalseMustFailTheTest()
    {
        var compilation = UnityBindingTestHarness.Compile("public static class FailedExercise { public static bool Run() => false; }");
        Assert.Throws<InvalidOperationException>(() => UnityBindingTestHarness.RunExercise(compilation, "FailedExercise"));
    }

    [TestCase("ObFramework.Generator")]
    [TestCase("ObFramework.Generator.Unity")]
    public void EveryDiagnosticHasAccurateReleaseMetadata(string project)
    {
        var assembly = project == "ObFramework.Generator" ? typeof(CdeGenerator).Assembly : typeof(ViewBindingGenerator).Assembly;
        var descriptors = assembly.GetTypes()
            .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (DiagnosticDescriptor)field.GetValue(null)!)
            .ToDictionary(descriptor => descriptor.Id);
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, project, project + ".csproj"))) root = root.Parent;
        Assert.That(root, Is.Not.Null, "Run the tests from a source checkout.");
        var entries = new[] { "AnalyzerReleases.Shipped.md", "AnalyzerReleases.Unshipped.md" }
            .SelectMany(file => File.ReadLines(Path.Combine(root!.FullName, project, file)))
            .Where(line => line.StartsWith("OBF", StringComparison.Ordinal))
            .Select(line => line.Split('|').Select(cell => cell.Trim().Trim('`')).ToArray())
            .ToDictionary(cells => cells[0]);
        Assert.That(entries.Keys, Is.EquivalentTo(descriptors.Keys), "Release tracking must include every declared diagnostic exactly once.");
        foreach (var descriptor in descriptors.Values)
        {
            Assert.That(entries[descriptor.Id][1], Is.EqualTo(descriptor.Category), descriptor.Id);
            Assert.That(entries[descriptor.Id][2], Is.EqualTo(descriptor.DefaultSeverity.ToString()), descriptor.Id);
        }
    }
}
