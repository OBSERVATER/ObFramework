# setup.json、模块化配置与 `ob config`

UI 2.0 的开发期配置集中在一个 `setup.json` 中。它只服务于编辑器/生成阶段，运行时 UI 使用
Adapter 的静态注册资源 Key，**不会**读取 setup.json。

## setup.json 结构

```json
{
  "root": "D:/GameProject",
  "modules": {
    "unity": {
      "scriptOutputRoot": "Assets/ObFramework/Generated/UI",
      "uiAssetRoot": "Assets/ObFramework/UI",
      "resourceKeyFormat": "UI/{view}",
      "resourceKeyOverrides": {
        "ShopView": "UI/Shop"
      }
    }
  }
}
```

规则：

- `root` 必须是**绝对路径**（开发机上的工程根目录），文件中按写定的绝对路径保存。
- 模块目录是相对 `root` 的路径，写入前会归一化并校验不能逃逸出 `root`
  （`SetupConfigPaths.NormalizeModulePath`）。
- `modules` 下每个键是一个模块（当前为 `unity`）。模块只能修改自己的节点；保存时会保留
  **未知/未注册**的模块节点与其它字段，避免卸载某个包时破坏配置。
- 运行时资源地址不来自这里的文件系统路径：`resourceKeyFormat` / `resourceKeyOverrides` 只用于
  Editor 生成注册代码（见 [editor.md](editor.md)），最终资源 Key 由 Adapter 注册表决定。

## 发现规则

- 默认从当前工作目录向上查找最近的 `setup.json`；
- 也可显式指定 `--file <FILE>`；
- `setup.json` 不存在时可 `--create --root <DIR>` 创建。

## 模块注册（代码侧）

```csharp
// ObFramework.Cli/Config/ConfigCliModule.cs
public static void RegisterBuiltInModules(ModuleConfigRegistry registry)
{
    registry.Register<UnitySetupConfig>("unity", "unity");   // 包自己的 POCO 配置类型
}
```

- 每个包提供自己的 POCO 配置类型与显式注册行；`ConfigCliModule` 是唯一知道模块清单的地方。
- 不扫描程序集、不修改 `CliModule`、与运行时 `ConfigPlugin` 完全分离。
- 配置 POCO 上可用 `[SetupPath]` 标记模块相对路径成员，参与 Root 约束校验。

## 命令

```text
ob config                      # 列出已注册模块与当前 setup.json
ob config unity                # 查看 unity 模块当前配置
ob config unity --root D:/GameProject --create
ob config unity --set scriptOutputRoot=Assets/Generated/UI --set uiAssetRoot=Assets/UI
ob config unity --unset resourceKeyOverrides
ob config unity --validate     # 只校验 root 与模块路径，不写文件
ob config unity --file D:/GameProject/setup.json --list
```

- 命令通过既有扩展点 `CliModule.InjectCommand<T>` 注册（`ConfigCliModule.Register(cli)`），
  `CliModule` 本体未修改。
- 既有 Diagnostics 配置（`diagnostics.json` / `DiagnosticsCommand`）保持不变，不迁移到 setup.json。

## Unity Editor 工具部署（NuGet + CLI）

Unity Adapter 继续以 **NuGet 包**分发，不使用 UPM。Editor 工具与 Roslyn 生成器所需的文件随包提供，
由 CLI 显式部署：

```text
ob unity deploy <UnityProject>     # 安装或更新 Editor 工具（幂等）
ob unity deploy <UnityProject> --dry-run
ob unity remove <UnityProject>     # 只移除部署器拥有的文件
ob unity deploy <UnityProject> --source <EditorDeploy目录>   # 从本地目录部署（开发调试）
```

部署规则：

