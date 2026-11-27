using ObFramework.Cli.Commands.Config;
using ObFramework.Cli.Commands.Config.Modules;
using ObFramework.Cli.Commands.Init;

namespace ObFramework.Test.Cli;

[TestFixture]
public class GasStarterTest
{
    [TestCase(true)]
    [TestCase(false)]
    public void ReinitializationPreservesWorkbooksAndPrimarySchema(bool existingSchema)
    {
        var root = Directory.CreateTempSubdirectory("ob-gas-init-");
        try
        {
            var context = new InitContext(root.FullName, EngineKind.None, true, CancellationToken.None);
            new SetupInitStep().Execute(ref context);
            var primary = Path.Combine(root.FullName, "Configs", "luban.conf");
            if (existingSchema)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(primary)!);
                File.WriteAllText(primary, "project schema");
            }

            GasStarter.Install(context);
            var config = SetupJsonDocument.Load(context.SetupFile).GetModuleConfig<LubanSetupConfig>("luban")!;
            Assert.That(config.ConfPath, Is.EqualTo(existingSchema ? "Configs/luban.conf" : config.GasConfPath));
            var directory = Path.GetDirectoryName(Path.Combine(root.FullName, config.GasConfPath))!;
            var edited = Path.Combine(directory, "Ability.xlsx");
            File.WriteAllText(edited, "user edits");
            GasStarter.Install(context);
            Assert.That(File.ReadAllText(edited), Is.EqualTo("user edits"));
            Assert.That(File.Exists(Path.Combine(directory, "Attribute.xlsx")), Is.True);
            Assert.That(File.Exists(Path.Combine(directory, "Effect.xlsx")), Is.True);
            if (existingSchema) Assert.That(File.ReadAllText(primary), Is.EqualTo("project schema"));
        }
        finally { root.Delete(true); }
    }
}
