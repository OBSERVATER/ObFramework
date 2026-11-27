using ObFramework.Cli.Commands.Config;
using ObFramework.Cli.Commands.Config.Modules;

namespace ObFramework.Cli.Commands.Init;

internal static class GasStarter
{
    internal static void Install(InitContext context)
    {
        var document = SetupJsonDocument.Load(context.SetupFile);
        var config = document.GetModuleConfig<LubanSetupConfig>("luban")!;
        var primary = SetupConfigPaths.GetFullModulePath(context.Root, config.ConfPath);
        var conf = string.IsNullOrEmpty(config.GasConfPath)
            ? Path.Combine(Path.GetDirectoryName(primary)!, "GAS", "luban.conf")
            : SetupConfigPaths.GetFullModulePath(context.Root, config.GasConfPath);
        var directory = Path.GetDirectoryName(conf)!;
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "Attribute.xlsx", "Effect.xlsx", "Ability.xlsx", "schema.xml", "luban.conf" })
        {
            var path = name == "luban.conf" ? conf : Path.Combine(directory, name);
            if (File.Exists(path)) continue;
            using var input = typeof(GasStarter).Assembly.GetManifestResourceStream("GasStarter/" + name)!;
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = File.Create(temp)) input.CopyTo(output);
                if (!File.Exists(path)) File.Move(temp, path);
            }
            finally { File.Delete(temp); }
        }
        config.GasConfPath = Path.GetRelativePath(context.Root, conf).Replace('\\', '/');
        if (!File.Exists(primary)) config.ConfPath = config.GasConfPath;
        document.SetModuleConfig("luban", config);
        document.Save();
        Console.WriteLine($"Init: GAS workbooks ready in {directory}. Run ob table validate, then ob table generate.");
    }
}
