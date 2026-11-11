# OBF UI 2.0 使用文档

本目录是 OBF UI 2.0（CDE：Component / Data / Event）的完整使用与集成文档。

> 验证边界：本阶段只验证纯 .NET 行为与 Roslyn 生成代码编译/执行。Unity Prefab 序列化、
> Editor 菜单与资源导入、IL2CPP、Godot 与 UnrealSharp 真实运行时行为留待示例项目阶段验证，
> 文档中会明确标注。

## 文档索引

| 文档 | 内容 |
|---|---|
| [architecture.md](architecture.md) | UI 2.0 整体架构、所有权模型与公开契约 |
| [lifecycle.md](lifecycle.md) | View 生命周期、Single/Multiple、LRU、Stack、关闭与释放 |
| [async-commands.md](async-commands.md) | Command、CanExecute、Running 防重入、取消与异常边界 |
| [getting-started.md](getting-started.md) | 从 setup.json、Editor 工具、Prefab 到 View/VC/BindData/BindCommand/Open 的完整流程 |
| [binding-reference.md](binding-reference.md) | ObservableProperty / RelayCommand / UIBindData / UIBindCommand 与生成诊断 |
| [editor.md](editor.md) | Unity Editor 工具与生成文件规则 |
| [config-cli.md](config-cli.md) | setup.json、模块化配置与 `ob config` |
| [adapter-integration.md](adapter-integration.md) | Unity / Godot / UnrealSharp Adapter 集成与注册 |
| [testing.md](testing.md) | 纯 .NET 测试指南与既有测试接缝 |
| [verification.md](verification.md) | 本阶段验收结果、兼容性核对与明确未验证项 |

## 10 分钟速览

```csharp
// 1. 业务 View：纯 C#，不引用任何引擎类型
public partial class ShopView : AUIView
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private bool _canBuy;

    [RelayCommand(CanExecute = nameof(CanBuy), CancelOnClose = true)]
    private async Task BuyAsync(CancellationToken ct)
    {
        await GetSystem<IShopSystem>().BuyAsync(ct);
    }

    private bool CanBuy() => CanBuy;

    protected override void OnOpen(IUIData? data)
    {
        // 每次打开/重新打开都会调用；OnInit 只调用一次
    }
}

// 2. 引擎 VC（Unity）：生成的 partial MonoBehaviour，持有 Prefab 上的控件引用
[BindView(typeof(ShopView), typeof(IShopViewContract))]
public partial class ShopWindow : AUIWindow, IShopViewContract
{
    [UIBindData(nameof(ShopView.Title))]
    public Text title;
    [UIBindCommand(nameof(ShopView.BuyCommand))]
    public Button buyButton;
}

// 3. 启动时注册（Editor 可生成 RegisterAll；也可以手写）
adapter.Register<ShopView, ShopWindow>("UI/ShopWindow");

// 4. 打开/查询/关闭都使用业务 View 类型
var view = await ui.OpenAsync<ShopView>(data: new ShopOpenData());
ui.TryGet(out ShopView? current);
ui.Close(current);
```

所有生成代码都是 `partial` 补充，用户手写的 View/VC 文件可编辑、可反复生成且不会被覆盖业务逻辑。
