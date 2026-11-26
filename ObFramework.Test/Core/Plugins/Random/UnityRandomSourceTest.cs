using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ObFramework.Integrated.Random;

namespace ObFramework.Test.Integrated.Random;

// Compile the actual adapter against a stateful Unity API double. Native Unity acceptance
// remains separate; these tests exercise the adapter's ownership of shared random state.
[TestFixture]
public class UnityRandomSourceTest
{
    private Type _sourceType = null!;
    private Type _globalType = null!;
    private PropertyInfo _globalState = null!;

    [OneTimeSetUp]
    public void CompileAdapter()
    {
        const string relativePath = "ObFramework.Adapter.Unity/Random/UnityRandomSource.cs";
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, relativePath))) root = root.Parent;
        Assert.That(root, Is.Not.Null, "Run the tests from a source checkout.");
        var platformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references = platformAssemblies.Split(Path.PathSeparator)
            .Append(typeof(IRandomSource).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var options = new CSharpParseOptions(LanguageVersion.CSharp9);
        var compilation = CSharpCompilation.Create("RandomAdapterTest_" + Guid.NewGuid().ToString("N"),
            new[]
            {
                CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root!.FullName, relativePath)), options),
                CSharpSyntaxTree.ParseText(UnityApiDouble, options)
            }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        _sourceType = assembly.GetType("ObFramework.Adapter.Unity.Random.UnityRandomSource")!;
        _globalType = assembly.GetType("UnityEngine.Random")!;
        _globalState = _globalType.GetProperty("state")!;
    }

    [SetUp]
    public void ResetGlobalState()
    {
        _globalType.GetMethod("InitState")!.Invoke(null, new object[] { 987 });
        _globalType.GetField("FailNext")!.SetValue(null, false);
    }

    private IRandomSource Create(int seed) => (IRandomSource)Activator.CreateInstance(_sourceType, seed)!;
    private object GlobalState => _globalState.GetValue(null)!;
    private void ConsumeGlobal() => _globalType.GetProperty("value")!.GetValue(null);
    private static float[] DrawMixed(IRandomSource source) => new[]
    {
        source.Range(-100, 100), source.Range(-1f, 1f), source.RandomValue01
    };

    [Test]
    public void ConstructionAndEveryOperationPreserveGlobalState()
    {
        var before = GlobalState;
        var source = Create(123);
        Assert.That(GlobalState, Is.EqualTo(before), "construction");
        source.Range(0, 100);
        Assert.That(GlobalState, Is.EqualTo(before), "int Range");
        source.Range(0f, 1f);
        Assert.That(GlobalState, Is.EqualTo(before), "float Range");
        _ = source.RandomValue01;
        Assert.That(GlobalState, Is.EqualTo(before), "value");
    }

    [Test]
    public void InterleavedInstancesAndExternalDrawsDoNotAlterEitherSequence()
    {
        var firstExpected = Create(123);
        var secondExpected = Create(456);
        var first = Create(123);
        var second = Create(456);
        for (var i = 0; i < 100; i++)
        {
            ConsumeGlobal();
            Assert.That(DrawMixed(first), Is.EqualTo(DrawMixed(firstExpected)));
            ConsumeGlobal();
            Assert.That(DrawMixed(second), Is.EqualTo(DrawMixed(secondExpected)));
        }
    }

    [Test]
    public void FailedDrawRestoresGlobalState()
    {
        var source = Create(123);
        var before = GlobalState;
        _globalType.GetField("FailNext")!.SetValue(null, true);
        Assert.Throws<InvalidOperationException>(() => source.Range(0, 100));
        Assert.That(GlobalState, Is.EqualTo(before));
    }

    [TestCase(int.MaxValue, int.MaxValue)]
    [TestCase(int.MinValue, int.MaxValue)]
    [TestCase(0, int.MaxValue)]
    [TestCase(int.MaxValue - 3, int.MaxValue)]
    [TestCase(int.MinValue, int.MinValue)]
    public void IntegerClosedRangeHandlesExtremeBounds(int min, int max)
    {
        var source = Create(42);
        var values = Enumerable.Range(0, 1000).Select(_ => source.Range(min, max)).ToArray();
        Assert.That(values, Is.All.InRange(min, max));
        if ((long)max - min <= 3)
        {
            Assert.That(values, Does.Contain(min));
            Assert.That(values, Does.Contain(max));
        }
        if (min == int.MinValue && max == int.MaxValue)
        {
            Assert.That(values.Any(value => value < 0), Is.True);
            Assert.That(values.Any(value => value >= 0), Is.True);
        }
    }

    [TestCase(2, 1)]
    [TestCase(int.MaxValue, int.MinValue)]
    public void ReversedRangeFailsWithoutChangingGlobalState(int min, int max)
    {
        var source = Create(42);
        var before = GlobalState;
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Range(min, max));
        Assert.That(GlobalState, Is.EqualTo(before));
    }

    private const string UnityApiDouble = """
        namespace UnityEngine
        {
            public static class Random
            {
                public struct State { public uint Value; }
                public static State state { get; set; }
                public static bool FailNext;
                public static void InitState(int seed) => state = new State { Value = (uint)seed };
                private static uint Next()
                {
                    state = new State { Value = unchecked(state.Value * 1664525u + 1013904223u) };
                    if (FailNext) { FailNext = false; throw new System.InvalidOperationException("draw failed"); }
                    return state.Value;
                }
                public static int Range(int min, int max)
                {
                    if (min > max) { int previous = min; min = max; max = previous; }
                    if (min == max) return min;
                    return (int)(min + (long)(Next() % (ulong)((long)max - min)));
                }
                public static float Range(float min, float max) => min + value * (max - min);
                public static float value => (Next() >> 8) / (float)0xFFFFFF;
            }
        }
        """;
}
