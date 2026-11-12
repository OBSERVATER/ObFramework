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

- 目标是 Unity 可识别的 `Assets/ObFramework/Editor/**` 布局，包含 Editor-only `.cs`、`.asmdef`
  与 `.meta`，以及 Unity Roslyn Analyzer（`ObFramework.Generator.Unity.dll` + 带
  `RoslynAnalyzer` 标签的 `.dll.meta`）。部署计划对文件类型做映射（`.cs/.asmdef/.meta` → Editor
  源码，`.dll` → roslyn-analyzer）；payload 共 47 个文件（45 个 Editor 文件 + Analyzer DLL/meta），
  dry-run 已验证；Unity Editor 内的实际导入/编译结果仍属示例项目阶段验证项
  （见 [verification.md](verification.md)）；
- 部署器在 `Assets/ObFramework/obf-deploy.json` 记录自己拥有的文件（含 SHA-256）；
- 重复 `deploy` 结果稳定；升级只替换部署器拥有的文件；`remove` 只删除它拥有的文件，
  **不会**删除用户的 View/VC 脚本、Prefab、`setup.json`；
- 文件被用户修改后（哈希不符）`remove` 保留该文件并报告；
- NuGet 安装本身不执行自定义脚本，部署必须由用户显式调用 CLI 命令完成。

## 相关测试

`ObFramework.Test/Cli/*` 覆盖：模块注册、setup.json 发现/解析/保存与未知节点保留、Root 绝对路径
校验、模块路径逃逸拒绝、部署安装/升级/卸载与用户文件保留。纯 .NET 文件系统测试，不启动 Unity。
