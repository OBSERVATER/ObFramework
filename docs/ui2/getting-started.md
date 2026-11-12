# 从零开始的完整流程

本文按顺序走完 UI 2.0 的完整开发流程：配置 → 安装 Editor 工具 → 制作 Prefab → 生成可编辑
View/VC → 数据/命令绑定 → 注册 → 打开/关闭。示例使用 Unity。

> 本阶段 Editor 工具的真实 Unity 导入/菜单/序列化行为尚未在引擎内验证，流程与模板以纯 .NET
> 生成逻辑与静态编译为准；示例项目阶段会补实机验证。

## 1. 准备 setup.json

在 Unity 工程根目录（或任意上层目录）创建 `setup.json`：

```json
{
  "root": "D:/GameProjects/MyGame",
  "modules": {
    "unity": {
      "scriptOutputRoot": "Assets/ObFramework/Generated/UI",
      "uiAssetRoot": "Assets/ObFramework/UI",
      "resourceKeyFormat": "UI/{view}",
      "resourceKeyOverrides": {}
    }
  }
}
```

`root` 必须是绝对路径；模块目录相对 `root` 且不能逃逸。也可以用 CLI 创建/修改：

```text
ob config unity --root D:/GameProjects/MyGame --create
ob config unity --set scriptOutputRoot=Assets/ObFramework/Generated/UI
ob config unity --set resourceKeyFormat=UI/{view}
```

详见 [config-cli.md](config-cli.md)。

## 2. 安装 Unity Editor 工具（NuGet + CLI）

Unity Adapter 通过 NuGet 包分发。安装包后，把 Editor 工具部署进 Unity 工程：

```text
ob unity deploy D:/GameProjects/MyGame
```

它会把 Editor-only 脚本、`.asmdef/.meta` 以及 Unity Roslyn Analyzer（`ObFramework.Generator.Unity.dll`
+ 带 `RoslynAnalyzer` 标签的 `.dll.meta`）安装到 `Assets/ObFramework/Editor/**`，并记录在
`Assets/ObFramework/obf-deploy.json`。升级时重新执行 `deploy`，卸载时执行 `ob unity remove`
（不会删除你的脚本/Prefab/setup.json）。

> 部署 payload 共 47 个文件（45 个 Editor 文件 + Analyzer DLL/meta），dry-run 已验证安装/升级/卸载
> 语义；Unity Editor 内的实际导入与编译结果仍属示例项目阶段验证项（见 [verification.md](verification.md)）。

## 3. 制作 Prefab

在 `Assets/ObFramework/UI/` 下创建 UI Prefab（命名约定 `<ViewName>.prefab`，例如 `ShopView.prefab`），
根节点挂载窗口控制器。根节点的资源 Key 由 `resourceKeyFormat` 决定（默认 `UI/{view}`，其中 `{view}`
是 View 类型名，例如 `ShopView` → `UI/ShopView`）；需要特殊 Key 时在 setup.json
`resourceKeyOverrides` 中为单个 View 配置。

## 4. 生成可编辑的 View 与 VC

1. 在 Prefab 中选中需要绑定的控件（Button/Text/TMP_Text/InputField/TMP_InputField/Toggle/Slider）；
2. 菜单 **Tools/OBF/UI/Bind Selected Controls** 把控件加入 CDE 绑定表（记录控件引用与名称）；
3. 菜单 **Tools/OBF/UI/Generate View+VC**：选择 Architecture 类型、UI Level（Scene/Common/Overlay）、
   源码输出目录与名称，生成：
   - `ShopView.cs`：纯 C# 业务 View（`partial class ShopView : AUIView`），可编辑；
   - `ShopWindow.cs`：Unity 窗口控制器（`partial class ShopWindow : AUIWindow`），可编辑，
     字段上带 `[UIBindData]` / `[UIBindCommand]`；
   - 首次生成会写入 Prefab 上对应组件的序列化引用（按字段名匹配）。
4. 重复生成只补齐缺失字段/成员，**不会覆盖**你手写的业务代码与 Attribute；重命名/删除控件等
   破坏性变更会先给出预览，确认后才应用。
