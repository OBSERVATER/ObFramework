using System.Net.Http.Headers;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace ObFramework.Cli.Commands.Init
{
    internal sealed class LubanReleaseInstaller : IDisposable
    {
        private const string Version = "v5.1.0";
        private const string ReleaseDownload = "https://github.com/focus-creative-games/luban/releases/download/" + Version + "/";
        private const string Marker = ".obf-luban-version";
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;

        public LubanReleaseInstaller() : this(new HttpClient(), true) { }

        internal LubanReleaseInstaller(HttpClient http) : this(http, false) { }

        private LubanReleaseInstaller(HttpClient http, bool ownsHttp)
        {
            _http = http;
            _ownsHttp = ownsHttp;
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ObFramework-CLI", "1.0"));
        }

        public void Install(string dll, string? agentDll, CancellationToken ct)
        {
            if (IsCurrent(dll) && (agentDll == null || IsCurrent(agentDll)))
            {
                Console.WriteLine($"Init: Luban {Version} already installed.");
                return;
            }

            // This release and its asset names are pinned. Download the same official assets
            // as the release page, without depending on the REST API's separate rate limit.
            InstallPackage("Luban", dll, ct);
            if (agentDll != null)
                InstallPackage("Luban.Agent", agentDll, ct);
        }

        private static bool IsCurrent(string dll)
        {
            var folder = Path.GetDirectoryName(dll)!;
            return File.Exists(dll) && File.Exists(Path.Combine(folder, Marker)) &&
                File.ReadAllText(Path.Combine(folder, Marker)).Trim() == Version;
        }

        private void InstallPackage(string package, string dll, CancellationToken ct)
        {
            if (IsCurrent(dll))
                return;

            var url = ReleaseDownload + package + ".7z";

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
                var archivePath = Path.Combine(temp, "package.7z");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using (var response = _http.Send(request,
                    HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    using var input = response.Content.ReadAsStream(ct);
                    using var output = File.Create(archivePath);
                    input.CopyTo(output);
                }
                ct.ThrowIfCancellationRequested();
                var extracted = Path.Combine(temp, "extracted");
                Directory.CreateDirectory(extracted);
                using (var archive = ArchiveFactory.OpenArchive(archivePath))
                    foreach (var entry in archive.Entries.Where(x => !x.IsDirectory))
                        entry.WriteToDirectory(extracted, new ExtractionOptions { ExtractFullPath = true, Overwrite = false });
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

        public void Dispose() { if (_ownsHttp) _http.Dispose(); }
    }
}
