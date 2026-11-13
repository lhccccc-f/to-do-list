# to-do-list

Windows 本地周计划与打印清单工具。C# + WinUI 3，中文界面，离线使用。

## 当前状态

源码已实现，**尚未编译或运行验证**。首次编译、运行和便携发布须等项目所有者明确批准。

## 功能

- 月历 + 默认较宽的日期编辑区，分隔线可拖动并记住宽度。
- 逐日编辑多行计划与自由文本预计用时；拖动序号调整顺序。
- 编辑后 500ms 自动保存，切换日期、点击完成、正常关闭时立即保存。
- SQLite 事务保存；保存失败保留编辑内容并阻止切换与关闭。
- “完成”打开独立导出页面，支持连续范围与多个不连续日期。
- 生成 Word / PDF / 两种格式；默认跳过空白日期，也可留出手写空间。
- A4 纵向、26 磅居中标题、日期与星期、打印用空心方框、右侧预计用时。
- Word 和 PDF 都嵌入随程序分发的中文字体，不需要安装 Microsoft Office。
- 同时导出时先完成全部临时文件；替换失败尝试恢复原文件。

## 目录

```text
src/ToDoList.App/       WinUI 3 界面与 Windows 集成
src/ToDoList.Core/      模型、SQLite、导出与便携路径
tests/ToDoList.Checks/  持久保存、排序、多日期、文档与失败恢复检查
docs/TESTING.md         检查说明与人工验收清单
Test-ToDoList.ps1       本机测试脚本，按要求加入 .gitignore
```

原有网页动画文件保留在本地，已按与新项目无关的原型加入忽略规则。

## 便携数据

发布目标为 Windows x64 自包含文件夹，基线系统 Windows 10 2004 / Windows 11。无需额外安装 .NET 或 Windows App SDK，仍须在兼容系统中实际验证依赖完整性。

```text
ToDoList/
  ToDoList.exe
  Assets/Fonts/         中文字体及其许可证
  Data/plans.db         计划、顺序和界面设置
  Data/plans.backup.db  打开应用时生成的上一会话完整备份
  Exports/              默认导出目录
  …                     自包含运行依赖
```

所有内部路径基于 `AppContext.BaseDirectory`，不依赖启动目录，不包含开发电脑的盘符。关闭程序后移动整个文件夹即可带走数据。应放在当前用户可写的目录，避免 Program Files、只读盘或压缩包内直接运行。文件锁阻止同一便携目录被多个窗口同时编辑。

用户在系统文件选择器中选择的外部导出位置仅对当前导出页面生效，不写入固定机器路径。数据库和导出文件不提交到 Git。异常断电或强制结束进程可能丢失尚在 500ms 等待期内的输入；正常关闭会立即保存。

## 开发环境与依赖

- .NET 10 SDK 或支持目标框架的更新 SDK。
- Visual Studio / Build Tools 的 WinUI 3 C# 与 Windows SDK 构建组件。
- 稳定版 Windows App SDK 1.8、Microsoft.Data.Sqlite 10、Open XML SDK 3、PDFsharp/MigraDoc 6、CommunityToolkit.Mvvm 8，具体版本固定于项目文件。
- 首次还原依赖需要连接 NuGet。

经批准后，可在项目根目录执行：

```powershell
dotnet build ToDoList.sln -c Debug -p:Platform=x64
dotnet publish src/ToDoList.App/ToDoList.App.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o artifacts/portable/ToDoList
```

不能只复制一个 exe；必须复制完整发布文件夹。Word 在不同编辑软件中重新排版时，分页可能与 PDF 不同。特殊字符（例如少见表情）是否可显示取决于字体覆盖范围。

## 检查

见 [docs/TESTING.md](docs/TESTING.md)。默认测试脚本只检查静态文件，不编译、不还原、不启动应用。运行核心行为检查须显式传入 `-Mode Core -AllowBuild`。

## Git

分支 `main`，远程 `origin`：<https://github.com/lhccccc-f/to-do-list>。

源代码、项目文件、测试源码、字体及许可证纳入版本控制；个人数据、导出文件、构建产物及本机 PowerShell 测试脚本忽略。

## 字体授权

霞鹜新晰黑 LXGW Neo XiHei v1.309，原样随项目分发，用于文档嵌入。来源、SHA-256 和 IPA Font License 见 `src/ToDoList.Core/Assets/Fonts/README.md` 与同目录 `LICENSE.md`。第三方库以各自 NuGet 包许可证为准。
