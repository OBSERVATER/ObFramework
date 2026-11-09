# Resource 使用与接入

在插件清单中加入 `ResourcePlugin.Create(adapter)`。业务通过 `this.GetPlugin<ResourcePlugin>()` 调用资源方法。每个 PluginHost 只有一套资源缓存；不同 Host 彼此独立。

```csharp
var assets = this.GetPlugin<ResourcePlugin>();
using var handle = await assets.LoadAssetAsync<MyAsset>("Cars/Car01", ct);
var asset = handle.Get<MyAsset>();
using var raw = await assets.LoadRawFileAsync("Config/Game.json", ct);
var json = raw.Get<string>();
```

`this.GetPlugin<T>()` 要求接收者实现 ICanGetPlugin，并归属已初始化的 Architecture。普通对象通过构造函数接收 ResourcePlugin，或显式传入 IPluginContext。完整启动示例见 Samples/Plugins。

IAssetAdapter<TNativeHandle> 实现同步 Read<T>、异步 ReadAsync<T> 和 Release；可选的 IRawFileAdapter<TNativeHandle> 提供原始文件读取。AssetLoadResult<T, TNativeHandle> 包含资源对象及可选原生句柄，HasNativeHandle 决定是否调用 Adapter.Release。加载失败前尚未转交给 Manager 的句柄，由 Adapter 自行清理。引擎 API 遵守对应主线程要求。

同一 Location、Type、Operation 共享一个 AssetEntry，每次加载返回独立业务 Handle。Dispose 减少引用计数，最后一个 Handle 释放原生句柄。同步加载遇到正在异步加载的资源会抛错，不阻塞等待；适配器也可以明确不支持同步加载。RawFile 的 string 使用 UTF-8 解码，byte[] 为共享数据，不应直接修改。

同一资源的异步调用共享底层加载。调用者取消不会取消其他调用者的加载：当前实现等待底层完成后检查调用者 ct，放弃该调用者的引用并抛出取消异常，并非立即取消等待。失败会移除条目，允许重试。

关闭时停止接收请求，通过 ObExecutionScope 请求取消并等待底层加载收尾，然后释放剩余句柄。适配器不支持取消时仍需等待其返回。业务先关闭 UI、释放自己的 Handle，再停止 Resource；PluginHost 按依赖逆序执行。ObExecutionScope 负责异步任务，不是 Handle 收集器。

ResourcePlugin 借用传入的 Adapter，不负责 Dispose 外部 Adapter；由创建它的应用在插件停止后释放。AssetManager 由 ResourcePlugin 创建和停止。
