namespace ObFramework.Cli.Commands.Reference;

public record ReferenceNode
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<string>? Reference { get; set; }
}