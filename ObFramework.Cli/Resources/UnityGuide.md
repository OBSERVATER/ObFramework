# ObFramework for Unity

[Guides and API reference](https://github.com/OBSERVATER/ObFramework.Docs/blob/main/docs/README.md)

Run commands from the Unity project root, with the Editor closed during installation.

```powershell
ob init --engine unity
ob doctor --json
ob doctor --unity --unity-editor "D:/Unity/Editor/Unity.exe"
```

New NuGetForUnity installations use its OpenUPM scoped registry; existing package selections and other registry scopes are preserved.

The default resource backend is `custom`. No Addressables or YooAsset package is required by the
base framework. Implement `IAssetAdapter<TNativeHandle>` and, if raw-file loading is needed,
`IRawFileAdapter<TNativeHandle>`. Pass the adapter to `ResourcePlugin.Create(adapter)` and pass that
resource plugin to `UnityUIAdapter`. Implement `AGameLauncher.CreateApplication()` to compose your
application; attach your concrete launcher to a scene object. Init installs tooling; it does not
create a runnable game or modify scenes/prefabs.

For the optional Addressables source integration, select it explicitly on initial installation:

```powershell
ob init --engine unity --resources addressables
```

This installs Addressables 1.21.21 if absent, preserves compatible 1.21.x selections, and deploys
`Assets/ObFramework/Integrations/Addressables`. Unity compiles this source against the project's own
Addressables assemblies. Register `ResourcePlugin.Create(new AddressablesAssetAdapter())` from
`ObFramework.Adapter.Unity.Resource`. The base framework DLL never references Addressables.
Provider switching is explicit: remove the old integration and update `modules.installation.resources`
in setup.json before re-running init. This avoids silently deleting a customized adapter.

For local nupkg testing use `ob init --source <absolute-feed-path> --package-version <version>`.
Init resolves the complete NuGet dependency graph through the .NET SDK, merges packages.config,
and restores through a project-local, pinned NuGetForUnity CLI. Existing non-framework versions
are kept exact; conflicts fail rather than upgrading unrelated packages silently. NuGetForUnity's
Unity editor plugin performs the final import processing. Private feeds can require separately
configured credentials; the CLI never prints credential files.

`--skip-packages` deploys tools/integration sources without restoring the framework. It is useful
when NuGetForUnity has already installed packages. `--skip-download` skips Luban only. These options
do not imply that skipped dependencies are installed: doctor reports remaining problems.

Keep `.obf/` out of source control (SDK restore cache, local tool installation and validation logs).
Commit Packages/manifest.json, NuGet.config, packages.config, setup.json, and owned deployment files
according to your project's policy. setup.json contains a machine-specific absolute root: update it
explicitly after moving/cloning the project; doctor detects stale roots.

## Coding and generation rules

- Business Views hold UI state/commands; Unity ViewControllers bind native components.
- Use partial View classes with `[ObservableProperty]` and `[RelayCommand]` fields/methods.
- Both source generators and their `RoslynAnalyzer` meta files must exist under
  Assets/ObFramework/Analyzers, outside Editor-only assembly definitions.
- Keep lifecycle and release ownership explicit. Custom resource adapters own their native handles;
  release them through the framework handle contract, including failure/cancellation paths.
- Use Tools/OBF/UI to bind controls, generate View+VC and the static UI registry. Register windows
  before opening them. Init does not assign resource addresses or create a provider's content build.
- Do not edit generator DLLs or deployed Editor tools. Put business logic in your View/controller
  files and keep customized provider implementations separate from CLI-owned integration sources.
- Static doctor checks do not prove Unity compilation or runtime correctness. Use doctor --unity
  for import/compile validation; play the application's bootstrap scene to verify runtime wiring.

Support baseline: Unity 2022.3, .NET Standard 2.1, Roslyn 4.3.1. Newer Unity versions require their
own import/runtime acceptance checks. IL2CPP must be verified on your target platform.
