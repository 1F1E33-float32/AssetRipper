# 开发规范

本文件是仓库的根规范，适用于所有目录。`Source/.editorconfig` 和 `.csharpierrc.json` 是格式的最终依据，`Directory.Build.props` 与 `Directory.Build.targets` 统一编译选项和产物路径；本文件与它们冲突时，以配置文件为准并同步修正本文件。

## 1. 环境与构建

- `AssetRipper.slnx`：.NET 11 解决方案；`global.json` 配置 `Microsoft.Testing.Platform` 作为测试运行器。
- 根 `Directory.Build.props` 统一设置 `TargetFramework=net11.0`、`Nullable=enable`、`LangVersion=latest`、`IsAotCompatible=true`、`CheckForOverflowUnderflow=true`、`DebugType=embedded`、`SatelliteResourceLanguages=en`、`IsPublishable=false` 及全局 `Using`；项目文件只声明差异，不重复引入全局命名空间。
- Roslyn 源生成器项目（`AssetRipper.GUI.Licensing.SourceGenerator`、`AssetRipper.GUI.Localizations.SourceGenerator`、`AssetRipper.Processing.SourceGenerator`、`AssetRipper.SourceGenerated.Extensions.SourceGenerator`、`AssetRipper.Il2CppInspector.VersionedSerialization.Generator`）目标为 `netstandard2.0`，只能使用该框架可用 API；修改 `VersionedSerialization.Generator` 诊断 ID 时同步更新其 `AnalyzerReleases.Unshipped.md`。
- IL2CPP 模拟器使用 `UnicornEngine.Unicorn`，版本在 Il2CppInspector 的 `Directory.Build.props` 统一声明；插件还原时自动下载预编译 NuGet 包至根 `packages/`，不提交包文件。
- `Source/AssetRipper.Il2CppInspector/Directory.Build.props` 覆盖 `IsAotCompatible=false`、`CheckForOverflowUnderflow=false`、`PackageLicenseExpression=AGPL-3.0-only`。
- 启用 `UseArtifactsOutput`：编译输出在 `artifacts/bin/<项目名>/<配置>[_<rid>]/`，中间与还原文件在 `artifacts/obj/<项目名>/`；仅 `AssetRipper.GUI.Free`（程序集名 `AssetRipper`）设 `IsPublishable=true`，发布至 `artifacts/publish/`（发布前会清空该目录）。可用 `-p:ArtifactsPath=<绝对路径>` 覆盖产物根目录。
- `Directory.Build.targets` 仅保留 Windows / Linux / macOS 的 `x64` / `arm64` 原生运行时资产；`dotnet clean AssetRipper.slnx` 清理所有配置与 RID 的 build / publish 产物，同时保留 `artifacts/obj/<项目名>/` 根部的 NuGet 还原文件。
- 插件打包由两个插件目录的 `Directory.Build.targets` 和 `Source/AssetRipper.GUI.Free/GamePlugins.targets` 自动完成：插件 DLL 放入 `plugins/`，公共托管依赖放在宿主目录根部（不重复复制），原生库集中放入 `runtimes/<rid>/native/`。

## 2. 目录与模块职责

