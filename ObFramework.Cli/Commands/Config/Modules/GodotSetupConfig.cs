namespace ObFramework.Cli.Commands.Config.Modules;

public sealed class GodotSetupConfig
{
    [SetupPath(Description = "Generated Godot window and registry source directory")]
    public string ScriptOutputRoot { get; set; } = "Generated/UI";
}
