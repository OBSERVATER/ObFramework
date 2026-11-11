using MessagePack;

namespace ObFramework.Cli.Commands.Reference;

[MessagePackObject]
public record ReferenceNode
{
    public string Name { get; set; }
    public List<string>? Reference { get; set; }
}