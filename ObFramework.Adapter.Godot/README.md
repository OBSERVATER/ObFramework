# Godot Bootstrap

Attach an `AGameLauncher` subclass to the Bootstrap scene and implement `CreateApplication()`. The host and its frame bridge stay in the scene tree after Bootstrap unloads.

To supply a custom manager, subclass `GodotRuntimeHost` and return it from `CreateRuntimeHost()`. A detached node or a Bootstrap node can be the host. Place persistent children under it and keep the launcher outside that subtree. Override `PrepareRuntime(IArchitecture)` for engine setup that needs the initialized Architecture.

A UI plugin can call `UIRoot.Ensure(tree)` during initialization. The host releases the root it owns after plugin shutdown.

Use `GodotRuntimeHost.Find(tree)` to access the active application. For a programmatic quit, await the host's `StopAsync()` before calling `SceneTree.Quit()` so tracked work can finish. A window close request waits for the same shutdown. The tree-exit fallback starts cleanup as Godot tears down the tree.

## UI adapter (business View pair contract)

`GodotUIAdapter` implements the engine-neutral `IUIAdapter` pair contract: one Godot `Control`
window (VC) is paired with one engine-independent `ObFramework.Integrated.UI.AUIView` (business View).
`UIManager` (inside `UIPlugin`) drives the business lifecycle; the adapter only creates, shows, hides
and frees native nodes. The adapter owns an explicit `UIViewRegistry`; it never scans assemblies,
preloads resources or reads `setup.json`.

Wiring:

```csharp
// Construct the adapter where the application builds its plugins.
var ui = new GodotUIAdapter(resourcePlugin);

new UIPlugin(
    ui,
    initialize: (adapter, _) =>
    {
        var godotUi = (GodotUIAdapter)adapter;
        godotUi.Root = UIRoot.Ensure(applicationTree); // Scene/Common/Popup layers
        godotUi.Register<ShopView, ShopWindow>(
            "res://UI/ShopView.tscn",
            architecture => new ShopView(architecture));
        return Task.CompletedTask;
    });
```

Registrations can also be written through `adapter.Registry` (the same `UIViewRegistry` shape used by
the Framework and by future generated `RegisterAll` code). `GodotUIAdapter(assets, architecture)` /
`adapter.Architecture` can pin the architecture passed to View factories; when omitted, the adapter
reads it from the created window (`IUIWindow.GetArchitecture()`), which keeps the existing
`ResourcePlugin`-only construction working.

Rules for game windows:

- A window scene root must be a `Control` implementing the registered `AUIWindow` type.
- `AUIWindow` now only exposes `Level` and `GetArchitecture()`. It has no `OnInit/OnOpen/OnClose`
  forwarding: move business code to the paired `AUIView`.
- `Release` frees the node and its `PackedScene` handle, then disposes the business View exactly once.
  In this phase the adapter is the binding owner until a generated Godot ViewBinding exists.