5. 菜单 **Tools/OBF/UI/Validate** 检查缺失的 Prefab 引用、重复 View 映射等问题。

## 5. 编写业务 View

```csharp
public partial class ShopView : AUIView
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuyCommand))]
    private int _gold;

    [ObservableProperty] private string _title = "";

    [RelayCommand(CanExecute = nameof(CanBuy))]
    private async Task BuyAsync(string itemId, CancellationToken ct)
    {
        await GetSystem<IShopSystem>().BuyAsync(itemId, ct);
    }

    private bool CanBuy(string itemId) => Gold >= 100;

    protected override void OnInit(IUIData? data) { /* 只执行一次 */ }

    protected override void OnOpen(IUIData? data)
    {
        // 每次打开/重开都会执行；用最新数据刷新状态（生成属性会通知控件）
        if (data is ShopOpenData open)
            Title = open.Title;
    }

    protected override void OnClose() { /* 受管理关闭，不处理 SetActive */ }
}
```

生成的 `ShopView.SubmitCommand` 嵌套命令、属性通知与 CanExecute 依赖见
[binding-reference.md](binding-reference.md)。

## 6. 绑定控件

生成器会在 VC 字段上生成/保留绑定声明（也可以手写）：

```csharp
[BindView(typeof(ShopView), typeof(IShopContract))]
public partial class ShopWindow : AUIWindow, IShopContract
{
    [UIBindData(nameof(ShopView.Title))]
    public Text title;

    [UIBindData(nameof(ShopView.Gold), Format = "金币: {0}")]
    public TMP_Text goldLabel;

    [UIBindData(nameof(ShopView.ItemId), Mode = UIBindMode.TwoWay)]
    public TMP_InputField itemInput;

    [UIBindCommand(nameof(ShopView.BuyCommand))]
    public Button buyButton;
}
```

- OneWay：View 状态变化 → 控件显示；打开时刷新初始值，关闭时解除订阅。
- TwoWay：控件输入 → View 状态（默认立即），View 状态变化也会回写控件，带反馈循环保护。
- 命令：生成的事件处理器直接 `new ShopView.BuyCommand(View[, value])` 后通过 Architecture 分发，
  不使用反射；按钮可交互状态随 CanExecute/Running 自动刷新。

## 7. 注册与启动

Editor 可生成静态注册代码（菜单 **Tools/OBF/UI/Generate UI Registry**，输出位置按
`scriptOutputRoot` 配置），在启动时显式调用：

```csharp
// 生成的 UI 注册文件（可重新生成；不要手改）
public static class ObfUIRegistry
{
    public static void RegisterAll(UnityUIAdapter adapter)
    {
        adapter.Register<ShopView, ShopWindow>("UI/ShopView");
    }
}

// 启动（例如 AGameLauncher / GameLauncher 的初始化阶段）
var registry = new UIViewRegistry();
var adapter = new UnityUIAdapter(resources, registry) { Root = uiRoot };
adapter.Register<ShopView, ShopWindow>("UI/ShopView");   // 或 ObfUIRegistry.RegisterAll(adapter)
var ui = new UIPlugin(adapter);
```

也可以完全手写注册，不依赖 Editor 生成。

## 8. 打开/查询/关闭

```csharp
var view = await ui.OpenAsync<ShopView>(data: new ShopOpenData("商店"));
ui.TryGet(out ShopView? current);            // 按业务 View 类型查询
ui.Close(current);                            // 进入 LRU 缓存
var reopened = await ui.OpenAsync<ShopView>();// 复用缓存实例，再次 OnOpen，不重复 OnInit
ui.Push(view);                                // Common 窗口入导航栈
var popped = await ui.Pop<ShopView>();        // 从栈重建/复用
await ui.CloseAll<ShopView>();
```

生命周期、缓存与导航语义见 [lifecycle.md](lifecycle.md)，异步/取消见
[async-commands.md](async-commands.md)。

## 9. 验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File artifacts/devbuild/Test-Obf.ps1
```
