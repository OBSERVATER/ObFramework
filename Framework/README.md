# ObFramework

[Documentation](https://github.com/OBSERVATER/ObFramework.Docs/blob/main/docs/README.md) ? [Getting started](https://github.com/OBSERVATER/ObFramework.Docs/blob/main/docs/getting-started.md) ? [Project/API map](https://github.com/OBSERVATER/ObFramework.Docs/blob/main/docs/architecture.md)

Engine-independent C# application, plugin, resource and UI primitives targeting .NET Standard 2.1.
Runtime assemblies include XML API documentation. Unity-specific lifecycle and bindings are in
`ObFramework.Adapter.Unity`; the development tool is the `ObFramework.Cli` dotnet tool (`ob`).

For Unity 2022.3 or newer, install the CLI from your configured NuGet feed and run it with the
Unity Editor closed:

```powershell
dotnet tool install --global ObFramework.Cli
ob init --engine unity --root "D:/UnityProjects/MyGame"
ob doctor --root "D:/UnityProjects/MyGame" --json
```

Init installs the matching runtime/Unity packages, NuGetForUnity, Editor tools and source generators.
For local or private feeds use `--source <feed>` and optionally `--package-version <version>`.
Run `ob update --check` from an initialized Unity project to find a matched CLI and framework update.
The Unity quick-start is installed at Assets/ObFramework/README.md.

The base Unity package has no Addressables or YooAsset dependency. Implement
`IAssetAdapter<TNativeHandle>` (and `IRawFileAdapter<TNativeHandle>` for raw files), register through
`ResourcePlugin.Create(adapter)`, and supply that plugin to the UI adapter. Opt into the supplied
Addressables source integration by setting `modules.installation.resources` to `addressables` in
`setup.json` before running `ob init`. It compiles inside the
user's Unity project against their selected compatible package.

Compose your application through `PluginApplication` and a Unity `AGameLauncher`. Business Views
use partial classes with `[ObservableProperty]` and `[RelayCommand]`; native controls are bound by
ViewControllers. Keep lifecycle and native handle ownership explicit. Init does not create gameplay
scenes or resource catalogs. Validate import/compilation with `ob doctor --unity --unity-editor
<Unity executable>` and verify runtime behavior in your application's bootstrap scene.
