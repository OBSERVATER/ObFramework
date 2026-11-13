using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ObFramework.Cli.Commands.Init
{
    internal sealed class LubanReleaseInstaller : IDisposable
    {
        private const string Version = "v5.1.0";
        private const string ReleaseApi = "https://api.github.com/repos/focus-creative-games/luban/releases/tags/" + Version;
        private const string Marker = ".obf-luban-version";
        private readonly HttpClient _http = new HttpClient();

        public LubanReleaseInstaller()
        {
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ObFramework-CLI", "1.0"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        public void Install(string dll, string? agentDll, CancellationToken ct)
        {
            if (IsCurrent(dll) && (agentDll == null || IsCurrent(agentDll)))
            {
                Console.WriteLine($"Init: Luban {Version} already installed.");
                return;
            }

            using var response = _http.Send(new HttpRequestMessage(HttpMethod.Get, ReleaseApi), ct);
            response.EnsureSuccessStatusCode();
            using var stream = response.Content.ReadAsStream(ct);
            using var release = JsonDocument.Parse(stream);
            var assets = release.RootElement.GetProperty("assets");
            InstallPackage(assets, "Luban", dll, ct);
            if (agentDll != null)
                InstallPackage(assets, "Luban.Agent", agentDll, ct);
        }

        private static bool IsCurrent(string dll)
        {
            var folder = Path.GetDirectoryName(dll)!;
            return File.Exists(dll) && File.Exists(Path.Combine(folder, Marker)) &&
                File.ReadAllText(Path.Combine(folder, Marker)).Trim() == Version;
        }

        private void InstallPackage(JsonElement assets, string package, string dll, CancellationToken ct)
        {
            if (IsCurrent(dll))
                return;

            string? url = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                var stem = Path.GetFileNameWithoutExtension(name);
                if (!stem.Equals(package, StringComparison.OrdinalIgnoreCase) &&
                    !stem.StartsWith(package + "-", StringComparison.OrdinalIgnoreCase) &&
                    !stem.StartsWith(package + ".", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (package == "Luban" && (stem.StartsWith("Luban.Agent", StringComparison.OrdinalIgnoreCase) ||
                    stem.StartsWith("Luban.Mcp", StringComparison.OrdinalIgnoreCase)))
                    continue;
                url = asset.GetProperty("browser_download_url").GetString();
                break;
            }
            if (url == null)
                throw new InvalidOperationException($"No {package} .zip found in Luban {Version} release.");

            var target = Path.GetDirectoryName(dll)!;
            var marker = Path.Combine(target, Marker);
            if (Directory.Exists(target) && !File.Exists(marker))
                throw new IOException($"'{target}' is not an OBF-managed Luban installation; move it before running ob init.");
            var parent = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(parent);
            var temp = Path.Combine(parent, ".obf-luban-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var zip = Path.Combine(temp, "package.zip");
                using (var response = _http.Send(new HttpRequestMessage(HttpMethod.Get, url),
                    HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    using var input = response.Content.ReadAsStream(ct);
                    using var output = File.Create(zip);
                    input.CopyTo(output);
                }
                ct.ThrowIfCancellationRequested();
                var extracted = Path.Combine(temp, "extracted");
                ZipFile.ExtractToDirectory(zip, extracted);
                var dlls = Directory.GetFiles(extracted, Path.GetFileName(dll), SearchOption.AllDirectories);
                if (dlls.Length != 1)
                    throw new IOException($"Expected exactly one '{Path.GetFileName(dll)}' in {package} archive, found {dlls.Length}.");
                var source = Path.GetDirectoryName(dlls[0])!;
                File.WriteAllText(Path.Combine(source, Marker), Version);
                var backup = target + ".obf-backup";
                if (Directory.Exists(backup))
                    throw new IOException($"Backup directory already exists: {backup}");
                if (Directory.Exists(target))
                    Directory.Move(target, backup);
                try
                {
                    Directory.Move(source, target);
                    if (Directory.Exists(backup))
                        Directory.Delete(backup, true);
                }
                catch
                {
                    if (Directory.Exists(backup) && !Directory.Exists(target))
                        Directory.Move(backup, target);
                    throw;
                }
                Console.WriteLine($"Init: installed {package} {Version} to {target}.");
            }
            finally
            {
                if (Directory.Exists(temp))
                    Directory.Delete(temp, true);
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