| 路径 | 职责 |
| --- | --- |
| `Source/AssetRipper.IO.Files`、`Source/AssetRipper.Numerics`、`Source/AssetRipper.Yaml` | 底层 Unity 文件格式、数学类型与 YAML 读写。 |
| `Source/AssetRipper.Assets`、`Source/AssetRipper.SerializationLogic`、`Source/AssetRipper.SourceGenerated.Extensions` | 资产对象模型、Unity 序列化规则与生成类型扩展。 |
| `Source/AssetRipper.Import` | 游戏结构加载、程序集管理（含 IL2CPP 转 AsmResolver）与 `IGameImportPlugin` 契约/发现。 |
| `Source/AssetRipper.Processing`、`Source/AssetRipper.Configuration` | 导入后资产处理管线与配置。 |
| `Source/AssetRipper.Export*` | Unity 工程导出、PrimaryContent 导出及音频/模型/纹理模块。 |
| `Source/AssetRipper.GUI.Avalonia`、`Source/AssetRipper.GUI.Free` | Avalonia 桌面 UI 与宿主入口（初始化 `PluginAssemblyLoader` 后启动 GUI）。 |
| `Source/AssetRipper.GUI.Licensing*`、`Source/AssetRipper.GUI.Localizations*` | 许可证与多语言资源（源文件在 `Localizations/`）。 |
| `Source/AssetRipper.Il2CppInspector` | 精简集成的 Il2CppInspector 运行库与 `Il2CppLoader`，直接输出反射模型供 `AssetRipper.Import` 消费，不生成 DummyDll。 |
| `Source/Plugins` | 游戏归档导入插件（`AssetRipper.GamePlugins.*`）。 |
| `Source/AssetRipper.Il2CppInspector/Plugins/<GameId>` | IL2CPP 解码插件（`AssetRipper.Il2CppInspector.Plugin.*`），目录使用 `<GameId>` 命名。 |
| `Source/*.Tests`、`Source/AssetRipper.Tools.*`、`Source/AssetRipper.AssemblyDumper*`、`Source/AssetRipper.DocExtraction*` | TUnit 测试、诊断工具与类型定义生成工具。 |

## 3. 模块规范

- **分层原则**：游戏专属归档格式与解密留在 `Source/Plugins/`，IL2CPP 变体留在 `Source/AssetRipper.Il2CppInspector/Plugins/<GameId>/`；多游戏共用的 IL2CPP 插件辅助代码放入 `Source/AssetRipper.Il2CppInspector/Plugins/Shared/<family>/`；通用能力下沉到核心项目，核心项目不硬编码特定游戏的密钥、地址、魔数或布局。
- **插件**：
  - 统一设 `EnableDynamicLoading=true`、`IsAotCompatible=false`，加入 `AssetRipper.slnx` 的对应目录；`AssetRipper.GUI.Free` 通过 `../Plugins/*/*.csproj` 和 `../AssetRipper.Il2CppInspector/Plugins/*/*.csproj` 自动打包，无需改动宿主项目。
  - 游戏导入插件：项目名 `AssetRipper.GamePlugins.<Game>`，实现公开无参构造的 `IGameImportPlugin`，返回 `GameImportSession` 与延迟解码的 `GameArchive`；`Id` 必须唯一。
  - IL2CPP 解码插件：项目名 `AssetRipper.Il2CppInspector.Plugin.<GameId>`（`NAME_REGION_PLATFORM_VERSION`），从 Il2CppInspector 的 `Directory.Build.props` 继承 `CheckForOverflowUnderflow=false`、`IsAotCompatible=false` 和 AGPL 许可证，实现 `GamePlugin`；访问 Core 内部成员时在 `AssetRipper.Il2CppInspector.Core.csproj` 中添加 `InternalsVisibleTo`，不为插件把成员改为 `public`。
  - 二进制/归档解析必须校验签名、魔数、版本、长度、偏移和索引范围；异常数据抛 `InvalidDataException`，不支持的版本或变体抛 `NotSupportedException`。
- **GUI 与日志**：
  - `AssetRipper.GUI.Free/Program.cs` 必须先调用 `PluginAssemblyLoader.Initialize()` 再启动 Avalonia，以正确解析 `plugins/` 与 `runtimes/<rid>/native/`。
  - 启用 `AvaloniaUseCompiledBindingsByDefault=true`；可见文案走 `Localizations/`。
  - 库项目与插件不直接读写控制台或耦合 GUI，日志统一通过 `AssetRipper.Import.Logging.Logger` 输出。
- **测试（`Source/*.Tests`）**：
  - 使用 TUnit；只验证可观察行为与边界条件，不写重复断言、空洞非空检查、简单 getter 或仅测试框架自身行为的测试。

## 4. 代码风格

### 格式

