using ObFramework.Cli.Commands.Init;
using ObFramework.Cli.Unity;

namespace ObFramework.Test.Cli;

[TestFixture]
public class InitWizardTest
{
    [TestCase(null)]
    [TestCase("auto")]
    public void UnattendedInitRequiresRoot(string? engine)
    {
        var options = new InitCommand.Settings { NonInteractive = true, Engine = engine, SkipDownload = true };
        Assert.That(new InitCommand().Execute(null!, options, default), Is.EqualTo(1));
    }

    [Test]
    public void UnattendedInitDetectsGodotFromRoot()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "project.godot"), "");
            var options = new InitCommand.Settings { NonInteractive = true, Root = directory.FullName, SkipDownload = true };
            var output = new StringWriter();
            var original = Console.Out;
            try
            {
                Console.SetOut(output);
                Assert.That(new InitCommand().Execute(null!, options, default), Is.Zero);
            }
            finally { Console.SetOut(original); }
            Assert.That(output.ToString(), Does.Contain("engine: Godot"));
        }
        finally { directory.Delete(true); }
    }

    [Test]
    public void WizardResolvesEngineAfterRootAndKeepsUnitySpecificDefaults()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory.FullName, "Assets"));
            Directory.CreateDirectory(Path.Combine(directory.FullName, "ProjectSettings"));
            var transcript = new StringWriter();
            var options = new InitCommand.Settings();
            var answers = new StringReader(directory.FullName + "\nauto\n\n\n\n\n\n\n\n");
            new InitWizard(answers, transcript).Configure(options);
            Assert.That(options.Root, Is.EqualTo(directory.FullName));
            Assert.That(options.Resources, Is.EqualTo("addressables"));
            Assert.That(options.SkipDownload, Is.False);
            Assert.That(options.SkipPackages, Is.False);
            Assert.That(options.Gas, Is.False);
            Assert.That(transcript.ToString(), Does.Contain("auto detects Unity"));
            File.WriteAllText(Path.Combine(directory.FullName, "setup.json"), """{"modules":{"installation":{"resources":"custom"}}}""");
            Assert.That(UnityProject.ResourceChoice(directory.FullName, "addressables"), Is.EqualTo("custom"));
        }
        finally { directory.Delete(true); }
    }

    [Test]
    public void ExplicitOptionsSkipQuestionsAndChosenPathsReachSetup()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var options = new InitCommand.Settings { Root = directory.FullName, Engine = "auto", Gas = true, SkipDownload = true };
            var transcript = new StringWriter();
            new InitWizard(new StringReader("y\nConfigs/schema.conf\nSource/Tables\nContent/Data\n"), transcript).Configure(options);
            Assert.That(transcript.ToString(), Does.Not.Contain("Engine ("));
            Assert.That(transcript.ToString(), Does.Not.Contain("Unity resources"));
            var context = new InitContext(directory.FullName, EngineKind.None, true, default) { LubanPaths = options.LubanPaths };
            new SetupInitStep().Execute(ref context);
            Assert.That(File.ReadAllText(context.SetupFile), Does.Contain("Source/Tables").And.Contain("Content/Data"));
        }
        finally { directory.Delete(true); }
    }
}
