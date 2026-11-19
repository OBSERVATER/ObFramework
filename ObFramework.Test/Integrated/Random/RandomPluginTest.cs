using ObFramework.Integrated.Random;

namespace ObFramework.Test.Integrated.Random;

[TestFixture]
public class RandomPluginTest
{
    private sealed class BoundarySource(float value = 0.25f, bool useUpperBound = true) : IRandomSource
    {
        public int Seed => 42;
        public int IntegerCalls { get; private set; }
        public int ValueReads { get; private set; }
        public int Range(int minInclude, int maxInclude)
        {
            IntegerCalls++;
            return useUpperBound ? maxInclude : minInclude;
        }
        public float Range(float minInclude, float maxInclude) => useUpperBound ? maxInclude : minInclude;
        public float RandomValue01 { get { ValueReads++; return value; } }
    }

    [Test]
    public void RandomItemCanSelectTheLastElementAndASingleElement()
    {
        var plugin = new RandomPlugin(new BoundarySource());
        Assert.That(plugin.GetRandomItem(new List<int> { 10, 20, 30 }), Is.EqualTo(30));
        Assert.That(plugin.GetRandomItem(new List<int> { 42 }), Is.EqualTo(42));
    }

    [Test]
    public void RandomItemRejectsNullOrEmptyWithoutDrawing()
    {
        var source = new BoundarySource();
        var plugin = new RandomPlugin(source);
        Assert.Throws<ArgumentNullException>(() => plugin.GetRandomItem<int>(null!));
        Assert.Throws<ArgumentException>(() => plugin.GetRandomItem(new List<int>()));
        Assert.That(source.IntegerCalls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ShuffleUsesInclusiveBoundsAndPreservesTheListAndItsItems(bool useUpperBound)
    {
        var source = new BoundarySource(useUpperBound: useUpperBound);
        var plugin = new RandomPlugin(source);
        var items = new List<int> { 1, 2, 3 };
        Assert.That(plugin.Shuffle(items), Is.SameAs(items));
        Assert.That(items, Is.EquivalentTo(new[] { 1, 2, 3 }));
        Assert.That(source.IntegerCalls, Is.EqualTo(2));
        Assert.That(items, Is.EqualTo(useUpperBound ? new[] { 1, 2, 3 } : new[] { 2, 3, 1 }));
    }

    [Test]
    public void EmptyAndSingleItemShufflesDoNotDraw()
    {
        var source = new BoundarySource();
        var plugin = new RandomPlugin(source);
        Assert.That(plugin.Shuffle(new List<int>()), Is.Empty);
        Assert.That(plugin.Shuffle(new List<int> { 42 }), Is.EqualTo(new[] { 42 }));
        Assert.Throws<ArgumentNullException>(() => plugin.Shuffle<int>(null!));
        Assert.That(source.IntegerCalls, Is.Zero);
    }

    [TestCase(0f, "A")]
    [TestCase(0.125f, "A")]
    [TestCase(0.25f, "B")]
    [TestCase(0.5f, "B")]
    [TestCase(1f, "B")]
    public void WeightedSelectionHandlesBucketBoundariesAndInclusiveOne(float draw, string expected)
    {
        var source = new BoundarySource(draw);
        var plugin = new RandomPlugin(source);
        Assert.That(plugin.GetByWeight(new Dictionary<string, float> { ["A"] = 1, ["B"] = 3 }), Is.EqualTo(expected));
        Assert.That(source.ValueReads, Is.EqualTo(1));
    }

    [TestCase(0.25f, "A")]
    [TestCase(0.75f, "B")]
    public void LargeFiniteWeightsDoNotOverflow(float draw, string expected)
    {
        var plugin = new RandomPlugin(new BoundarySource(draw));
        Assert.That(plugin.GetByWeight(new Dictionary<string, float>
        {
            ["A"] = float.MaxValue, ["B"] = float.MaxValue
        }), Is.EqualTo(expected));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void NonFiniteWeightsAreRejectedBeforeDrawing(float weight)
    {
        var source = new BoundarySource();
        var plugin = new RandomPlugin(source);
        Assert.Throws<ArgumentException>(() => plugin.GetByWeight(new Dictionary<string, float>
        {
            ["valid"] = 1, ["invalid"] = weight
        }));
        Assert.That(source.ValueReads, Is.Zero);
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    public void WeightsWithoutAPositiveEntryAreRejected(float weight)
    {
        var source = new BoundarySource();
        var plugin = new RandomPlugin(source);
        Assert.Throws<InvalidOperationException>(() => plugin.GetByWeight(new Dictionary<int, float> { [42] = weight }));
        Assert.That(source.ValueReads, Is.Zero);
    }

    [TestCase(0f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    public void ZeroAndNegativeEntriesAreNeverSelected(float draw)
    {
        var plugin = new RandomPlugin(new BoundarySource(draw));
        Assert.That(plugin.GetByWeight(new Dictionary<int, float>
        {
            [10] = 0, [20] = -1, [30] = 2, [40] = 0
        }), Is.EqualTo(30));
    }

    [Test]
    public void WeightedSelectionRejectsNullOrEmptyWithoutDrawing()
    {
        var source = new BoundarySource();
        var plugin = new RandomPlugin(source);
        Assert.Throws<ArgumentNullException>(() => plugin.GetByWeight<string>(null));
        Assert.Throws<ArgumentException>(() => plugin.GetByWeight(new Dictionary<string, float>()));
        Assert.That(source.ValueReads, Is.Zero);
    }
}
