using System.ComponentModel;
using System.Diagnostics;
using ObFramework.Cli.Commands.Config;
using ObFramework.Cli.Commands.Config.Modules;

namespace ObFramework.Cli.Commands.Table
{
    internal static class LubanRunner
    {
        public static async Task<int> RunAsync(string action, string setupFile, bool dataOnly, bool codeOnly, CancellationToken ct)
        {
            if (dataOnly && codeOnly)
                throw new SetupConfigException("--d and --c cannot be combined.");
            if (action != "generate" && action != "validate")
                throw new SetupConfigException($"Unknown table action '{action}'. Expected 'generate' or 'validate'.");
            if (action == "validate" && (dataOnly || codeOnly))
                throw new SetupConfigException("--d and --c only apply to generate.");

            var document = SetupJsonDocument.Load(setupFile);
            if (!document.TryGetAbsoluteRoot(out var root, out var error))
                throw new SetupConfigException(error ?? "setup.json root is not configured.");
            var config = document.GetModuleConfig<LubanSetupConfig>("luban") ?? new LubanSetupConfig();
            var registration = ConfigCliModule.CreateDefaultRegistry().Get("luban");
            var normalization = registration.NormalizePaths(root!, config);
            if (!normalization.IsValid)
                throw new SetupConfigException(string.Join(Environment.NewLine, normalization.Errors));

            string Resolve(string path) => SetupConfigPaths.GetFullModulePath(root!, path);
            var lubanDll = Resolve(config.LubanDll);
            var conf = Resolve(config.ConfPath);
            var codeDir = Resolve(config.CodeOutputRoot);
            var dataDir = Resolve(config.DataOutputRoot);
            if (!File.Exists(lubanDll))
                throw new SetupConfigException($"Luban.dll not found: {lubanDll}");
            if (!File.Exists(conf))
                throw new SetupConfigException($"luban.conf not found: {conf}");
            if (string.IsNullOrWhiteSpace(config.Target))
                throw new SetupConfigException("modules.luban.target must not be empty.");

            var agentDll = string.IsNullOrWhiteSpace(config.AgentDll) ? null : Resolve(config.AgentDll);
            if (action == "validate")
            {
                if (agentDll != null && File.Exists(agentDll))
                    return await StartAsync(agentDll, root!, ct, "validate", "--conf", conf, "-t", config.Target);
                return await StartAsync(lubanDll, root!, ct, "--conf", conf, "-t", config.Target,
                    "-f", "--strict", "--errorFormat", "json", "-x", "outputSaver=null");
            }

            using var templates = codeOnly || !dataOnly ? new LubanTemplateDirectory() : null;
            var args = new List<string> { "--conf", conf, "-t", config.Target, "--strict", "--errorFormat", "json" };
            if (!dataOnly)
            {
                args.AddRange(["-c", "cs-newtonsoft-json", "--customTemplateDir", templates!.Path,
                    "-x", "outputCodeDir=" + codeDir]);
            }
            if (!codeOnly)
                args.AddRange(["-d", "json", "-x", "outputDataDir=" + dataDir]);
            return await StartAsync(lubanDll, root!, ct, [..args]);
        }

        private static async Task<int> StartAsync(string dll, string workingDirectory, CancellationToken ct, params string[] args)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(dll);
            foreach (var arg in args)
                start.ArgumentList.Add(arg);

            using var process = Process.Start(start) ?? throw new Win32Exception("Failed to start dotnet.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(ct);
                await Task.WhenAll(stdout, stderr);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            Console.Write(await stdout);
            Console.Error.Write(await stderr);
            return process.ExitCode;
        }
    }
}
