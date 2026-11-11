using ObFramework.Core;
using ObFramework.Integrated.UI;
using ObFramework.Test.Core.UI;

namespace ObFramework.Test.Core.UI;

[TestFixture]
public class UIViewRegistryTest
{
    public sealed class RegisteredWindow : IUIWindow
    {
        public EUILevel Level => EUILevel.Common;
        public IArchitecture GetArchitecture() => throw new NotSupportedException();
    }

    [Test]
    public void RegisterStoresTheViewToWindowResourceMappingWithAnOptionalFactory()
    {
        var registry = new UIViewRegistry();
        registry.Register<FirstView, RegisteredWindow>("UI/First");

        Assert.That(registry.TryGet(typeof(FirstView), out var registration), Is.True);
        Assert.That(registration.WindowType, Is.EqualTo(typeof(RegisteredWindow)));
        Assert.That(registration.ResourceKey, Is.EqualTo("UI/First"));
        Assert.That(registration.Factory, Is.Null);
        Assert.That(registry.All.Count, Is.EqualTo(1));
    }

    [Test]
    public void FactoryBasedRegistrationCreatesTheViewOnDemand()
    {
        var registry = new UIViewRegistry();
        registry.Register(typeof(FirstView), typeof(RegisteredWindow), "UI/First", _ => new FirstView());
        Assert.That(registry.TryGet(typeof(FirstView), out var registration), Is.True);
        Assert.That(registration.Factory, Is.Not.Null);
        var view = registration.Factory!(null!);
        Assert.That(view, Is.InstanceOf<FirstView>());
    }

    [Test]
    public void DuplicateViewRegistrationIsRejected()
    {
        var registry = new UIViewRegistry();
        registry.Register<FirstView, RegisteredWindow>("UI/First");
        var error = Assert.Throws<InvalidOperationException>(() => registry.Register<FirstView, RegisteredWindow>("UI/Other"));
        Assert.That(error!.Message, Does.Contain("already registered"));
    }

    [Test]
    public void InvalidRegistrationsAreRejected()
    {
        var registry = new UIViewRegistry();
        Assert.Throws<ArgumentException>(() => registry.Register(typeof(object), typeof(RegisteredWindow), "UI/Bad"));
        Assert.Throws<ArgumentException>(() => registry.Register(typeof(FirstView), typeof(object), "UI/Bad"));
        Assert.Throws<ArgumentException>(() => registry.Register<FirstView, RegisteredWindow>(" "));
        Assert.That(registry.TryGet(typeof(SecondView), out _), Is.False);
        Assert.That(registry.TryGet(null!, out _), Is.False);
    }
}
