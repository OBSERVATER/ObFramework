# UnrealSharp adapter

`UnrealUIAdapter` implements the engine-neutral `IUIAdapter` pair contract: one UMG widget
(`AUIWindow`) is paired with one engine-independent `ObFramework.Integrated.UI.AUIView` (business
View). `UIManager` (inside `UIPlugin`) drives the business lifecycle; the adapter only creates, adds,
hides and destroys native widgets and keeps their `TStrongObjectPtr` handles.

## Runtime requirements

- The Unreal project owns a UMG root widget at `/Game/UI/WBP_ObUIRoot` with `Scene`, `Common` and
  `Popup` canvas panels (`UIRoot` resolves them by name).
- `InitAsync` must run before the first open and `Deinit` on shutdown. Wire them through the
  `UIPlugin` callbacks, for example:

```csharp
var ui = new UnrealUIAdapter(owningPlayerController);

new UIPlugin(
    ui,
    initialize: (adapter, ct) => ((UnrealUIAdapter)adapter).InitAsync(ct),
    shutdown: adapter =>
    {
        ((UnrealUIAdapter)adapter).Deinit();
        return Task.CompletedTask;
    });
```

## Registration

The adapter owns an explicit `UIViewRegistry`; there is no assembly scanning and no `setup.json`
read at runtime. Hand-written startup code (or future generated `RegisterAll` code) registers each
business View with its widget class path and a factory delegate:

```csharp
ui.Register<ShopView, ShopWindow>(
    "/Game/UI/ShopView.ShopView_C",
    architecture => new ShopView(architecture));
```

The factory receives the architecture. `UnrealUIAdapter(controller, architecture)` /
`adapter.Architecture` can pin it; when omitted, the adapter reads it from the created widget
(`IUIWindow.GetArchitecture()`), which keeps the existing `PlayerController`-only construction
working.

## Rules for game widgets

- A registered window type must inherit `UUserWidget` and implement `IUIWindow`; the loaded widget
  class must be an instance of it.
- `AUIWindow` now only exposes `Level` and `GetArchitecture()`. It has no `OnInit/OnOpen/OnClose`
  forwarding: move business code to the paired `AUIView`.
- `Release` removes the widget from its canvas, disposes the strong object handle and then disposes
  the business View exactly once. In this phase the adapter is the binding owner until a generated
  UnrealSharp ViewBinding exists.
