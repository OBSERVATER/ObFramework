using System.Diagnostics;
using NuGet.Versioning;
using ObFramework.Cli.Unity;

namespace ObFramework.Cli.Commands.Update;

internal static class UpdateWorker
{
    internal const string Argument = "--apply-queued-update";

    internal static NuGetVersion? GlobalVersion(string root, CancellationToken ct)
    {
        var tools = ToolProcess.Run("dotnet", ["tool", "list", "--global"], root, ct,
            TimeSpan.FromSeconds(15));
        foreach (var line in tools.Split('\n'))
        {
            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length >= 2 && columns[0].Equals(UpdateCatalog.PackageId, StringComparison.OrdinalIgnoreCase))
                return NuGetVersion.Parse(columns[1]);
        }
        return null;
    }

    internal static string Start(string root, string version, string editor)
    {
        var operation = Guid.NewGuid().ToString("N");
        var log = Path.Combine(root, ".obf", "updates", operation + ".log");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);

        // The worker loads a separate copy so global tool files are free when the launcher exits.
        var worker = Directory.CreateTempSubdirectory("ob-update-");
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(worker.FullName, Path.GetRelativePath(AppContext.BaseDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root
        };
        foreach (var value in new[] { Path.Combine(worker.FullName, "ObFramework.Cli.dll"), Argument,
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            root, version, editor, log })
            start.ArgumentList.Add(value);
        File.WriteAllText(log, $"Update to {version} queued.{Environment.NewLine}");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the update worker.");
        return log;
    }

    internal static int Run(string[] args)
    {
        if (args.Length != 6 || !int.TryParse(args[1], out var parentId)) return 1;
        var root = args[2];
        var target = args[3];
        var editor = args[4];
        using var log = File.AppendText(args[5]);
        log.AutoFlush = true;
        try
        {
            WaitForLauncher(parentId);
            Directory.CreateDirectory(UpdateCatalog.StateDirectory);
            using var updateLock = File.Open(Path.Combine(UpdateCatalog.StateDirectory, "update-install.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            UnityProject.Validate(root);
            UnityProject.RequireClosed(root);
            var global = GlobalVersion(root, CancellationToken.None);
            var version = NuGetVersion.Parse(target);
            if (global == null || global > version ||
                NuGetVersion.TryParse(UnityProject.FrameworkVersion(root), out var project) && project > version)
                throw new InvalidOperationException("The installation changed while the update was queued. Run ob update --check again.");

            var feed = Path.Combine(AppContext.BaseDirectory, "update.NuGet.Config");
            File.WriteAllText(feed, $"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="nuget.org" value="{UpdateCatalog.Source}" />
                  </packageSources>
                </configuration>
                """);
            var tools = Path.Combine(AppContext.BaseDirectory, "target-tool");
            log.WriteLine($"Downloading ObFramework CLI {target}.");
            log.WriteLine(ToolProcess.Run("dotnet",
                ["tool", "install", UpdateCatalog.PackageId, "--tool-path", tools, "--version", target, "--configfile", feed],
                root, CancellationToken.None));
            var ob = Path.Combine(tools, OperatingSystem.IsWindows() ? "ob.exe" : "ob");
            var init = new List<string> { "init", "--root", root, "--engine", "unity",
                "--package-version", target };
            if (UnityProject.LoadObject(Path.Combine(root, "setup.json"))["modules"]?["installation"]?
                ["skipLubanDownload"]?.GetValue<bool>() == true)
                init.Add("--skip-download");

            log.WriteLine($"Restoring the Unity project to {target}.");
            log.WriteLine(ToolProcess.Run(ob, init, root, CancellationToken.None, TimeSpan.FromMinutes(30)));
            log.WriteLine("Verifying Unity import and compilation.");
            log.WriteLine(ToolProcess.Run(ob, ["doctor", "--root", root, "--unity", "--unity-editor", editor],
                root, CancellationToken.None, TimeSpan.FromMinutes(12)));

            if (global < version)
            {
                log.WriteLine($"Updating the global CLI to {target}.");
                log.WriteLine(ToolProcess.Run("dotnet",
                    ["tool", "update", "--global", UpdateCatalog.PackageId, "--version", target, "--configfile", feed],
                    root, CancellationToken.None));
            }
            log.WriteLine($"Update complete: CLI and Unity project are at {target}.");
            return 0;
        }
        catch (Exception ex) when (UpdateCatalog.IsUpdateError(ex))
        {
            log.WriteLine($"Update failed: {ex.Message}");
            log.WriteLine($"After resolving the error, retry with 'ob update --version {target}'.");
            return 1;
        }
    }

    private static void WaitForLauncher(int parentId)
    {
        Process parent;
        try { parent = Process.GetProcessById(parentId); }
        catch (ArgumentException) { return; }
        using (parent)
        {
            if (!parent.WaitForExit(30_000))
                throw new InvalidOperationException("The CLI did not exit before the update deadline.");
        }
    }
}
