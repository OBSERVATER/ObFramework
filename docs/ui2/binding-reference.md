# Attribute / 数据与命令绑定参考

UI 2.0 只用 4 个引擎无关 attribute + 3 个 Unity 绑定 attribute。所有生成代码都在 `partial` 中完成，
没有运行期反射、没有轮询、没有第二套命令总线。

## 引擎无关 attribute（Framework）

| Attribute | 应用位置 | 作用 |
|---|---|---|
| `[ObservableProperty]` | 业务 View 的实例字段 | 生成同名 PascalCase 属性 + 变化通知 |
| `[RelayCommand]` | 业务 View 的实例方法 | 生成嵌套 `AUICommand`/`AUIAsyncCommand` |
| `[NotifyCanExecuteChangedFor(nameof(XCommand))]` | Observable 字段（可多个） | 属性变化时通知命令重算 CanExecute |
| `[NotifyPropertyChangedFor(nameof(DisplayX))]` | Observable 字段（可多个） | 属性变化时额外通知 View 自写的显示属性 |

### `[ObservableProperty]`

```csharp
[ObservableProperty] private int _count = 5;
```

生成（同一 partial，字段仍是唯一存储）：

```csharp
public event Action<int>? CountChanged;

public int Count
{
    get => _count;
    set
    {
        if (EqualityComparer<int>.Default.Equals(_count, value)) return;
        _count = value;
        CountChanged?.Invoke(value);
        NotifyPropertyChanged("Count");
        // [NotifyPropertyChangedFor] / [NotifyCanExecuteChangedFor] 目标依次通知
    }
}
```

- 字段名 `_count` / `count` / `m_count` 都生成 `Count`；字段初始化值保留。
- 重复赋相同值不会重复通知。
- 只允许实例、非 const、非 readonly、非指针/ref-like/void 类型的字段。
- View 必须 `partial`、顶层、非泛型、继承 `AUIView`。

### `[RelayCommand]`

```csharp
[RelayCommand(CanExecute = nameof(CanBuy), AllowConcurrentExecutions = false, CancelOnClose = true)]
private async Task BuyAsync(string itemId, CancellationToken ct) { ... }
```

生成：

```csharp
public sealed class BuyCommand : AUIAsyncCommand
{
    public BuyCommand(MyView view) : base(view, "BuyCommand") { ... }
    public BuyCommand(MyView view, string itemId) : base(view, "BuyCommand", itemId) { ... }
}
```

| 方法形态 | 生成 |
|---|---|
| `void M()` | `MCommand : AUICommand`，ctor `(View)` |
| `void M(T value)` | `MCommand : AUICommand`，ctor `(View)` / `(View, T)`，`Parameter` 为装箱值 |
| `Task M()` / `ValueTask M()` | `MCommand : AUIAsyncCommand` |
| `Task M(T value)` / `ValueTask M(T value)` | 同上，带参数 |
| `Task M(CancellationToken ct)` | `MCommand : AUIAsyncCommand`（token 透传） |
| `Task M(T value, CancellationToken ct)` | `MCommand`（值 + token） |

- 方法名以 `Async` 结尾时命令名去掉后缀：`BuyAsync` → `BuyCommand`。
- `CanExecute = nameof(Method)`：方法零参或一个与事件值匹配的参数；执行时再次检查。
- 默认防重复执行；`AllowConcurrentExecutions = true` 允许重叠。
- 默认 `CancelOnClose = true`：窗口受管理关闭时请求协作取消。
- 同步命令上的 `AllowConcurrentExecutions/CancelOnClose` 会产生警告诊断（无意义）。
- `Task<T>` / `ValueTask<T>` 不支持；结果请暴露为 Observable 状态。

## Unity 绑定 attribute（`ObFramework.Adapter.Unity.ViewBinding`）

VC 是 `[BindView(typeof(XView), typeof(IXContract))]` 标注的 partial MonoBehaviour（继承 `AUIWindow`）。
绑定声明直接写在 VC 的序列化字段上：

```csharp
[BindView(typeof(ShopView), typeof(IShopContract))]
public partial class ShopWindow : AUIWindow, IShopContract
{
    [SerializeField, UIBindData(nameof(ShopView.Title))]
    private Text title;

    [SerializeField, UIBindData(nameof(ShopView.Gold), Format = "金币: {0}")]
    private TMP_Text goldLabel;

    [SerializeField, UIBindData(nameof(ShopView.ItemId), Mode = UIBindMode.TwoWay)]
    private TMP_InputField itemInput;

    [SerializeField, UIBindCommand(nameof(ShopView.BuyCommand))]
    private Button buyButton;
}
```

### `[UIBindData(viewProperty, Mode = OneWay|TwoWay, Format = null)]`

支持的控件与行为：

