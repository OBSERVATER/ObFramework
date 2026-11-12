# UI 2.0 架构

## 目标

UI 业务逻辑与引擎实现解耦：

- 业务逻辑写在纯 C# 的 `AUIView` 中，不引用 Unity/Godot/UnrealSharp 类型。
- 引擎侧 `IUIWindow`（VC）只负责窗口身份（`EUILevel`）与原生对象。
- `UIManager` 只管理一对 `(VC, View)`，沿用既有的 Single/Multiple、Common-only Stack、LRU 缓存语义。
- 事件 → Command、属性 → 控件更新由 Roslyn 生成代码完成；运行期没有反射绑定、没有第二套
  Command 分发器、没有额外的 UIManager / ViewRegistry / UIChildManager。

## 模块与职责

```
Framework (ObFramework)
  Integrated/UI
    AUIView            引擎无关业务 View：生命周期 + 数据 + 通知 + 命令运行态
    IUIWindow          引擎 VC 身份：EUILevel
    IUIAdapter         引擎适配器：创建/显示/隐藏/释放 (VC, View) 对
    UIManager (internal)  Single/Multiple、Stack、LRU，驱动业务生命周期
    UIPlugin           面向业务的公开 API（OpenAsync/TryGet/Close/CloseAll/Push/Pop）
    UIViewRegistry     引擎 Adapter 拥有的 View→VC→资源 Key 显式注册表
    UICommand          AUICommand / AUIAsyncCommand 命令基类
    Attributes         ObservableProperty / RelayCommand / NotifyCanExecuteChangedFor / NotifyPropertyChangedFor

ObFramework.Generator            引擎无关 CDE 生成器（属性、嵌套 Command、CanExecute 通知）
ObFramework.Generator.Unity      Unity 生成器（BindView 工厂 + 控件订阅/命令分发代码）

ObFramework.Adapter.Unity        运行时 Unity Adapter（Prefab 加载、层级、资源句柄、UnityViewBindings）；
                                 同一工程另提供 `-p:ObfBuildEditorTools=true` 构建模式，仅用于
                                 EditorDeploy（可部署到 Unity 的 Editor 源文件）的静态编译校验

ObFramework.Editor.Core          纯 C# 的 Editor 生成核心（模板、绑定表、注册代码、诊断），可纯 .NET 测试

ObFramework.Adapter.Godot        Godot Adapter（PackedScene/Control/UIRoot）
ObFramework.Adapter.UnrealSharp  UnrealSharp Adapter（UUserWidget/TStrongObjectPtr/UIRoot）
```

## 所有权模型（关键）

| 资源 | 所有者 | 说明 |
|---|---|---|
| 引擎原生对象（GameObject/Control/UUserWidget） | 引擎 Adapter | Adapter 创建、显示、隐藏、销毁 |
| 引擎资源句柄（Prefab/PackedScene/WidgetClass 等） | 引擎 Adapter | 与实例同生命周期释放 |
| 业务 View 的**最终释放**（`OnDestroy` / `Dispose`） | 绑定层（Unity: `ViewBinding`；Godot/UE: Adapter 作为绑定所有者） | 只释放一次；`UIManager` 不直接 Dispose View |
| 业务生命周期（`OnInit/OnOpen/OnClose`） | `UIManager` | Adapter 与生成的 Unity 回调都不调用业务生命周期 |
| 原生启用/禁用（`IViewEnabled/IViewDisabled`、Unity `OnEnable/OnDisable`） | 引擎绑定层 | 与 UI Open/Close 明确区分 |
| View 的 Architecture | 绑定层 | `ViewBinding` 在创建 View 后调用 `ICanSetArchitecture.SetArchitecture`，复用现有 DI，无第二套容器 |
| View→VC→资源 Key 映射 | 引擎 Adapter 的 `UIViewRegistry` | 启动时显式注册，不扫描程序集、不预加载、不读 setup.json |

Release 路径：`UIManager` 淘汰缓存 → `adapter.Release(pair)` → 引擎销毁对象/句柄 →
绑定层 `Dispose` 业务 View（`OnDestroy` 恰好一次）。

## 公开契约

```csharp
public interface IUIAdapter
{
    Task<UIViewInstance?> CreateAsync(Type viewType, CancellationToken ct = default);
    void Open(UIViewInstance instance, IUIData? data = null);
    void Close(UIViewInstance instance);
    void Release(UIViewInstance instance);
    bool TryGetView(IUIWindow window, out AUIView view);
}

public readonly struct UIViewInstance
{
    public IUIWindow Window { get; }
    public AUIView View { get; }
    public EUILevel Level { get; }
}

public sealed class UIPlugin : IPlugin
{
    Task<T?> OpenAsync<T>(EUIOpenMode mode = EUIOpenMode.Single, IUIData? data = null, CancellationToken ct = default) where T : AUIView;
    bool TryGet<T>(out T? view) where T : AUIView;
    bool Close(AUIView? view);
    bool Close<T>() where T : AUIView;
    int CloseAll<T>() where T : AUIView;
    void Push<T>(T view) where T : AUIView;
    Task<T?> Pop<T>() where T : AUIView;
}
```

`IUIWindow` 只保留 `EUILevel Level`；旧的 `Init/Open/Close` 业务转发已删除，避免两套生命周期。

## View 能力

`AUIView` 实现 `IController`，因此保留既有的 `GetArchitecture/GetSystem/GetModel/GetPlugin/
SendCommand/SendEvent/Query` 能力（见 `ICanExtension`），也实现 `ICanSetArchitecture` 供绑定层注入
Architecture、`IViewEnabled/IViewDisabled` 供原生启用/禁用回调、`IDisposable` 供绑定层最终释放。

`AUIView` 提供两个通知通道：

- `event Action<string>? PropertyChanged`：生成属性变化时按属性名通知，供生成的控件绑定刷新。
- `event Action<string>? CommandStateChanged`：命令 Running / CanExecute 可见状态变化时通知，
  供按钮等控件刷新可交互状态。

两者都是「按名过滤」的显式订阅，运行期不做属性反射。

后台线程触发的属性/命令状态通知由绑定层通过 `UIBindingDispatch`（Framework 提供的主线程投递
seam）投递回主线程再更新原生控件；Unity 绑定运行期在启动时配置主线程与 Unity
`SynchronizationContext`。业务 View 本身不被框架全局线程安全化。

## 跨引擎边界

- `Framework` 不引用任何引擎类型；`UIPlugin` 不暴露 VC 类型。
- 资源地址（Unity 资源 Key / Godot `res://` / UE 类路径）由各自 Adapter 的注册表决定，
  框架不强制统一资源寻址，也不把 setup.json 的文件系统 Root 转成运行时资源地址。
- Godot/UnrealSharp 本阶段提供共同的业务契约与最小桥接，Editor 工具链优先 Unity；
  两端真实运行行为留待示例项目验证。
