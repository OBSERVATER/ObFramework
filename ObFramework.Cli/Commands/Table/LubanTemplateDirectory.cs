using System.Reflection;

namespace ObFramework.Cli.Commands.Table
{
    internal sealed class LubanTemplateDirectory : IDisposable
    {
        private const string Prefix = "ObFramework.Cli.LubanTemplates/";
        private static readonly string[] Files = ["tables.sbn"];
        public string Path { get; }

        public LubanTemplateDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "obf-luban-" + Guid.NewGuid().ToString("N"));
            var templatePath = System.IO.Path.Combine(Path, "cs-newtonsoft-json");
            Directory.CreateDirectory(templatePath);
            foreach (var file in Files)
            {
                var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + "cs-newtonsoft-json/" + file)
                    ?? throw new InvalidOperationException($"Missing bundled Luban template: {file}");
                using (resource)
                using (var output = File.Create(System.IO.Path.Combine(templatePath, file)))
                    resource.CopyTo(output);
            }
        }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