- 目标是 Unity 可识别的布局：`Assets/ObFramework/Editor/**`（Editor-only `.cs`、`.asmdef` 与
  `.meta`）加上 `Assets/ObFramework/Analyzers/**`（两个 Roslyn Analyzer：
  `ObFramework.Generator.dll`、`ObFramework.Generator.Unity.dll` 及带 `RoslynAnalyzer` 标签的
  `.dll.meta`）。Analyzer 放在 Editor-only asmdef 之外，普通游戏程序集同样会被分析。部署计划对文件
  类型做映射（`.cs/.asmdef/.meta` → Editor 源码，`.dll` → roslyn-analyzer）；payload 共 51 个文件
  （47 个 Editor 源文件 + 4 个 Analyzer 文件），dry-run 已验证；Unity Editor 内的实际导入/编译结果
  仍属示例项目阶段验证项（见 [verification.md](verification.md)）；
- 部署器在 `Assets/ObFramework/obf-deploy.json` 记录自己拥有的文件（含 SHA-256）；
- 重复 `deploy` 结果稳定；升级只替换部署器拥有的文件；`remove` 只删除它拥有的文件，
  **不会**删除用户的 View/VC 脚本、Prefab、`setup.json`；
- 文件被用户修改后（哈希不符）`remove` 保留该文件并报告；
- NuGet 安装本身不执行自定义脚本，部署必须由用户显式调用 CLI 命令完成。

## CLI 的引擎边界：通用核心 vs Unity 专用模块

CLI 的**程序集/代码引用**只有引擎无关的 `ObFramework`（以及 build-order-only、不参与编译的
`ObFramework.Generator.Unity` 项目引用，仅用于把生成器 DLL 作为资源嵌入；CLI 输出目录不含
Adapter/Editor/Generator 程序集）。Unity 相关能力是模块化挂载的：

| 部分 | 归属 | 说明 |
|---|---|---|
| `Config/` 下的 `SetupJsonDocument`、`ModuleConfigRegistry`、`SetupJsonDiscovery`、`SetupConfigPaths`、`SetupConfigValues` 等 | **通用** | 模块化 POCO 配置的组合、发现、保存保留未知节点、Root/相对路径校验；不依赖任何引擎 |
| `Config/Modules/UnitySetupConfig.cs` | **Unity 专用** | 只是 `unity` 模块的 POCO 声明；新增引擎模块只需加一个 POCO + 一行 `registry.Register<...>` |
| `Deployment/` 的 `DeployableFileSource`、`DeploymentManifest`、`OwnedDeploymentManifest`、`DeploymentResult` | **通用机制**（清单为 Unity 布局做了默认值） | 文件源抽象、计划模型、SHA-256 所有权、幂等/保留语义；默认 target root、`Assets/` 校验与 asmdef/meta/roslyn-analyzer 类型映射是 Unity 的默认约定 |
| `Deployment/UnityEditorDeployer.cs`、`Deployment/UnityDeployCliModule.cs`、`Commands/UnityCommand.cs`、`Resources/UnityDeployPlan/*` | **Unity 专用** | Unity 工程布局（`Assets/**`）、Editor 工具 payload、`ob unity deploy|remove` 命令 |
| `Core/CliModule`（`InjectCommand<T>` 扩展点） | **通用** | 引擎模块通过它注册命令，不修改 CLI 本体、不扫描程序集 |

因此：**配置文件组合层是引擎通用的；部署层是"通用文件部署引擎 + Unity 专用实现"**。
其它引擎（如 Godot）接入时应新增自己的 `XxxEditorDeployer` + 命令 + 模块 POCO，复用
`DeployableFileSource/OwnedDeploymentManifest` 的幂等与所有权语义，而不是把 Unity 的
`Assets/` 约定硬套过去；若目标布局差异较大，可把 `DeploymentPaths` 的 Unity 校验抽成
“目标布局策略”，这是当前结构里唯一仍带 Unity 假设的通用类。

## 相关测试

`ObFramework.Test/Cli/*` 覆盖：模块注册、setup.json 发现/解析/保存与未知节点保留、Root 绝对路径
校验、模块路径逃逸拒绝、部署安装/升级/卸载与用户文件保留。纯 .NET 文件系统测试，不启动 Unity。
