using System.IO.Compression;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json.Nodes;
using ObFramework.Cli.Commands.Init;
using ObFramework.Utility.Pipeline;

namespace ObFramework.Test.Cli;

[TestFixture, NonParallelizable]
public class LubanDownloadTest
{
    private const string Version = "v5.1.0";
    private const string Marker = ".obf-luban-version";
    private const string ReleaseUrl = "https://api.github.com/repos/focus-creative-games/luban/releases/tags/" + Version;
    private const string PackageUrl = "https://github.com/focus-creative-games/luban/releases/download/" + Version + "/Luban.7z";
    private const string AgentUrl = "https://github.com/focus-creative-games/luban/releases/download/" + Version + "/Luban.Agent.7z";
    private string _root = null!;
    private string Dll => Path.Combine(_root, "Tools", "Luban", "Luban.dll");
    private string AgentDll => Path.Combine(_root, "Tools", "Luban.Agent", "Luban.Agent.dll");

    [SetUp]
    public void CreateTemporaryProject()
    {
        _root = Path.Combine(Path.GetTempPath(), "obf-luban-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void RemoveTemporaryProject()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static HttpResponseMessage Forbidden() => new(HttpStatusCode.Forbidden)
    {
        Content = new StringContent("{\"message\":\"API rate limit exceeded\"}", Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Package(string dllName)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var dll = new StreamWriter(zip.CreateEntry("release/" + dllName).Open());
            dll.Write("downloaded " + dllName);
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) };
    }

    private void WriteOldInstallation(string dll)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        File.WriteAllText(dll, "old binary");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(dll)!, Marker), "v4.0.0");
    }

    private void AssertNoTemporaryInstallations()
    {
        Assert.That(Directory.GetDirectories(_root, ".obf-luban-*", SearchOption.AllDirectories), Is.Empty);
        Assert.That(Directory.GetDirectories(_root, "*.obf-backup", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void PinnedDownloadsDoNotDependOnGitHubApiQuota()
    {
        using var handler = new ScriptedHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            ReleaseUrl => Forbidden(),
            PackageUrl => Package("Luban.dll"),
            AgentUrl => Package("Luban.Agent.dll"),
            _ => throw new AssertionException("Unexpected download URL")
        });
        using var http = new HttpClient(handler);
        using var installer = new LubanReleaseInstaller(http);
        installer.Install(Dll, AgentDll, CancellationToken.None);
        Assert.That(File.Exists(Dll), Is.True);
        Assert.That(File.Exists(AgentDll), Is.True);
        Assert.That(handler.Urls, Is.EqualTo(new[] { PackageUrl, AgentUrl }));
        AssertNoTemporaryInstallations();
    }

    [Test]
    public void DownloadsAndExtractsMainAndAgentAndSkipsSubsequentDownloads()
    {
        using var handler = new ScriptedHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            PackageUrl => Package("Luban.dll"), AgentUrl => Package("Luban.Agent.dll"),
            _ => throw new AssertionException("Unexpected download URL")
        });
        using var http = new HttpClient(handler);
        using var installer = new LubanReleaseInstaller(http);
        installer.Install(Dll, AgentDll, CancellationToken.None);
        Assert.That(File.ReadAllText(Dll), Is.EqualTo("downloaded Luban.dll"));
        Assert.That(File.ReadAllText(AgentDll), Is.EqualTo("downloaded Luban.Agent.dll"));
        foreach (var dll in new[] { Dll, AgentDll })
            Assert.That(File.ReadAllText(Path.Combine(Path.GetDirectoryName(dll)!, Marker)), Is.EqualTo(Version));
        installer.Install(Dll, AgentDll, CancellationToken.None);
        Assert.That(handler.Urls, Is.EqualTo(new[] { PackageUrl, AgentUrl }));
        AssertNoTemporaryInstallations();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ForbiddenAtMainOrAgentAssetPreservesOldInstallationAndCleansTemporaryFiles(bool agentForbidden)
    {
        WriteOldInstallation(Dll);
        WriteOldInstallation(AgentDll);
        var failedDll = agentForbidden ? AgentDll : Dll;
        using var handler = new ScriptedHandler(request => agentForbidden && request.RequestUri!.AbsoluteUri == PackageUrl
            ? Package("Luban.dll") : Forbidden());
        using var http = new HttpClient(handler);
        using var installer = new LubanReleaseInstaller(http);
        var error = Assert.Throws<HttpRequestException>(() => installer.Install(Dll, AgentDll, CancellationToken.None));
        Assert.That(error!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(File.ReadAllText(failedDll), Is.EqualTo("old binary"));
        Assert.That(File.ReadAllText(Path.Combine(Path.GetDirectoryName(failedDll)!, Marker)), Is.EqualTo("v4.0.0"));
        Assert.That(handler.Urls, Is.EqualTo(agentForbidden ? new[] { PackageUrl, AgentUrl } : new[] { PackageUrl }));
        AssertNoTemporaryInstallations();
    }

    [Test]
    public void FailedAssetCanBeRetriedWithoutLeavingAFakeCurrentInstallation()
    {
        var fail = true;
        using var handler = new ScriptedHandler(_ => fail ? Forbidden() : Package("Luban.dll"));
        using var http = new HttpClient(handler);
        using var installer = new LubanReleaseInstaller(http);
        Assert.Throws<HttpRequestException>(() => installer.Install(Dll, null, CancellationToken.None));
        Assert.That(File.Exists(Dll), Is.False);
        Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(Dll)!, Marker)), Is.False);
        fail = false;
        installer.Install(Dll, null, CancellationToken.None);
        Assert.That(File.ReadAllText(Dll), Is.EqualTo("downloaded Luban.dll"));
        Assert.That(handler.Urls, Is.EqualTo(new[] { PackageUrl, PackageUrl }));
        AssertNoTemporaryInstallations();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InitContinuesAfter403AndRecordsSkipWithoutLosingExistingSettings(bool agentForbidden)
    {
        var context = new InitContext(_root, EngineKind.Unity, false, CancellationToken.None);
        new SetupInitStep().Execute(ref context);
        var setupPath = Path.Combine(_root, "setup.json");
        var setup = JsonNode.Parse(File.ReadAllText(setupPath))!;
        setup["custom"] = "preserved";
        setup["modules"]!["installation"] = new JsonObject { ["frameworkVersion"] = "existing-version" };
        File.WriteAllText(setupPath, setup.ToJsonString());
        using var handler = new ScriptedHandler(request => agentForbidden && request.RequestUri!.AbsoluteUri == PackageUrl
            ? Package("Luban.dll") : Forbidden());
        using var http = new HttpClient(handler);
        using var warning = new StringWriter();
        var originalError = Console.Error;
        StepResult result;
        try
        {
            Console.SetError(warning);
            result = new LubanInstallStep(() => new LubanReleaseInstaller(http)).Execute(ref context);
        }
        finally { Console.SetError(originalError); }
        Assert.That(result, Is.EqualTo(StepResult.Continue));
        Assert.That(warning.ToString(), Does.Contain("403").And.Contain("continuing without Luban"));
        setup = JsonNode.Parse(File.ReadAllText(setupPath))!;
        Assert.That(setup["custom"]!.GetValue<string>(), Is.EqualTo("preserved"));
        Assert.That(setup["modules"]!["installation"]!["frameworkVersion"]!.GetValue<string>(), Is.EqualTo("existing-version"));
        Assert.That(setup["modules"]!["installation"]!["skipLubanDownload"]!.GetValue<bool>(), Is.True);
        Assert.That(File.Exists(Dll), Is.EqualTo(agentForbidden));
        Assert.That(File.Exists(AgentDll), Is.False);
        AssertNoTemporaryInstallations();
    }

    [Test]
    public void ExplicitSkipDoesNotCreateAnInstallerOrRequireSetup()
    {
        var context = new InitContext(_root, EngineKind.None, true, CancellationToken.None);
        var step = new LubanInstallStep(() => throw new AssertionException("Skipped download created an installer"));
        Assert.That(step.Execute(ref context), Is.EqualTo(StepResult.Continue));
    }

    [Test]
    public void CallerCancellationIsNotSwallowedAsANetworkWarning()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new InitContext(_root, EngineKind.Unity, false, cancellation.Token);
        new SetupInitStep().Execute(ref context);
        using var handler = new ScriptedHandler(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        using var http = new HttpClient(handler);
        var step = new LubanInstallStep(() => new LubanReleaseInstaller(http));
        Assert.Catch<OperationCanceledException>(() => step.Execute(ref context));
        var setup = JsonNode.Parse(File.ReadAllText(context.SetupFile))!;
        Assert.That(setup["modules"]!["installation"], Is.Null);
        AssertNoTemporaryInstallations();
    }

    [Test, Category("Network"), Explicit("Downloads the pinned GitHub Luban release; requires network access.")]
    public void LiveGitHubReleaseDownloadsUsableMainAndAgentAssemblies()
    {
        using var handler = new DiagnosticHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        using var installer = new LubanReleaseInstaller(http);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        installer.Install(Dll, AgentDll, cancellation.Token);
        foreach (var dll in new[] { Dll, AgentDll })
        {
            Assert.That(File.Exists(dll), Is.True, dll);
            using var file = File.OpenRead(dll);
            using var pe = new PEReader(file);
            Assert.That(pe.HasMetadata, Is.True, "Downloaded file is not a managed assembly: " + dll);
            Assert.That(File.ReadAllText(Path.Combine(Path.GetDirectoryName(dll)!, Marker)), Is.EqualTo(Version));
        }
        AssertNoTemporaryInstallations();
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.That(request.Headers.UserAgent.ToString(), Does.Contain("ObFramework-CLI"));
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return respond(request);
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    private sealed class DiagnosticHandler() : DelegatingHandler(new HttpClientHandler())
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.That(request.RequestUri!.Host, Is.Not.EqualTo("api.github.com"), "Pinned release downloads must not depend on REST API quota.");
            var stage = request.RequestUri!.Host == "api.github.com" ? "release metadata" : "release asset";
            TestContext.Progress.WriteLine($"Luban {stage}: GET {request.RequestUri.GetLeftPart(UriPartial.Path)}");
            var response = base.Send(request, cancellationToken);
            TestContext.Progress.WriteLine($"Luban {stage}: HTTP {(int)response.StatusCode} {response.StatusCode}");
            foreach (var header in new[] { "X-RateLimit-Remaining", "X-RateLimit-Reset", "X-RateLimit-Resource", "Retry-After" })
                if (response.Headers.TryGetValues(header, out var values))
                    TestContext.Progress.WriteLine(header + ": " + string.Join(", ", values));
            return response;
        }
    }
}
