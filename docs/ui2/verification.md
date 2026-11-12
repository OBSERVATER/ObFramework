# UI 2.0 验收与兼容性说明

本文件对应 T18「完整 UI 2.0 验收、兼容性审查与使用文档」，记录本阶段实际验证过的内容、
验证命令和明确未验证的部分。

## 构建与测试（本机最终状态）

| 项目 | 结果 |
|---|---|
| `Framework/ObFramework.sln` 全量构建 | ✅ 0 errors（仅既有 warning：离线 NuGet NU1900、Godot/Immutable 版本提示） |
| 9 个项目单独构建 | ✅ 全部 0 errors |
| `ObFramework.Adapter.Unity` 的 Editor 工具编译校验构建模式（`-p:ObfBuildEditorTools=true`） | ✅ 0 errors（产物 `ObFramework.Adapter.Unity.EditorTools.dll`，仅作静态编译校验、不随包分发） |
| 纯 .NET 测试（反射 NUnit runner） | ✅ **303 / 303 通过** |
| CLI 帮助命令 | ✅ `ob config --help`、`ob unity --help` 可用 |
| T17 部署 dry-run（真实 EditorDeploy payload + Analyzer） | ✅ 47/47 文件规划（45 个 Editor 文件 + `ObFramework.Generator.Unity.dll` + `.dll.meta`），重复部署稳定，remove 只删自有文件 |

验证命令（本机环境；正常环境直接用 `dotnet build` / `dotnet test`）：

```powershell
# 全量构建（含 Adapter.Unity 的 Editor 工具编译校验模式）
powershell -NoProfile -ExecutionPolicy Bypass -File artifacts/devbuild/Build-All-Obf.ps1
# 纯 .NET 测试（全量 / 分组）
powershell -NoProfile -ExecutionPolicy Bypass -File artifacts/devbuild/Test-Obf.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File artifacts/devbuild/Test-Obf.ps1 -Filter Cli
```

也可以直接使用标准命令：

```powershell
dotnet build Framework/ObFramework.sln
dotnet test ObFramework.Test/ObFramework.Test.csproj
# Editor 工具静态编译校验（独立的中间目录，避免覆盖运行时 Adapter 产物）
dotnet build ObFramework.Adapter.Unity/ObFramework.Adapter.Unity.csproj -p:ObfBuildEditorTools=true
```

> 本机 `dotnet test`/vstest 测试宿主不可用（跨进程句柄被环境拒绝），因此测试通过标准 NUnit
> 测试 + 反射 runner 执行；测试本身不依赖该 runner，正常机器/CI 可直接 `dotnet test`。

## 测试覆盖（按工单）

| 工单 | 覆盖测试 |
|---|---|
| T01/T02 业务 View 与生命周期 | `Core/UI/UIFixtures.cs`、`UILifecycleTest.cs`（OnInit 一次、OnOpen 每次、关闭/缓存、最终释放唯一、失败清理、Stack 重建、停机）、`UIPluginTest`（Single/Multiple/Stack/LRU 兼容行为） |
| T03/T12 Unity Adapter 与子组件 | `UI/ChildViewTest.cs`（重复 Bind、通知刷新、父级释放唯一、Rebind 不取消任务）；Adapter 由静态编译验证 |
| T04/T05 Godot / UnrealSharp | 工程静态编译（0 errors） |
| T14 显式注册 | `Core/UI/UIViewRegistryTest.cs`（注册、重复拒绝、工厂） |
| T07/T09/T10/T11 CDE 生成器 | `Generator/CDEGeneratorTests.cs` + `CDETestHarness.cs`（27）：生成属性/命令、通知、CanExecute、防重入、并发、取消、异常恢复、跨 View 隔离、OBF1xx 诊断 |
| T08 Unity 绑定生成器 | `Generator/UnityBinding*`（16）：OneWay/Format、TwoWay + 反馈保护、CanExecute、按 View 实例分发、生命周期、异步边界、手写/带参命令、OBFU1xx 诊断；双生成器同轮最终编译 |
| T13 setup.json CLI | `Cli/*`（103）：模块注册、发现、解析/保存保留未知节点、Root/路径校验、`--set/--unset`、诊断 |
| T15/T16 Editor 生成 | `Editor/*`（95）：View/VC 模板、重新生成保留、破坏性预览、诊断、setup 快照、资源 Key、注册代码、重复映射 |
| T17 部署 | `Cli/*`：安装/升级/卸载、自有文件清单、用户文件保留、payload 完整性回归 |

## 兼容性核对

- **Unity 2022.3 / C# 9**：生成器与 Editor.Core 输出 C# 9 兼容代码；Editor glue 与 Adapter.Unity
  以本机 Unity 2022.3.58.f1 的 UnityEngine/UnityEditor DLL 静态编译通过。
- **Roslyn 基线**：两个生成器（`ObFramework.Generator`、`ObFramework.Generator.Unity`）统一编译
  针对 `Microsoft.CodeAnalysis 4.3.1`（Unity 2022.3 自带 Roslyn 基线），均使用
  `IIncrementalGenerator`；测试在 `.NET 8` 下用 5.9 driver 驱动这两个 4.3.1 程序集并最终编译通过。
- **主线程投递**：`UIBindingDispatch` 由 Unity 绑定运行期在构造时配置主线程 + Unity
  `SynchronizationContext`；生成的属性/命令状态处理器在后台线程触发时先投递回主线程再更新控件
  （`ObFramework.Test/Core/UI/UIBindingDispatchTest` 覆盖投递语义）。