- C# 使用 Tab 缩进（`tabWidth: 4`）、LF 换行、无 BOM UTF-8、文件末尾换行，CSharpier 行宽 150（与 `Source/.editorconfig`、`.csharpierrc.json` 一致）。
- XML / `.csproj` / `.slnx` / `.props` / `.targets` / JSON 优先使用 2 空格缩进；修改既有上游文件时沿用原文件缩进。
- 仅对本次改动的文件或项目执行 `csharpier format <路径>`，避免无故重排无关上游文件。

### 语言特性与命名

- 显式声明变量类型，不使用 `var`（`Source/.editorconfig` 中 `csharp_style_var_* = false`）。
- 使用文件作用域命名空间；`using` 置于命名空间外，删除未使用的 `using`，不写 `this.`。
- 优先使用目标类型 `new()`、集合表达式 `[]`（类型完全匹配时）、模式匹配、`is null` / `is not null`、表达式体属性/索引器/访问器。
- 仅在构造时赋值的字段标 `readonly`；无状态局部函数/匿名函数标 `static`；控制流语句优先使用花括号（修改既有文件时保持原风格）。
- 遵守分析器规则：`CA1309`（字符串比较显式指定 `StringComparison`）、`CA1806`（不忽略无副作用方法返回值）、`SYSLIB1054`（优先 `LibraryImport`）；不通过 `#pragma`、`!` 或 `NoWarn` 掩盖新增警告。
- 命名：类型、命名空间、方法、属性、事件、常量用 `PascalCase`；接口用 `I` 前缀；参数与局部变量用 `camelCase`；私有字段沿用所在项目风格，不顺带批量重命名。

### 注释

- 注释说明原因、格式依据、逆向地址或非显而易见的约束，不复述代码。
- 不在代码、注释、测试或文档中引用本仓库以外的辅助项目、私人脚本或本地样本路径。
- 保留既有版权与许可证声明（包括 `Source/AssetRipper.Il2CppInspector/` 的 AGPL-3.0 声明）。

## 5. 简洁性：不写与要删除的代码

以下写法不得新增，发现时在保证行为不变前提下清理：

- **冗余与死代码**：未使用的成员、参数、局部变量、`using`、不可达分支、注释掉的代码和已过期的 TODO。
- **无用封装**：只转发调用的薄方法/薄类、仅做一对一拷贝的中间类型、无替换需求的新接口、与基类一致的重写。
- **无意义赋值**：字段显式初始化为默认值、读取前被覆盖的赋值、只为立即 `return` 的临时变量、先建临时集合再拷贝、只读不用的解析结果（只需跳过时用 `_ = ...`）。
- **过度防御与重复**：对非空值多余的判空或 `?.`/`??`、调用双方重复校验、吞掉异常或原样重抛的 `try/catch`、多余强转/括号、与现有辅助方法重复的实现。

以下内容**必须保留**：二进制/归档/元数据的魔数、签名、版本、长度、偏移和索引边界校验；由逆向证据决定的结构顺序、偏移、密钥、查表及来源注释；上游兼容分支。

## 6. 验证

在仓库根目录执行：

```bash
csharpier check <改动路径>
dotnet build AssetRipper.slnx -c Release
dotnet test AssetRipper.slnx -c Release --no-build
# 按需验证发布与插件布局（按目标平台替换 RID）
dotnet publish AssetRipper.slnx -c Release -r win-x64 --self-contained false
```

## 7. 交付与版本控制

- 保留工作树中已有的未提交改动：不执行 `git checkout`/`restore`/`reset --hard`/`stash`/`clean` 等会丢弃改动的命令；除非明确要求，不要提交。
- 不提交 `artifacts/`、`bin/`、`obj/`、`0Bins/`、`packages/`、`TestResults/` 等生成内容。
- 提交信息使用 Conventional Commits：`type(scope): summary`（`type` 取 `feat`、`fix`、`refactor`、`perf`、`test`、`docs`、`chore`，`summary` 用英文祈使句且小写开头）。
- 格式化、重构与行为变更分开提交；交付时说明变更内容、验证结果及未完成的检查。
