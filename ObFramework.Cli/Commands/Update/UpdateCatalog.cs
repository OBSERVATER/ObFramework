using System.Net;
using System.Text.Json;
using NuGet.Versioning;
using ObFramework.Cli.Unity;

namespace ObFramework.Cli.Commands.Update;

internal static class UpdateCatalog
{
    internal const string Source = "https://api.nuget.org/v3/index.json";
    internal const string PackageId = "ObFramework.Cli";
    internal static string StateDirectory
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (home.Length == 0) throw new InvalidOperationException("No per-user application data directory is available.");
            return Path.Combine(home, "ObFramework");
        }
    }
    internal static NuGetVersion CliVersion => NuGetVersion.Parse(UnityPackageInstaller.DefaultVersion);

    internal static bool IsCompatible(NuGetVersion current, NuGetVersion target) =>
        !current.IsPrerelease && !target.IsPrerelease &&
        current.Major == target.Major && (current.Major != 0 || current.Minor == target.Minor);

    internal static NuGetVersion[] Available(IEnumerable<NuGetVersion> releases,
        NuGetVersion project, NuGetVersion cli) => releases
        .Where(version => version >= project && version >= cli && (version > project || version > cli))
        .OrderByDescending(version => version)
        .ToArray();

    internal static async Task<NuGetVersion[]> FetchAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        using var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
        using var service = await GetJsonAsync(http, Source, deadline.Token);
        var registration = service.RootElement.GetProperty("resources").EnumerateArray()
            .First(resource => resource.GetProperty("@type").GetString() == "RegistrationsBaseUrl/3.6.0")
            .GetProperty("@id").GetString()!;
        using var response = await http.GetAsync(
            new Uri(new Uri(registration), "obframework.cli/index.json"), deadline.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();
        using var index = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(deadline.Token),
            cancellationToken: deadline.Token);
        var versions = new List<NuGetVersion>();
        foreach (var page in index.RootElement.GetProperty("items").EnumerateArray())
        {
            if (page.TryGetProperty("items", out var items))
                ReadListedVersions(items, versions);
            else
            {
                using var document = await GetJsonAsync(http, page.GetProperty("@id").GetString()!, deadline.Token);
                ReadListedVersions(document.RootElement.GetProperty("items"), versions);
            }
        }
        return versions.ToArray();
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient http, string url, CancellationToken ct)
    {
        await using var stream = await http.GetStreamAsync(url, ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static void ReadListedVersions(JsonElement items, List<NuGetVersion> versions)
    {
        foreach (var item in items.EnumerateArray())
        {
            var entry = item.GetProperty("catalogEntry");
            if (entry.TryGetProperty("listed", out var listed) && !listed.GetBoolean()) continue;
            if (NuGetVersion.TryParse(entry.GetProperty("version").GetString(), out var version))
                versions.Add(version);
        }
    }

    internal static Task<string?> GetNoticeAsync(string root)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected)
            return Task.FromResult<string?>(null);
        return Task.Run(() => CheckOnceAsync(root));
    }

    private static async Task<string?> CheckOnceAsync(string root)
    {
        try
        {
            if (!NuGetVersion.TryParse(UnityProject.FrameworkVersion(root), out var project)) return null;
            Directory.CreateDirectory(StateDirectory);
            var cache = Path.Combine(StateDirectory, "update-versions.json");
            await using var cacheLock = File.Open(Path.Combine(StateDirectory, "update-check.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            NuGetVersion[] versions;
            if (File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromDays(1))
            {
                versions = (JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(cache)) ?? [])
                    .Select(NuGetVersion.Parse).ToArray();
            }
            else
            {
                // Record the attempt before the request so offline launches also observe the interval.
                await File.WriteAllTextAsync(cache, "[]");
                versions = await FetchAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
                await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(versions.Select(version => version.ToNormalizedString())));
            }

            var releases = Available(versions, project, CliVersion);
            var compatible = releases.FirstOrDefault(version => IsCompatible(project, version));
            if (compatible != null)
                return $"ObFramework {compatible} is available for this project. Run 'ob update --check' for details.";
            var migration = releases.FirstOrDefault(version => !version.IsPrerelease || project.IsPrerelease);
            return migration == null ? null :
                $"ObFramework {migration} is available and requires an explicit upgrade. Run 'ob update --check' for details.";
        }
        catch (Exception ex) when (IsUpdateError(ex)) { return null; }
    }

    internal static bool IsUpdateError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or HttpRequestException or OperationCanceledException or ArgumentException or InvalidOperationException
        or KeyNotFoundException or FormatException or JsonException or System.Xml.XmlException or System.ComponentModel.Win32Exception;
}