- **第三方目录**：`Framework/ThirdParty` 未改动。
- **既有能力保留**：Core Architecture、IController/ICan*、ACommand/AAsyncCommand、UIPlugin/UIManager、
  ResourcePlugin、ViewBinding、LRU、UIStack 均沿用/扩展，没有新增平行管理器。
- **旧接口收缩**：`IUIWindow` 只保留 `EUILevel` 身份；删除旧 `Init/Open/Close` 转发、旧窗口
  UIPlugin API、未使用的 `EUIState`；`AUIWindow` 各引擎只保留身份。旧 UIPlugin 测试已迁移到业务
  View 语义并保持行为覆盖。
- **Godot 构建适配**：`ObFramework.Adapter.Godot` 由 netstandard2.1 调整为 net8.0，以匹配本机
  GodotSharp 4.7.2；业务契约不变（详见 adapter-integration.md）。

## 代码审查后的修正（本轮）

- 生成器统一到 `IIncrementalGenerator` + Roslyn 4.3.1 基线（符合仓库 `SourceGenerator.md` 与
  Unity 2022.3 兼容要求）。
- 新增 `UIBindingDispatch` 主线程投递 seam：Unity 绑定运行期配置主线程与 SynchronizationContext，
  生成的显示/命令状态处理器自动把后台通知投递回主线程。
- Unity Adapter 创建实例时强制 clone 为非激活后再挂到激活层级根节点，确保 `Awake/OnEnable` 与
  生成的显示绑定不会在 `UIManager.InitView/OpenView` 之前触发。
- CLI 部署升级时对「已拥有但被用户修改」的过期文件改为保留 + 警告（与 remove 的 SHA-256 所有权
  规则一致），不再无条件删除。
- 文档与实现对齐：`{view}` 使用 View 类型名、Prefab 约定 `<ViewName>.prefab`、命令构造函数形态。

## 收尾阶段修正（项目合并与 Analyzer 部署）

- **Adapter 项目合并**：删除独立的 `ObFramework.Adapter.Unity.Editor` 工程，改为同一
  `ObFramework.Adapter.Unity.csproj` 的第二构建模式 `-p:ObfBuildEditorTools=true`：默认构建只产出
  运行时 Adapter 程序集（`EditorDeploy/**` 不参与编译），校验模式额外编译 EditorDeploy 根部的
  UnityEditor glue 并引用 `Editor.Core`，产物为 `ObFramework.Adapter.Unity.EditorTools.dll`，仅作静态
  编译守卫、不随包分发。仓库从 10 个项目收敛为 9 个，Editor 工具源码仍保持单一来源。
- **Analyzer 随 CLI 部署**：`ObFramework.Cli` 构建时嵌入已构建的 `ObFramework.Generator.Unity.dll`
  与带 `RoslynAnalyzer` 标签的 `.meta`（`EmbedObfUnityGeneratorAnalyzer` 目标在资源收集前加入），
  `ob unity deploy` 一次安装 Editor 源码 + asmdef/meta + Roslyn Analyzer，共 47 个文件；
  `ObFramework.Test/Cli/UnityDeploymentManifestTest` 增加回归测试锁定 payload 中的 DLL 与 meta。
- **Adapter NuGet 包**：运行时 Adapter 工程把 `EditorDeploy/**` 作为 `contentFiles` 打包，安装
  Adapter 包的项目也能检查/复用同一份 Editor 工具资源（CLI 仍读取自己嵌入的副本）。

## 明确未验证（留待示例项目阶段）

以下能力本阶段**没有**实际运行验证，文档与测试均不声称已验证：

1. Unity：Prefab 序列化/`PrefabUtility` 引用赋值、Editor 菜单发现、asmdef 导入与 Editor 编译、
   UGUI/TMP 事件时序、Addressables 句柄生命周期、IL2CPP。
2. Godot：PackedScene 真实加载/层级/`QueueFree`、`_Ready` 与 Architecture 解析时序。
3. UnrealSharp：UUserWidget 异步类加载、`ConfigureWithUnrealContext` 线程行为、
   `TStrongObjectPtr` 与 Canvas 层级。
4. 生成代码与真实引擎控件的联合编译/运行（本阶段以 stub 编译面 + 纯 .NET 执行为准；后台通知的
   主线程投递语义在框架 seam 层测试，Unity 真实 SynchronizationContext 行为待实机验证）。
5. NuGet 部署后的 Unity 工程内实际导入结果（本阶段只验证文件部署规划、幂等与所有权语义）。
6. Editor 绑定表目前以 Prefab 路径持久化组件定位信息并在生成时解析；改为 Prefab 内序列化引用
   对象属于示例项目阶段演进项。

## 已知后续项

- `ob config` / `ob unity deploy` 需要重新构建 CLI 才能反映 EditorDeploy payload 变化（构建期 glob）。
  CLI 构建会自动重新构建并嵌入 `ObFramework.Generator.Unity`，但 EditorDeploy 内容变化后仍需重建 CLI。
- Analyzer 已随 CLI payload 部署（`ObFramework.Generator.Unity.dll` + `RoslynAnalyzer` meta，47 文件 dry-run
  已验证）；Unity Editor 内的实际导入/编译结果仍属于示例项目阶段验证项（见上节第 5 条）。
- 非 Unity 引擎的 Editor 生成工具不在首阶段范围。
