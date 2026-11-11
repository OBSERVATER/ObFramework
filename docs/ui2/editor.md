# Unity Editor 工具与生成文件规则

UI 2.0 的 Editor 工具通过 NuGet + CLI 部署到 Unity 工程（见 [config-cli.md](config-cli.md)），
由纯 .NET 生成核心（`ObFramework.Editor.Core`）与一层薄 UnityEditor glue 组成。生成核心可在
仓库内纯 .NET 测试，UnityEditor glue 只做 Prefab/Asset 交互与菜单入口。

> 验证边界：本阶段验证模板/绑定表/注册代码生成与诊断的纯逻辑，以及 UnityEditor glue 对本机
> Unity 2022.3.58.f1 DLL 的静态编译；真实 Prefab 序列化、菜单发现、asmdef 导入与资源导入
> 留待示例项目验证。

## 菜单

| 菜单 | 作用 |
|---|---|
| **Tools/OBF/UI/Bind Selected Controls** | 把当前选中的控件加入 CDE 绑定表（记录组件引用、控件类型、默认字段名/目标成员） |
| **Tools/OBF/UI/Binding Table** | 打开绑定表窗口：增删行、修改字段名/目标属性/命令、OneWay/TwoWay、Format、View/Window 名称、Namespace、Architecture、Level、输出目录 |
| **Tools/OBF/UI/Generate View+VC** | 首次生成可编辑 View/VC/Contract 模板并写入 Prefab 序列化引用；再次生成只补齐缺失声明 |
| **Tools/OBF/UI/Generate UI Registry** | 从 setup.json + 已编译的 `[BindView]` 类型生成静态 View→VC→资源 Key 注册代码 |
| **Tools/OBF/UI/Validate** | 校验缺失的 Prefab 引用、重复 View 映射、无效 setup 路径等 |

上下文菜单/组件菜单也提供「Add to OBF Binding Table」。

## 生成文件

| 文件 | 可否手改 | 规则 |
|---|---|---|
| `<Name>View.cs` | ✅ 可编辑 | 纯 C# 业务 View；重新生成只追加缺失的 `[ObservableProperty]` 字段 / `[RelayCommand]` 方法 / 生命周期 hook，不覆盖已有成员 |
| `<Name>Window.cs` | ✅ 可编辑 | Unity VC；重新生成只追加缺失的 `[SerializeField]` + `[UIBindData]/[UIBindCommand]` 字段；重命名/删除等破坏性变更先预览、确认后才应用 |
| `<IName>Contract.cs` | ✅ 可编辑 | VC 与 View 之间的契约接口（可按需扩展） |
| `GeneratedUIRegistry.g.cs` | 可重新生成 | 静态注册入口；默认输出到 `<scriptOutputRoot>/`，也可手写注册替代 |
| Roslyn 生成的 `*.CDE.g.cs` / `*.ViewBinding.g.cs` / `*.UIBinding.g.cs` | ❌ 不落盘 | 编译器内存中生成，不进入工程、不手改 |

### 模板样式

```csharp
// <Name>View.cs（可编辑）
public partial class ShopView : global::ObFramework.Integrated.UI.AUIView
{
    [global::ObFramework.Integrated.UI.ObservableProperty]
    private string _title = string.Empty;

    [global::ObFramework.Integrated.UI.RelayCommand]
    private void Submit()
    {
    }
}
```

```csharp
// <Name>Window.cs（可编辑）
[global::ObFramework.Adapter.Unity.ViewBinding.BindView(typeof(ShopView), typeof(IShopContract))]
public partial class ShopWindow : global::ObFramework.Adapter.Unity.UI.AUIWindow, IShopContract
{
    public override global::ObFramework.Core.IArchitecture GetArchitecture()
        => global::Game.GameArchitecture.Interface;

    public override global::ObFramework.Integrated.UI.EUILevel Level
        => global::ObFramework.Integrated.UI.EUILevel.Common;

    [global::UnityEngine.SerializeField]
    [global::ObFramework.Adapter.Unity.ViewBinding.UIBindData(nameof(ShopView.Title))]
    private global::UnityEngine.UI.Text titleText;

    [global::UnityEngine.SerializeField]
    [global::ObFramework.Adapter.Unity.ViewBinding.UIBindCommand(nameof(ShopView.SubmitCommand))]
    private global::UnityEngine.UI.Button submitButton;
}
```

- 生成时选择 **Architecture**（写入 `GetArchitecture()`）与 **EUILevel**（Scene/Common/Overlay）。
- 控件类型决定 `[UIBindData]` / `[UIBindCommand]` 与是否允许 TwoWay；不支持的控件会在绑定表/生成
  诊断中报错（OBFE 系列），不会生成无效代码。
- 重新生成时已存在的文件先与内存模板合并（`SourceTextMerger`）：保留用户方法、Attribute 与命名；
  破坏性变更返回 `DestructiveChanges`，需要用户确认 id 后才应用。
- 输出目录独立于 Prefab 路径：由 setup.json 的 `scriptOutputRoot` 或绑定表窗口指定。

### 静态注册代码（T16）

```csharp
// GeneratedUIRegistry.g.cs（可重新生成）
public static class GeneratedUIRegistry
{
    public static void RegisterAll(global::ObFramework.Adapter.Unity.UI.UnityUIAdapter adapter)
    {
        adapter.Register<global::Game.UI.ShopView, global::Game.UI.ShopWindow>("UI/Shop");
    }
}
```

- 资源 Key 规则：单 View 覆盖 > `resourceKeyOverrides` > `resourceKeyFormat`（默认 `UI/{view}`；
  `{view}` 替换为 **View 类型名**，例如 `ShopView` → `UI/ShopView`，不自动去掉 `View` 后缀）。
  以文件系统 Root 路径作为资源地址的行为被明确禁止。
- Prefab 约定：注册校验按 `<uiAssetRoot>/<ViewName>.prefab` 查找 Prefab（例如
  `Assets/ObFramework/UI/ShopView.prefab`），缺失时给出 warning（不阻断纯生成）。
- 绑定表当前按 Prefab 路径持久化组件定位信息（名称/类型/路径），在生成时解析为实际组件引用并写入
  序列化字段；不采用运行时反射表。真实 Unity 下把绑定表改为 Prefab 内序列化对象（ScriptableObject/
  Component）属于示例项目阶段验证/演进项。
- 生成时选择 Architecture、Level、输出目录；单 View 的资源 Key 覆盖可在 setup.json
  `resourceKeyOverrides` 中按 View 名配置（T16 注册代码生成时生效）。
- 重复 View 映射报错；重复资源 Key 给出警告。
- 生成前需保证 View/VC 脚本已编译（注册扫描基于 `TypeCache` 的 `[BindView]`）。
- 输出命名空间/类名/方法名/Adapter 类型/输出路径均可配置。

## Editor 诊断

`ObFramework.Editor.Core` 返回结构化 `EditorDiagnostic`（Severity/Code/Message/Target），覆盖：
无效标识符、命名空间/Level、重复字段、控件类型不支持、缺失组件/Prefab 引用、setup 路径逃逸、
无效资源 Key 规则、重复 View 映射等。UnityEditor glue 以对话框/控制台输出呈现并阻止破坏性写入。

## 与生成器/运行时的关系

Editor 只负责「可编辑模板 + 绑定表 + 静态注册」，不参与 Roslyn 编译管线：
没有 Editor→Roslyn 的 AdditionalFile 中转，也没有把 Prefab 数据塞进编译器的流程。
Roslyn 生成器（CDE + Unity 绑定）只读取 C# Attribute 约定（见
[binding-reference.md](binding-reference.md)）。
