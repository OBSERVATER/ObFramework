using ObFramework.Integrated.Random;

namespace ObFramework.Test.Integrated.Random;

[TestFixture]
public class RandomSourceTest
{
    [TestCase(int.MaxValue, int.MaxValue)]
    [TestCase(int.MinValue, int.MinValue)]
    [TestCase(int.MinValue, int.MaxValue)]
    [TestCase(0, int.MaxValue)]
    [TestCase(int.MinValue, 0)]
    [TestCase(int.MaxValue - 3, int.MaxValue)]
    [TestCase(-10, 10)]
    public void IntegerClosedRangeHandlesExtremeBounds(int min, int max)
    {
        var source = new SystemRandomSource(42);
        var values = Enumerable.Range(0, 1000).Select(_ => source.Range(min, max)).ToArray();
        Assert.That(values, Is.All.InRange(min, max));
        if (min == int.MinValue && max == int.MaxValue)
        {
            Assert.That(values.Any(value => value < 0), Is.True);
            Assert.That(values.Any(value => value >= 0), Is.True);
        }
        if ((long)max - min <= 3)
        {
            Assert.That(values, Does.Contain(min));
            Assert.That(values, Does.Contain(max));
        }
    }

    [TestCase(2, 1)]
    [TestCase(int.MaxValue, int.MinValue)]
    public void IntegerRangeRejectsReversedBounds(int min, int max) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SystemRandomSource(42).Range(min, max));

    [TestCase(-float.MaxValue, float.MaxValue)]
    [TestCase(float.MaxValue, float.MaxValue)]
    [TestCase(-10f, 10f)]
    public void FloatRangeStaysFiniteAndWithinBounds(float min, float max)
    {
        var source = new SystemRandomSource(42);
        for (var i = 0; i < 1000; i++)
        {
            var value = source.Range(min, max);
            Assert.That(float.IsFinite(value), Is.True);
            Assert.That(value, Is.InRange(min, max));
        }
    }

    [Test]
    public void SameSeedReproducesMixedCalls()
    {
        var first = new SystemRandomSource(123);
        var second = new SystemRandomSource(123);
        for (var i = 0; i < 100; i++)
        {
            Assert.That(first.Range(int.MinValue, int.MaxValue), Is.EqualTo(second.Range(int.MinValue, int.MaxValue)));
            Assert.That(first.Range(0, int.MaxValue), Is.EqualTo(second.Range(0, int.MaxValue)));
            Assert.That(first.RandomValue01, Is.EqualTo(second.RandomValue01));
        }
    }
}
