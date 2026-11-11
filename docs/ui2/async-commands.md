# Command、CanExecute、异步与取消

UI 2.0 的 Command 沿用 OBF 既有 `ACommand` / `AAsyncCommand` 与 `IArchitecture.SendCommand /
SendCommandAsync` 分发，不引入第二套命令总线。生成的 Command 只是普通 C# 类，持有产生它的
View 实例，因此同类窗口互不串扰。

## 两种 Command

```csharp
public abstract class AUICommand : ACommand
{
    protected AUICommand(AUIView view, string commandName, object? parameter = null);
    public AUIView View { get; }
    public string CommandName { get; }
    public object? Parameter { get; }   // 可选的 UI 事件参数（按钮无参、输入事件一个值）
    public bool IsRunning { get; }
    public bool CanExecute();
    public bool CanExecute(object? parameter);
    public sealed override void Execute();   // 执行前再次检查 CanExecute
}

public abstract class AUIAsyncCommand : AAsyncCommand
{
    // 与同步版相同，另有：
    public bool AllowConcurrentExecutions { get; }  // 默认 false：防重复执行
    public bool CancelOnClose { get; }              // 默认 true：窗口关闭请求协作取消
    public sealed override ValueTask Execute(CancellationToken ct);
}
```

## `[RelayCommand]` 生成规则

业务方法：

```csharp
[RelayCommand(CanExecute = nameof(CanSubmit), CancelOnClose = true)]
private async Task SubmitAsync(string text, CancellationToken ct) { ... }

private bool CanSubmit(string text) => !string.IsNullOrWhiteSpace(text);
```

生成（View 的 partial 内）：

```csharp
public sealed class SubmitCommand : AUIAsyncCommand
{
    public SubmitCommand(MyView view, string text) : base(view, "SubmitCommand", text) { ... }
}
```

- 支持 `void` / `Task` / `ValueTask`；`Task`/`ValueTask` 生成 `AAsyncCommand` 派生类，其余生成
  `ACommand` 派生类。
- 方法名以 `Async` 结尾时，命令类型去掉该后缀：`SubmitAsync` → `SubmitCommand`。
- 参数：零个或一个 UI 事件参数，可再跟一个可选的 `CancellationToken`（仅异步方法）。
- 生成的引擎事件处理器直接 `new View.SubmitCommand(View[, value])` 后通过
  `View.GetArchitecture().SendCommand(...)` / `SendCommandAsync(...)` 分发；不使用反射/Activator。
- 手写 Command 也使用同一 `[UIBindCommand]` 绑定标记，只接受「无参」或「一个与事件值匹配的参数」
  的公开构造函数，不做隐式 View 注入。
- 两个业务方法映射到同一命令类型名（含 Async 后缀折叠）会产生编译诊断。

## CanExecute

- `CanExecute = nameof(Method)`：方法可以是零参或一个与 UI 事件参数匹配的参数。
- 属性依赖必须显式声明：

```csharp
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
private string _text = "";
```

生成代码会在该属性 setter 中调用 `NotifyCommandStateChanged("SubmitCommand")`；生成的控件绑定
订阅 `CommandStateChanged` 并刷新按钮 `interactable`。未声明的属性变化不做全量重算。
- 执行边界再次检查 `CanExecute`：即使绕过按钮直接 `SendCommand`，`CanExecute() == false` 的命令
  也不会执行业务方法。

## 防重复执行与 Running

- 默认（`AllowConcurrentExecutions = false`）：同一个 View 实例的同一命令在运行期间再次执行会
  立即返回，不进入业务方法。
- `AllowConcurrentExecutions = true`：显式允许重叠执行。
- Running 状态保存在 View 实例上（每个 View × 每个命令），`new SubmitCommand(view)` 重新构造
  Command 无法绕过保护。
- Running 在第一次 `await` 之前置位，`finally` 中清除。正常完成、抛异常、取消后都会恢复。
- 关闭窗口进入 LRU 不会重置 Running；重新打开后仍有防重入保护；任务结束时（即使窗口已关闭）
  状态会正确清除。

## 取消

- `CancelOnClose = true`（默认）：窗口受管理关闭时，`AUIView` 会对该命令注册的
  `CancellationTokenSource` 请求协作取消。业务方法收到 `OperationCanceledException` 即可停止。
- `CancelOnClose = false`：任务继续完成后台工作；关闭/重开不误报回滚。
- 取消是协作式的：框架不会强行终止 Task，也不会自动回滚已发生的副作用。

## 异常边界

- 生成的 Unity 事件处理器用 `GeneratedUIBindingRuntime.Send/SendAsync` 分发，并在引擎事件边界
  `try/catch` 后调用 `Debug.LogException` 报告；业务 View 不引用引擎 logger。
- 命令抛出异常时 Running 会被恢复，不会永久卡住；纯 .NET 测试验证异常传播与状态恢复。

## 可复用子组件与异步任务

- `Bind(IUIData)` 不会自动取消子组件正在执行的业务任务（语义明确：只换数据，不干预任务）。
- 需要区分数据版本的异步操作，在任务开始时保存业务标识，完成时校验：

```csharp
public void OnBind(IUIData data)
{
    var cell = (CellData)data;
    var version = cell.Version;
    _ = LoadAsync(cell.Id, version);   // 完成时校验 version 是否仍是当前值
}
```

## 相关测试

`ObFramework.Test/Core/UI/UICommandRuntimeTest.cs`（运行时语义）、
`ObFramework.Test/Generator/CDE*Test.cs`（生成代码编译与执行）、
`ObFramework.Test/Generator/UnityBinding*Test.cs`（生成控件绑定）覆盖以上契约。
