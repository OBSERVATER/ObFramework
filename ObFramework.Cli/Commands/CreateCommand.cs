using System.ComponentModel;
using ObFramework.Cli.Utility;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Commands
{
    internal sealed class CreateSettings : CommandSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("Generated class name")]
        public string Name { get; set; } = string.Empty;

        [CommandArgument(1, "<namespace>")]
        [Description("Generated class namespace")]
        public string Namespace { get; set; } = string.Empty;

        [CommandArgument(2, "<path>")]
        [Description("Output directory")]
        public string Path { get; set; } = string.Empty;

        [CommandArgument(3, "<template>")]
        [Description("Embedded template name")]
        public string Template { get; set; } = string.Empty;
    }

    internal sealed class CreateCommand : Command<CreateSettings>
    {
        public override int Execute(CommandContext context,CreateSettings settings,CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var template =TemplateGenerateHelper.LoadTemplate(settings.Template);
            var source =TemplateGenerateHelper.GenerateSourceCode(template,settings.Name,settings.Namespace);
            var savedPath =TemplateGenerateHelper.Save(source,settings.Name,settings.Path,settings.Template);
            Console.WriteLine($"Created {settings.Name}.cs at {Path.GetFullPath(savedPath)}");
            return 0;
        }
    }
}
