# ObFramework Resource — 使用与接入说明

## 1. 职责与结构

- **`ResModule`**：对外静态入口；一个应用运行环境只初始化一次。多个 Architecture 共用同一个资源缓存。
- **`AssetManager`**：实例实现；管理 `AssetKey → AssetEntry`、共享异步加载、引用计数和释放。
- **`IAssetAdapter`**：由引擎包实现普通资源加载；可选能力接口（如 `IRawFileAdapter`）由同一实例实现。
- **`IHandle` / `AHandle`**：业务持有的资源使用权；`Dispose()` 通知管理器减引用。
- **`ObScope`**：登记业务 Handle，Scope 结束后统一释放。

## 2. 对外 API（目标）

```csharp
public static class ResModule
{
    public static void Initialize(IAssetAdapter adapter);
    public static IHandle LoadAsset<T>(string location);
    public static Task<IHandle> LoadAssetAsync<T>(string location);
    public static Task<RawFileHandle> LoadRawFileAsync(string location);
}
```

`ResModule` 仅转发给内部 `AssetManager`，不存放第二套缓存；不直接暴露 `AssetEntry`、`ReleaseEntry` 或 Adapter。`ReleaseEntry(AssetEntry)` 保持 **internal**。

## 3. 启动与 Adapter 接入

由引擎包提供实现，**GameLauncher 在创建 Architecture 前初始化一次**；不通过业务 Architecture 注册 Adapter。

```csharp
// Unity 启动脚本示意；在 YooAsset/资源包初始化完成之后调用
var adapter = new UnityAssetAdapter(/* 引擎所需依赖 */);
ResModule.Initialize(adapter);

// 然后按项目原有入口创建/初始化 GameArchitecture
```

Godot 可在 Autoload Node 的 `_Ready()` 中装配；UnrealSharp 可在 GameInstance Subsystem 的初始化阶段装配。引擎启动顺序必须确保 Adapter 可用后再使用资源 API。

Adapter 契约示意：

```csharp
public interface IAssetAdapter
    : IReadAdapter<AssetLoadResult, AssetKey>,
      IAsyncReadAdapter<AssetLoadResult, AssetKey>
{
}

public interface IRawFileAdapter
{
    Task<AssetLoadResult> ReadRawFileAsync(string location);
}

// Unity 示例：同一个实例提供基础能力和可选能力
public sealed class UnityAssetAdapter : IAssetAdapter, IRawFileAdapter
{
    // Read(AssetKey)               → AssetLoadResult
    // ReadAsync(AssetKey)          → Task<AssetLoadResult>
    // ReadRawFileAsync(string)     → Task<AssetLoadResult>
}
```

`AssetLoadResult.Asset` 是加载后的资源对象，`AssetLoadResult.Handle` 是**已转换为 OBF `IHandle` 的底层持有凭证**；无需向 Manager 暴露 YooAsset/Godot/UE 原生 Handle 类型。RawFile 可返回 `byte[]`，且在不需要持续持有底层资源时 `Handle` 可为 `null`。

## 4. 使用示例

```csharp
// 普通资源：同步 / 异步
using var a = ResModule.LoadAsset<MyAsset>("Cars/Car01");
MyAsset asset = a.Get<MyAsset>();

using var b = await ResModule.LoadAssetAsync<MyAsset>("Cars/Car01");

// 原始文件：仅异步
using var raw = await ResModule.LoadRawFileAsync("Config/Game.json");
string json = raw.Get<string>();    // UTF-8
byte[] bytes = raw.Get<byte[]>();  // 共享字节数组，请勿修改

// 业务可把 IHandle 登记到 ObScope，结束时自动 Dispose
// scope.Register(b); // 按当前 ObScope 实际登记 API 替换
```

每次加载返回**独立的业务 Handle**；同一缓存键复用 `AssetEntry`。每个 Handle 只释放一次；`RefCount == 0` 时立刻移除缓存并 `Dispose()` Adapter 返回的底层 `IHandle`。这里的“释放”不等于引擎立即回收实际内存。

## 5. 加载方式扩展

普通资源、RawFile 等共享型操作复用 `LoadEntryAsync()` 和 `CompleteLoadAsync()`；通过加载委托执行不同 Adapter 方法：

```csharp
var entry = await LoadEntryAsync(key, () => _adapter.ReadAsync(key));

// RawFile（仅检测同一个 _adapter 实例是否支持能力）
if (_adapter is not IRawFileAdapter rawAdapter)
    throw new NotSupportedException("RawFile is unsupported.");
var rawEntry = await LoadEntryAsync(rawKey,
    () => rawAdapter.ReadRawFileAsync(location));
```

`AssetKey` 的身份建议包含 **Location + Type + Operation**（稳定操作标识），避免同一路径在 Asset/RawFile/SubAssets 等方式下误共享。无需按 `typeof(T)` 猜测加载方法，也无需 Attribute 或工厂。

**Scene（待设计）**：未来可由 `ResModule.LoadSceneAsync()` 统一进入底层加载；`SceneManager` 负责切换、激活和卸载流程。运行中的场景实例默认**不使用普通资源的共享 Entry**；场景选项与 `ISceneHandle` 尚未定稿。

## 6. 当前约束

- 异步 API 使用标准 `Task`；**不支持调用者取消加载**。
- 同一资源的并发异步请求共享一次加载任务；同步调用遇到尚未完成的异步加载时抛出异常，不阻塞等待。
- `AssetManager` 的字典与引用计数需要一致的同步保护；实际引擎加载/释放遵守对应引擎线程规则。
- 加载失败从缓存移除，异常传递给所有等待者；后续允许重试。
- `ResModule.Initialize()` 约定启动阶段单线程、只调用一次；不允许运行时替换仍被使用的 Adapter。
- 关闭顺序：先释放业务 `ObScope`/Handle，再关闭资源系统。静态模块的 `Shutdown` 和未完成加载时的退出协议**尚未实现**。