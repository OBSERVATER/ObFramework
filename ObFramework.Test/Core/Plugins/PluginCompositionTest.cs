using ObFramework.Core;
using ObFramework.Integrated.Config;
using ObFramework.Integrated.Diagnostics;
using ObFramework.Integrated.Resource;
using ObFramework.Integrated.Save;
using ObFramework.Integrated.UI;
using ObFramework.Test.Core.UI;
using ResourceTests = ObFramework.Test.Core.Plugins.ResourceConfigPluginTest;
using SaveTests = ObFramework.Test.Core.Plugins.SaveDiagnosticsPluginTest;

namespace ObFramework.Test.Core.Plugins;

[TestFixture]
public class PluginCompositionTest
{
    private sealed class Application : PluginApplication
    {
        private readonly string _directory;
        public readonly ResourceTests.AssetAdapter Assets = new();
        public readonly SaveTests.Store Store = new();
        public readonly SaveTests.Sink Sink = new();
        public TestUIAdapter UI = null!;
        public bool ResourcesAvailableDuringUIStop;
        public Application(string directory)
        {
            _directory = directory;
            Assets.Files["rows"] = "[{\"Id\":7,\"Name\":\"seven\"}]";
        }
        protected override IPlugin[] CreatePlugins() => new IPlugin[]
        {
            new ConfigPlugin(new ResourceTests.Catalog()),
            new UIPlugin(assets => UI = new TestUIAdapter(assets).Map<FirstView>(), shutdown: async _ =>
            {
                using var handle = await UI.Assets!.LoadRawFileAsync("rows");
                ResourcesAvailableDuringUIStop = handle.Get<string>()!.Contains("seven");
            }),
            new SavePlugin(Store),
            new DiagnosticsPlugin(_directory, Sink),
            ResourcePlugin.Create(Assets)
        };
        protected override IArchitecture CreateArchitecture(IPluginContext plugins)
        {
            SaveTests.Architecture.InitInterface(plugins);
            return SaveTests.Architecture.Interface;
        }
        protected override Task EnterGameAsync(CancellationToken ct)
        {
            Architecture.GetPlugin<SavePlugin>().Bind(Architecture);
            return Task.CompletedTask;
        }
        protected override Task StopGameAsync() => Architecture.GetPlugin<SavePlugin>().UnbindAsync();
        protected override void ReleaseAdapters()
        {
            Assets.Dispose();
            Store.Dispose();
            Sink.Dispose();
        }
    }

    [Test]
    public async Task AllPluginsRunTogetherAndReleaseTheirOwnedResources()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var application = new Application(directory);
        try
        {
            await application.StartAsync();
            var architecture = application.Architecture;
            Assert.That(architecture.GetPlugin<ConfigPlugin>().Get<ResourceTests.Row>(7)!.Name, Is.EqualTo("seven"));
            var model = architecture.GetModel<SaveTests.IPlayerModel>();
            var save = architecture.GetPlugin<SavePlugin>();
            model.Value = 42;
            await save.SaveAsync("slot");
            model.Value = 0;
            await save.LoadAsync("slot");
            Assert.That(model.Value, Is.EqualTo(42));
            var ui = architecture.GetPlugin<UIPlugin>();
            var view = await ui.OpenAsync<FirstView>();
            ui.Close(view!);
            architecture.GetPlugin<DiagnosticsPlugin>().GetLogger("Composition").Warning("composition-complete");
            await application.StopAsync();
            Assert.That(application.ResourcesAvailableDuringUIStop, Is.True);
            Assert.That(application.UI.ReleasedCount, Is.EqualTo(1));
            Assert.That(application.UI.Disposals, Is.EqualTo(1));
            Assert.That(application.Assets.Released.Count, Is.EqualTo(application.Assets.Reads));
            Assert.That(application.Store.Disposals, Is.EqualTo(1));
            Assert.That(application.Sink.Disposals, Is.EqualTo(1));
            Assert.That(File.ReadAllText(Directory.GetFiles(directory).Single()), Does.Contain("composition-complete"));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await save.SaveAsync("closed"));
        }
        finally
        {
            await application.StopAsync();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
