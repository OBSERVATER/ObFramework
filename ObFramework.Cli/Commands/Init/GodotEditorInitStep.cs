using ObFramework.Utility.Pipeline;

namespace ObFramework.Cli.Commands.Init;

internal sealed class GodotEditorInitStep : IPipelineStep<InitContext>
{
    public StepResult Execute(ref InitContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var addon = Path.Combine(context.Root, "addons", "obf");
        Directory.CreateDirectory(addon);
        CopyIfMissing("plugin.cfg", Path.Combine(addon, "plugin.cfg"));
        CopyIfMissing("ObfGodotEditor.cs", Path.Combine(addon, "ObfGodotEditor.cs"));
        CopyIfMissing("obf-ui.example.json", Path.Combine(context.Root, "obf-ui.example.json"));
        Console.WriteLine("Init: Godot editor files ready; add runtime, analyzer and Editor.Core references before building.");
        Console.WriteLine("Init: Godot NuGet installation is not implemented; use local project references.");
        return StepResult.Continue;
    }

    private static void CopyIfMissing(string name, string destination)
    {
        if (File.Exists(destination)) return;
        using var source = typeof(GodotEditorInitStep).Assembly.GetManifestResourceStream("ObFramework.Cli.GodotEditorDeploy/" + name)
            ?? throw new InvalidOperationException($"Missing embedded Godot editor file: {name}");
        using var target = File.Open(destination, FileMode.CreateNew);
        source.CopyTo(target);
    }
}
