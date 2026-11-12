using ObFramework.Editor.Core;

namespace ObFramework.Test.Editor;

[TestFixture]
public class BindingTablePathTest
{
    [Test]
    public void LegacyAbsolutePathIsNormalizedToThePrefabRoot()
    {
        Assert.That(
            BindingTablePath.MakeRelative("ShopRoot/Buy/SubmitButton", "ShopRoot"),
            Is.EqualTo("Buy/SubmitButton"));
    }

    [Test]
    public void AlreadyRelativePathIsKept()
    {
        Assert.That(
            BindingTablePath.MakeRelative("Buy/SubmitButton", "ShopRoot"),
            Is.EqualTo("Buy/SubmitButton"));
    }

    [Test]
    public void RootItselfNormalizesToEmpty()
    {
        Assert.That(BindingTablePath.MakeRelative("ShopRoot", "ShopRoot"), Is.Empty);
        Assert.That(BindingTablePath.MakeRelative(string.Empty, "ShopRoot"), Is.Empty);
        Assert.That(BindingTablePath.MakeRelative(null, "ShopRoot"), Is.Empty);
    }

    [Test]
    public void SeparatorsAndOuterSlashesAreNormalized()
    {
        Assert.That(
            BindingTablePath.MakeRelative("\\ShopRoot\\Buy\\Button\\", "ShopRoot"),
            Is.EqualTo("Buy/Button"));
    }

    [Test]
    public void RootNameThatIsOnlyAPrefixOfASegmentIsNotStripped()
    {
        // "ShopRootExtra" is a different GameObject name; the path must not lose its first segment.
        Assert.That(
            BindingTablePath.MakeRelative("ShopRootExtra/Buy", "ShopRoot"),
            Is.EqualTo("ShopRootExtra/Buy"));
    }

    [Test]
    public void CombinePathBuildsRelativePathsInRootOrder()
    {
        Assert.That(BindingTablePath.CombinePath(new[] { "Buy", "Button" }), Is.EqualTo("Buy/Button"));
        Assert.That(BindingTablePath.CombinePath(Array.Empty<string>()), Is.Empty);
    }
}
