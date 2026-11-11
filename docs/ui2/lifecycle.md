# 生命周期、Single/Multiple、LRU 与 Stack

## View 生命周期

每个业务 View 实例的生命周期由 `UIManager` 驱动：

| 回调 | 调用时机 | 次数 |
|---|---|---|
| `OnInit(IUIData?)` | 实例创建后、首次显示前（从 LRU 复用的实例不再调用） | 每个 View 实例恰好一次 |
| `OnOpen(IUIData?)` | 每次受管理的打开：首次打开、Single 重复打开、LRU 重开、Stack Pop 重开 | 每次打开一次，总是使用最新数据 |
| `OnClose()` | 每次受管理的关闭（`Close/CloseAll/Push/Pop` 隐式关闭、关闭全部、插件停止） | 每次关闭一次 |
| `Bind(IUIData)` | 可复用子组件被父组件重新绑定数据时由业务代码显式调用 | 任意次 |
| `OnDestroy()` | 绑定层最终释放 View 时（缓存淘汰 / 停止插件 / 场景销毁） | 恰好一次 |

规则：

- `OnInit` 只执行一次；`OnOpen` 每次打开都执行，包括 Single 模式下重复打开同一个窗口。
- `OnClose` 只对应「受管理的关闭」，直接对引擎对象做 `SetActive`/`Show`/`Hide` 不会改变
  `UIManager` 的栈/缓存状态，也不会触发 `OnClose`。
- `OnDestroy` 只由绑定层的最终 Dispose 触发；`UIManager` 在关闭窗口时不会 Dispose View。
- 原生启用/禁用（Unity `OnEnable/OnDisable` → `IViewEnabled.OnViewEnabled` /
  `IViewDisabled.OnViewDisabled`）与 UI Open/Close 是两套概念：显示周期订阅挂在原生启用/禁用上，
  缓存中的窗口可以保持「已初始化但未启用」的状态。
- `Bind(IUIData)` 不会重复 `OnInit`，也不会取消子组件正在执行的业务任务。需要区分数据版本的
  异步操作，应在业务代码中保存任务开始时的数据标识并在完成时校验（文档见 async-commands.md）。

## Single / Multiple

- `Single`（默认）：同类型已有活动实例时，直接复用该实例：调用 `OnOpen(新数据)` 并刷新显示，
  不会创建第二个实例，也不会重新 `OnInit`。
- `Multiple`：每次打开创建/复用独立实例；同类型的多个实例互不影响（命令 Running 状态按 View
  实例隔离）。

## LRU 缓存

- 关闭的窗口（VC + View 成对）进入 LRU 缓存，容量由 `new UIPlugin(adapter, cacheCapacity)` 指定。
- 缓存保留的是**成对**的引擎 VC 与业务 View，因此重新打开时业务身份不变，`OnInit` 不重复执行。
- 缓存溢出时最久未使用的窗口被 `adapter.Release(pair)`；绑定层随后 Dispose View，触发一次
  `OnDestroy`。
- 缓存中的窗口可以保留未完成的异步命令 Running 状态：关闭窗口不会重置 Running，重新打开后
  默认的防重入仍然有效。

## Stack（Common-only 导航栈）

- 只有 `EUILevel.Common` 的窗口允许 `Push`；`Scene`/`Overlay` 会被拒绝（`InvalidOperationException`）。
- `Push(view)` 等价于「关闭当前窗口并记录 type/mode/data」：窗口进入缓存，栈中存放的是
  `ViewType + Mode + Data` 而不是实例。
- `Pop<TView>()` 使用栈顶记录的 Type/Mode/Data 重新打开：优先复用缓存实例；若缓存已淘汰，
  会重新创建窗口，因此 Pop 可以在淘汰后重建。
- 栈顶类型与 `Pop<TView>` 不匹配时抛出 `InvalidOperationException`。

## 关闭、失败与停机

- 打开流程：在非激活上下文中创建（Adapter 不显示）→ `OnInit`（仅首次）→ `OnOpen` → Adapter 显示
  → 生成的绑定在原生启用时订阅并刷新。
- 打开失败（创建失败、`OnOpen` 抛错、Adapter 显示抛错）：本次创建的 `(VC, View)` 立即释放，
  不进入活动表和缓存；下一次打开会重新创建。
- 关闭失败（`OnClose` 或 Adapter 关闭抛错）：释放该实例并向上抛出，不会留下可复用缓存条目。
- 插件停机：取消停机令牌 → 等待进行中的打开 → 关闭并释放所有活动窗口 → 清空缓存（逐个 Release）
  → 调用 Adapter 的 shutdown 回调。多个释放错误会聚合为 `AggregateException`，但所有资源仍会被
  尝试释放。停机后继续调用 UI API 会抛出 `ObjectDisposedException`。
- 打开过程中发生取消（调用方 token 或停机）时，Adapter 负责清理未完成的原生对象；已返回的
  结果若已不可用会被释放，不会进入缓存。

## 相关测试

`ObFramework.Test/Core/UI/UILifecycleTest.cs` 覆盖：OnInit 一次、OnOpen 每次、关闭/缓存复用、
最终释放唯一性、部分创建失败清理、Stack 重建、停机释放。配合 `UIPluginTest` 的行为回归。
