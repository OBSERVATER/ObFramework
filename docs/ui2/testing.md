# 纯 .NET 测试指南

UI 2.0 首阶段只验证纯 .NET 行为与 Roslyn 生成代码，不包含 Unity/Godot/UnrealSharp 运行时验证。
测试全部位于 `ObFramework.Test`。

## 现有测试接缝

| 接缝 | 位置 | 用途 |
|---|---|---|
| 模拟 `IUIAdapter` + `PluginHost` | `ObFramework.Test/Core/UI/UIFixtures.cs`、`UIPluginTest.cs` | View/VC 身份、Single/Multiple、生命周期、LRU/Stack、失败清理、停机 |
| `AUIView` + 手写 `AUICommand/AUIAsyncCommand` | `Core/UI/UICommandRuntimeTest.cs` | Running、防重入、CanExecute、取消、异常恢复 |
| `UIViewRegistry` | `Core/UI/UIViewRegistryTest.cs` | 显式注册、重复注册拒绝、工厂 |
| Roslyn `CSharpGeneratorDriver` | `Generator/*Test.cs` | 生成代码编译、诊断、运行期行为（`Assembly.Load` 后反射调用） |
| 纯文件系统 | `Cli/*Test.cs`、`Editor/*Test.cs` | setup.json 解析/保存/发现、Editor 模板与注册代码生成、CLI 部署文件行为 |

## 运行方式

```powershell
dotnet test ObFramework.Test/ObFramework.Test.csproj
```

构建/测试也可以用仓库根目录的脚本（含 Editor 工具编译校验模式）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
# 或 macOS/Linux
bash build.sh
```

> 开发环境若 `dotnet test` / vstest 测试宿主不可用（跨进程句柄被拒绝），可改用任意 NUnit runner
> 执行同一测试程序集；测试本身是标准 NUnit 测试，不依赖特定 runner。

## 编写测试的约定

1. 优先测试**公开行为/编译器诊断**，不要断言生成的空白符、私有集合实现或冗余防御分支。
2. 异步命令测试使用 `TaskCompletionSource` 控制时序，禁止 `Thread.Sleep`/时间等待。
3. 生成器测试使用 `CSharpParseOptions(LanguageVersion.CSharp9)`，至少包含：
   - 输入源码 + 代表性生成器集合一起跑 `RunGeneratorsAndUpdateCompilation`；
   - 断言 `Compilation.Emit` 成功（不是只比较字符串）；
   - 对错误声明断言具体诊断 ID 与 Error 级别。
4. 跨生成器（CDE + Unity 绑定）场景必须在同一个 Driver 中同时运行两个生成器，最终编译通过；
   生成器之间不能依赖对方本轮已生成的符号（只按源码约定校验）。
5. View/Adapter 测试用显式注册的工厂委托创建 View，不使用反射或 Activator。
6. 测试显示生命周期时，用记录调用的 `TestView` 子类断言 `init/open/close/destroy` 次数。

## 生成器测试示例骨架

```csharp
var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
    .Split(Path.PathSeparator)
    .Append(typeof(ObFramework.Integrated.UI.AUIView).Assembly.Location)
    .Distinct()
    .Select(path => MetadataReference.CreateFromFile(path));

var compilation = CSharpCompilation.Create("Generated",
    new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
    references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

CSharpGeneratorDriver.Create(new ISourceGenerator[] { new CdeGenerator(), new ViewBindingGenerator() }, parseOptions: parseOptions)
    .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

Assert.That(diagnostics, Is.Empty);
var emit = output.Emit(stream);
Assert.That(emit.Success, Is.True, string.Join(Environment.NewLine, emit.Diagnostics));
```

## 本阶段不验证的内容

Unity Prefab 序列化、Editor 菜单发现、资源导入、UGUI 事件时序、IL2CPP、Godot 与 UnrealSharp
真实运行时行为，均留待示例项目阶段验证；文档与测试报告不声称这些能力已被验证。
