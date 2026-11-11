# 跨引擎 Adapter 集成与注册

三个引擎共用同一套业务契约：`AUIView`（业务 View）、`IUIAdapter`（创建/显示/释放成对实例）、
`UIPlugin`（业务 API）、`UIViewRegistry`（显式 View→VC→资源 Key 注册）。Framework 不引用任何
引擎类型，运行时也不会读取 setup.json。

## 共同契约

```csharp
public interface IUIAdapter
{
    Task<UIViewInstance?> CreateAsync(Type viewType, CancellationToken ct = default);
    void Open(UIViewInstance instance, IUIData? data = null);
    void Close(UIViewInstance instance);
    void Release(UIViewInstance instance);
    bool TryGetView(IUIWindow window, out AUIView view);
}
```

注册：

```csharp
var registry = new UIViewRegistry();
registry.Register<ShopView, ShopWindow>("UI/Shop");           // Unity：资源 Key
registry.Register(typeof(ShopView), typeof(ShopWindow), "res://UI/Shop.tscn",
    architecture => new ShopView(architecture));               // Godot/UE：工厂委托
```

- 每个 Adapter 拥有自己的 `UIViewRegistry`；重复注册同一 View 类型直接抛
  `InvalidOperationException`，不做「后者覆盖」。
- 注册只发生在启动阶段：不扫描程序集、不预加载资源、不在运行时读 setup.json。
- Editor 生成的 `RegisterAll` 与手写注册共用同一入口。

## Unity

```csharp
var registry = new UIViewRegistry();
var adapter = new UnityUIAdapter(resources, registry)
{
    Root = uiRoot                      // UIRoot.Init() 之后赋值
};
adapter.Register<ShopView, ShopWindow>("UI/Shop");   // 或 ObfUIRegistry.RegisterAll(adapter)
var ui = new UIPlugin(adapter);        // 需要 ResourcePlugin 时可用工厂重载
```

- VC：`[BindView(typeof(ShopView), typeof(IShopContract))] partial class ShopWindow : AUIWindow`，
  由已有的 Unity ViewBinding 生成器在**非激活上下文**创建业务 View 并注入 Architecture；
  Adapter 只负责 Prefab 资源句柄、层级、显示/隐藏与销毁。
- 业务 View 的最终 `Dispose` 由 `ViewBinding` 路径负责，且恰好一次；`UIManager` 不直接 Dispose。
- `Open` 后 Adapter 调用生成的 `IGeneratedUIBinding.RefreshBindings()`，覆盖已激活 Single 窗口
  重复打开时的刷新。
- Editor 生成的静态注册代码调用 `adapter.Register<TView, TWindow>(resourceKey)`。

## Godot

```csharp
var adapter = new GodotUIAdapter(resources);
adapter.Root = UIRoot.Ensure(tree);
adapter.Register<ShopView, ShopWindow>(
    "res://UI/ShopWindow.tscn",
    architecture => new ShopView(architecture));
```

- `PackedScene` 通过 `ResourcePlugin` 加载；根节点必须是实现注册窗口类型的 `Control`。
- 保留 Scene/Common/Overlay 层级、`Show/Hide`、`MoveToFront`、`QueueFree` 语义。
- 本阶段没有 Godot 生成式 ViewBinding：注册工厂负责创建业务 View，Adapter 作为绑定所有者
  注入 Architecture 并在 `Release` 时 Dispose View 一次；业务生命周期仍由 `UIManager` 驱动。
- 未显式提供 Architecture 时，Adapter 从窗口 `IUIWindow.GetArchitecture()` 解析后传给工厂。

## UnrealSharp

```csharp
var adapter = new UnrealUIAdapter(owningController);
await adapter.InitAsync(ct);           // CreateWidget UIRoot
adapter.Register<ShopView, ShopWindow>(
    "/Game/UI/ShopWindow.ShopWindow_C",
    architecture => new ShopView(architecture));
```

- 保留 `AsyncStaticLoadClass<UUserWidget>` + `ConfigureWithUnrealContext()`、`CreateWidget`、
  `TStrongObjectPtr`、`UIRoot` Canvas 层级、`RemoveFromParent` 与句柄释放语义。
- 通过 `UIPlugin` 的 initialize/shutdown 回调调用 `InitAsync` / `Deinit`。
- 与 Godot 相同：注册工厂创建业务 View，Adapter 是绑定所有者并负责一次性 Dispose。

## UIPlugin 接线

```csharp
// 现成 Adapter 实例
var ui = new UIPlugin(adapter);

// 或由 UIPlugin 在 ResourcePlugin 就绪后创建 Adapter
var ui = new UIPlugin(resources => new UnityUIAdapter(resources, registry), initialize: ..., shutdown: ...);
```

`UIPlugin` 的公开 API 全部以业务 View 为类型（`OpenAsync<T>/TryGet/Close/CloseAll/Push/Pop`），
不暴露 VC 类型；`IUIWindow` 只保留 `EUILevel` 身份。

## 验证边界

- 本仓库内纯 .NET 验证：共同契约、View/VC 成对生命周期、LRU/Stack、失败清理、生成代码编译与执行。
- 需要示例项目验证：真实 Prefab/PackedScene/Widget 加载与层级、引擎事件时序、Addressables/
  StrongObjectPtr 生命周期、IL2CPP、Editor 菜单与资源导入。
- Godot Adapter 已从 `netstandard2.1` 调整为 `net8.0` 以匹配本机 GodotSharp 4.7.2 的静态编译；
  该改动是构建环境适配，不改变业务契约。
