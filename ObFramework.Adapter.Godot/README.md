# Godot Bootstrap

Attach an `AGameLauncher` subclass to the Bootstrap scene and implement `CreateApplication()`. The host and its frame bridge stay in the scene tree after Bootstrap unloads.

To supply a custom manager, subclass `GodotRuntimeHost` and return it from `CreateRuntimeHost()`. A detached node or a Bootstrap node can be the host. Place persistent children under it and keep the launcher outside that subtree. Override `PrepareRuntime(IArchitecture)` for engine setup that needs the initialized Architecture.

A UI plugin can call `UIRoot.Ensure(tree)` during initialization. The host releases the root it owns after plugin shutdown.

Use `GodotRuntimeHost.Find(tree)` to access the active application. For a programmatic quit, await the host's `StopAsync()` before calling `SceneTree.Quit()` so tracked work can finish. A window close request waits for the same shutdown. The tree-exit fallback starts cleanup as Godot tears down the tree.
