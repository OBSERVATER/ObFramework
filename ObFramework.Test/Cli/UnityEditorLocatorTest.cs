using System.Text.Json;
using ObFramework.Cli.Unity;

namespace ObFramework.Test.Cli;

public class UnityEditorLocatorTest
{
    [Test]
    public void SelectsExactVersionRegisteredInHub()
    {
        var directory = Directory.CreateTempSubdirectory("ob-unity-locator-");
        try
        {
            var expected = CreateEditor(Path.Combine(directory.FullName, "custom installation"));
            var other = CreateEditor(Path.Combine(directory.FullName, "other version"));
            var registry = JsonSerializer.Serialize(new
            {
                data = new[]
                {
                    new { version = "2022.3.58f1", location = new[] { other } },
                    new { version = "2022.3.59f1", location = new[] { Path.Combine(directory.FullName, "missing"), expected } }
                }
            });
            File.WriteAllText(Path.Combine(directory.FullName, "editors-v2.json"), registry);

            Assert.That(UnityEditorLocator.FindInstalled("2022.3.59f1", directory.FullName, directory.FullName),
                Is.EqualTo(expected));
            Assert.That(UnityEditorLocator.FindInstalled("2022.3.60f1", directory.FullName, directory.FullName),
                Is.Null);
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void FindsEditorInConfiguredOrDefaultHubDirectory(bool custom)
    {
        var directory = Directory.CreateTempSubdirectory("ob-unity-locator-");
        try
        {
            const string version = "2022.3.59f1";
            var defaultRoot = Path.Combine(directory.FullName, "default");
            var customRoot = Path.Combine(directory.FullName, "secondary");
            var expected = CreateEditor(Path.Combine(custom ? customRoot : defaultRoot, version));
            File.WriteAllText(Path.Combine(directory.FullName, "editors-v2.json"), "{");
            File.WriteAllText(Path.Combine(directory.FullName, "secondaryInstallPath.json"),
                JsonSerializer.Serialize(customRoot));

            Assert.That(UnityEditorLocator.FindInstalled(version, directory.FullName, defaultRoot), Is.EqualTo(expected));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static string CreateEditor(string directory)
    {
        var path = OperatingSystem.IsMacOS()
            ? Path.Combine(directory, "Unity.app", "Contents", "MacOS", "Unity")
            : Path.Combine(directory, "Editor", OperatingSystem.IsWindows() ? "Unity.exe" : "Unity");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }
}
