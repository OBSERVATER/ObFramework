using NuGet.Versioning;
using ObFramework.Cli.Commands.Update;
using ObFramework.Cli.Unity;

namespace ObFramework.Test.Cli;

public class UpdateTest
{
    [TestCase("0.1.4", "0.1.5", true)]
    [TestCase("0.1.4", "0.2.0", false)]
    [TestCase("1.0.0", "1.2.0", true)]
    [TestCase("1.0.0", "2.0.0", false)]
    [TestCase("0.1.0-preview.3", "0.1.0-preview.4", false)]
    [TestCase("0.1.0-preview.3", "0.1.0", false)]
    [TestCase("1.0.0", "1.1.0-preview.1", false)]
    public void EnforcesReleaseCompatibilityLines(string current, string candidate, bool compatible)
    {
        Assert.That(UpdateCatalog.IsCompatible(NuGetVersion.Parse(current), NuGetVersion.Parse(candidate)),
            Is.EqualTo(compatible));
    }

    [Test]
    public void OffersProjectUpgradeWhenGlobalCliIsAlreadyCurrent()
    {
        var available = UpdateCatalog.Available(
            new[] { "0.1.4", "0.1.5", "0.2.0-preview.2", "0.2.0-preview.10" }.Select(NuGetVersion.Parse),
            NuGetVersion.Parse("0.1.4"), NuGetVersion.Parse("0.1.5"));
        Assert.That(available.Select(version => version.ToNormalizedString()),
            Is.EqualTo(new[] { "0.2.0-preview.10", "0.2.0-preview.2", "0.1.5" }));
    }

    [Test]
    public void ReadsMatchingPackageVersionsInAnOlderProject()
    {
        var root = Directory.CreateTempSubdirectory("ob-update-test-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "Assets"));
            var packages = Path.Combine(root.FullName, "Assets", "packages.config");
            File.WriteAllText(packages, """
                <packages>
                  <package id="ObFramework" version="0.1.4" />
                  <package id="ObFramework.Adapter.Unity" version="0.1.4" />
                </packages>
                """);

            Assert.That(UnityProject.FrameworkVersion(root.FullName), Is.EqualTo("0.1.4"));
            File.WriteAllText(packages, File.ReadAllText(packages).Replace("Adapter.Unity\" version=\"0.1.4", "Adapter.Unity\" version=\"0.2.0"));
            Assert.Throws<InvalidOperationException>(() => UnityProject.FrameworkVersion(root.FullName));
        }
        finally { root.Delete(recursive: true); }
    }
}
