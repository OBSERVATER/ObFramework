using System.ComponentModel;
using Newtonsoft.Json;
using ObFramework.Cli.Utility;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Commands.Reference;
    public sealed class ReferenceSettings : CommandSettings
    {
        [CommandArgument(0, "<ACTION>")]
        [Description("add, link, unlink, find, reverse, list or delete")]
        public string Action { get; set; } = string.Empty;

        [CommandArgument(1, "[NODE]")]
        [Description("System or Model name")]
        public string Node { get; set; } = string.Empty;

        [CommandArgument(2, "[REFERENCES]")]
        [Description("Referenced System or Model names")]
        public string[] References { get; set; } = Array.Empty<string>();
    }

public sealed class ReferenceCommand : Command<ReferenceSettings>
{

    public override int Execute(CommandContext context, ReferenceSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var name = settings.Node;
        var references = settings.References;

        try
        {
            switch (settings.Action.ToLowerInvariant())
            {
                case "add":
                    return string.IsNullOrWhiteSpace(name) ? Fail("node name is required.") 
                        : Complete(ReferenceNodeHelper.CreateNode(name, references));

                case "link":
                    if (string.IsNullOrWhiteSpace(name) || references.Length == 0)
                        return Fail("node and references are required.");

                    return Complete(ReferenceNodeHelper.AddReference(name, references));

                case "unlink":
                    if (references.Length != 1)
                        return Fail("exactly one reference is required.");

                    return ReferenceNodeHelper.RemoveReference(name, references[0])
                        ? Success()
                        : Fail("node or reference not found.");

                case "delete":
                    return ReferenceNodeHelper.DeleteNode(name)
                        ? Success()
                        : Fail("node not found.");

                case "find":
                    if (!ReferenceNodeHelper.TryFindNode(name, out var result))
                        return Fail("node not found.");

                    Console.WriteLine(result);
                    return 0;

                case "reverse":
                    if (!ReferenceNodeHelper.TryFindReverseNode(name, out var reverseResult))
                        return Fail("node not found.");

                    Console.WriteLine(reverseResult);
                    return 0;

                case "list":
                    Console.WriteLine(JsonConvert.SerializeObject(
                        ReferenceNodeHelper.ListNodes(), Formatting.Indented));
                    return 0;

                default:
                    return Fail("unknown action. Use add, link, unlink, find, reverse, list or delete.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Fail(ex.Message);
        }
    }

    private static int Complete(string error) => string.IsNullOrEmpty(error) ? Success() : Fail(error);

    private static int Success()
    {
        Console.WriteLine("ok");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"Reference: {message}");
        return 1;
    }
}
