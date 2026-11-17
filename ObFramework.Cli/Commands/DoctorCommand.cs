using System.ComponentModel;
using System.Text.Json;
using ObFramework.Cli.Unity;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Commands;

internal sealed class DoctorCommand : Command<DoctorCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--root <DIR>")]
        public string? Root { get; set; }
        [CommandOption("--json")]
        public bool Json { get; set; }
        [CommandOption("--unity")]
        [Description("Run Unity batch import/compilation (writes Library and logs). Close the Editor first.")]
        public bool Unity { get; set; }
        [CommandOption("--unity-editor <FILE>")]
        public string? UnityEditor { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(settings.Root ?? Directory.GetCurrentDirectory());
        var checks = UnityDoctor.Check(root);
        var log = Path.Combine(root, ".obf/doctor-unity.log");
        if (settings.Unity && checks.All(x => x.Status != "fail"))
        {
            try
            {
                UnityProject.RequireClosed(root);
                var editor = settings.UnityEditor ?? Environment.GetEnvironmentVariable("UNITY_EDITOR_PATH");
                if (editor == null || !File.Exists(editor))
                    throw new InvalidOperationException("Supply --unity-editor <Unity executable> or UNITY_EDITOR_PATH.");
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                ToolProcess.Run(editor, new[] { "-batchmode", "-nographics", "-projectPath", root,
                    "-executeMethod", "ObFramework.Adapter.Unity.EditorTools.ObfBatchValidation.Run", "-logFile", log },
                    root, cancellationToken, TimeSpan.FromMinutes(10));
                checks.Add(new("OBD900", "pass", $"Unity import and compilation succeeded. Log: {log}"));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { checks.Add(new("OBD900", "fail", ex.Message + $" See Unity log: {log}")); }
        }
        else checks.Add(new("OBD900", "pending", "Unity import/compilation has not been verified; run doctor --unity --unity-editor <FILE>."));
        var success = checks.All(x => x.Status != "fail");
        if (settings.Json) Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, root, success, checks }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        else foreach (var check in checks) Console.WriteLine($"[{check.Status}] {check.Id}: {check.Message}");
        return success ? 0 : 1;
    }
}