| 控件 | OneWay | TwoWay | 说明 |
|---|---|---|---|
| `Text` / `TMP_Text` | ✅ `.text` | ❌ 诊断 OBFU103 | `Format` 用 `string.Format` |
| `InputField` / `TMP_InputField` | ✅ | ✅ | 字符串 ↔ 属性；数值类型用 TryParse 转换 |
| `Toggle` | ✅ | ✅ | `bool <-> isOn` |
| `Slider` | ✅ | ✅ | `float/int`，写回 int 时四舍五入 |
| `Button` | ❌ 诊断 OBFU101 | ❌ | 按钮用 `[UIBindCommand]` |

- OneWay：显示周期内订阅 `View.PropertyChanged`，打开/重新打开时刷新当前值，关闭时解除订阅。
- TwoWay：控件值变化立即写回 View 属性；View 属性变化也回写控件；生成代码使用更新守卫避免
  反馈循环。初始值来自打开时刷新+属性变化。
- View 自写显示属性：直接绑定属性名，但需要在业务代码中显式调用 `NotifyPropertyChanged(nameof(...))`
  （或在字段上用 `[NotifyPropertyChangedFor(nameof(DisplayText))]`）。
- 生成字段名任意、可以为 private；不支持静态/const 字段。

### `[UIBindCommand(commandTypeName)]`

- 参数写生成的嵌套命令类型名：`nameof(ShopView.BuyCommand)`。
- 按钮：点击后 `new ShopView.BuyCommand(View)` 经 `View.GetArchitecture().SendCommand(...)` 分发。
- 带值控件（InputField/Toggle/Slider）：事件值作为命令构造参数；
  `new ShopView.BuyCommand(View, value)`。
- 异步命令：`async void` 处理器 `await SendCommandAsync(...)`，异常在引擎边界 `Debug.LogException`
  上报，不会静默吞掉。
- 按钮 `interactable` 自动跟随 `CanExecute()` 与 Running 状态刷新。
- 手写 Command（继承 `AUICommand`/`AUIAsyncCommand`）使用同一 attribute，按「无参」或「一个事件值
  参数」的公开构造函数约定实例化，不做隐式 View 注入。
- 参数化 CanExecute 使用事件值；异步命令运行时 `interactable` 还受 Running 影响。

## 生成文件与可编辑文件

| 文件 | 归属 | 说明 |
|---|---|---|
| `*.ViewBinding.g.cs`、`*.UIBinding.g.cs`、`*.CDE.g.cs` | 编译器内存/生成 | 不落盘、不手改；重新编译即重建 |
| `XView.cs` / `XWindow.cs`（Editor 生成） | 可编辑 | 首次生成模板；重复生成只补齐缺失成员，不覆盖业务代码/attribute |
| `ObfUIRegistry.cs`（Editor 生成的静态注册） | 可重新生成 | 只包含 View→VC→资源 Key 注册，可手写替代 |

Editor 生成工具的破坏性变更（重命名/删除控件字段）会先预览再应用。

## 诊断

### CDE 生成器（OBF1xx，Error）

| ID | 含义 |
|---|---|
| OBF100 | View 不是 partial |
| OBF101 | View 未继承 AUIView |
| OBF102 | View 是嵌套类型 |
| OBF103 | View 是泛型 |
| OBF104 | 不是 class |
| OBF110/111/112 | Observable 字段为 static/const/readonly |
| OBF113 | Observable 字段类型不支持（pointer/ref-like/void） |
| OBF114 | 生成成员与现有成员同名 |
| OBF115 | 两个字段生成同名属性 |
| OBF116 | 无法从字段名推导属性名 |
| OBF120 | Notify 目标名为空 |
| OBF130–134 | RelayCommand 形态不支持（非普通方法/static/泛型/参数/返回类型） |
| OBF135 | 两个方法生成同一命令名 |
| OBF136/137 | CanExecute 方法缺失/签名不支持 |
| OBF138 | 同步命令上设置了仅异步有效的选项（Warning） |
| OBF139 | 生成命令类型与现有成员冲突 |

### Unity 绑定生成器（OBFU1xx，Error）

| ID | 含义 |
|---|---|
| OBFU101 | 控件类型不支持（或对 Button 使用 UIBindData / 对文本用 UIBindCommand） |
| OBFU102 | View 源码中找不到对应声明（Observable 属性或命令） |
| OBFU103 | 对只读显示控件使用 TwoWay |
| OBFU104 | 值类型转换不兼容 |
| OBFU105 | 重复绑定目标 |
| OBFU106 | 有绑定字段但缺少 `[BindView]` |
| OBFU107 | attribute 参数非法（空属性、命令名不以 Command 结尾、Mode 非法） |
| OBFU108 | 绑定声明非法（View 非 AUIView、字段 static/const、TwoWay 只读目标、手写命令不可用等） |

原有 `[BindView]` 工厂诊断 `OBFV001–OBFV005` 保持不变。

## 不支持/边界

- 不做运行期属性反射、不做轮询、不做通用虚拟化/无限滚动、不做自动控件扫描。
- 生成器之间只按源码约定校验，不依赖另一个生成器本轮已生成的符号；最终编译用两个生成器同跑验证。
- 真实 UGUI/TMP 事件时序、Prefab 序列化、IL2CPP 行为留待示例项目验证。
