# GitBranchSwitcher 项目索引

更新时间：2026-05-14  
仓库路径：`H:\GitFleetManagerProjectRoot\GitBranchSwitcher`  
当前分支：`main`

这份文档用于后续 agent 快速接手项目。优先读这里，不需要一上来遍历全仓库；若要改功能，再按下方“优先阅读顺序”进入具体文件。

## 项目简介

`GitBranchSwitcher` 是一个面向多 Git 仓库协作场景的 Windows 桌面工具，技术栈是 C# / WinForms / .NET 8。它的核心目标是：在一个父目录下管理多个子仓库，批量查看分支状态、并发切换分支、Fetch/Pull/Push/Stash、处理 lock 文件、做仓库瘦身，并通过内网共享 JSON 记录排行榜和藏品数据。

项目目前是扁平文件结构，没有按目录拆分业务模块。主要复杂度集中在 `MainForm.cs` 和 `GitHelper.cs`。

## 优先阅读顺序

1. `PROJECT_INDEX.md`：当前文件，先获得地图。
2. `GitBranchSwitcher.csproj`：目标框架、版本号、编译模式、发布部署逻辑。
3. `Program.cs`：WinForms 入口，启动 `MainForm`。
4. `MainForm.cs`：主窗口、UI、事件、业务编排，最大文件。
5. `GitHelper.cs`：所有底层 git 命令封装、进程取消、切线、diff、GC、扫描。
6. `GitWorkflowService.cs`：并发批量切线服务。
7. `AppSettings.cs`：本地设置、缓存、共享路径、快捷键、GC 参数。
8. 其他 Form / Service 文件按功能需要进入。

## 技术与构建

- 框架：`.NET 8`，`net8.0-windows`
- UI：WinForms
- 项目类型：`WinExe`
- Nullable / ImplicitUsings：已启用
- 图标：`AppIcon.ico`
- 当前版本：`1.4.2`
- 本地配置文件：`%AppData%\GitBranchSwitcher\settings.json`

常用命令：

```powershell
dotnet restore
dotnet build GitBranchSwitcher.csproj
dotnet publish GitBranchSwitcher.csproj -c Release
```

发布时有自定义 MSBuild Target：当 `IsPureMode=false`、`IsBossMode=false`、`IsBetaMode=false` 时，`Publish` 后会自动把版本号、更新公告和 exe 复制到：

```text
\\s4.biubiubiu.io\share
```

相关发布目录：

- `\\s4.biubiubiu.io\share\Version\version.txt`
- `\\s4.biubiubiu.io\share\Version\release_note.txt`
- `\\s4.biubiubiu.io\share\Exe\GitBranchSwitcher.exe`

## 编译模式

`GitBranchSwitcher.csproj` 支持几个条件编译模式：

- `PURE_MODE`：纯净模式，隐藏/跳过部分娱乐或非核心能力。
- `BOSS_MODE`：老板模式，隐藏排行榜、藏品、娱乐文案等。
- `BETA_MODE`：测试发布模式，发布后不走远程自动部署。

开关在 csproj 内：

```xml
<IsBetaMode>false</IsBetaMode>
<IsPureMode>false</IsPureMode>
<IsBossMode>false</IsBossMode>
```

## 结构总览

```text
.
├─ GitBranchSwitcher.csproj       # .NET 8 WinForms 项目、版本、发布部署 Target
├─ Program.cs                     # 应用入口
├─ MainForm.cs                    # 主 UI 与业务编排
├─ GitHelper.cs                   # git 命令封装、扫描、切线、GC、diff、进程取消
├─ GitWorkflowService.cs          # 批量并发切线服务
├─ GitRepo.cs                     # 仓库状态模型
├─ AppSettings.cs                 # 设置、缓存、共享路径、GC/快捷键参数
├─ ThemeManager.cs                # 深色/浅色主题、控件绘制与样式
├─ BranchFavoritesForm.cs         # 分支收藏管理弹窗
├─ CloneForm.cs                   # 新建/批量 clone 拉线弹窗
├─ CollectionForm.cs              # 藏品图鉴弹窗
├─ CollectionService.cs           # 藏品 JSON 读写
├─ LeaderboardService.cs          # 内网共享排行榜 JSON 读写
├─ UpdateService.cs               # 自动更新检查与替换 exe
├─ FloatIconForm.cs               # 悬浮图标/最小化模式
├─ GlobalKeyboardHook.cs          # 全局键盘 Hook
├─ ImageHelper.cs                 # 内嵌资源图片/图标加载
├─ NetworkShare.cs                # Windows 网络共享连接封装
├─ MemoForm.cs                    # 当前基本为空的占位类
├─ AppIcon.ico                    # 应用图标
├─ README.md                      # 旧说明文档
├─ .claude/                       # Claude 本地配置，当前未跟踪
├─ .idea/                         # Rider/IDE 配置
├─ bin/                           # 构建输出，忽略
└─ obj/                           # 构建中间产物，忽略
```

## 核心文件职责

### `MainForm.cs`

主窗口与大多数业务流程都在这里。包含：

- 父目录列表与子仓库列表 UI。
- 仓库扫描、缓存加载、刷新分支、自动 Fetch。
- 分支下拉框、远端分支拉取、收藏分支入口。
- 批量切线按钮、确认弹窗、取消切线按钮。
- 仓库详情区：变更文件、diff、stage/unstage、commit、pull、push、stash、fetch。
- 排行榜、藏品、主题设置、悬浮图标、瘦身弹窗等入口。

改 UI 或用户操作流程时大概率先看这里。

### `GitHelper.cs`

底层 git 能力集中在这里，职责包括：

- 运行 git 命令并收集 stdout/stderr。
- 注册正在运行的 git 进程，支持取消切线时 kill 进程树。
- 查当前分支、远端分支、本地/远端全部分支。
- `FetchFast`、`FetchCurrentBranch`、`PullCurrentBranch`、`Clone`。
- `SwitchAndPull`：切线核心逻辑，支持 stash、reapply stash、fast mode、timeout、lock recovery。
- 工作区文件状态、diff、stage/unstage、commit/push 等辅助操作。
- 扫描目录下 Git 仓库，跳过 `.git`、`.vs`、`.idea`、`node_modules` 等目录。
- `GarbageCollect`：仓库瘦身，封装 `git gc --prune=now` / `--aggressive`。

改 git 行为或异常处理时先看这里。

### `GitWorkflowService.cs`

批量并发切线服务。它接收仓库列表、目标分支、stash/fast/timeout 参数，用 `SemaphoreSlim` 控制并发，默认最大并发 16。每个仓库调用 `GitHelper.SwitchAndPull`，再通过 `IProgress` 回传单仓库结果和日志。

### `AppSettings.cs`

本地设置模型和 JSON 读写。关键字段：

- `ParentPaths`：用户添加的父目录。
- `RepositoryCache`：父目录到子仓库的扫描缓存。
- `CachedBranchList`：分支列表缓存。
- `FavoriteBranches`：分支收藏。
- `StashOnSwitch`、`ReapplyStashOnSwitch`、`FastMode`、`ConfirmOnSwitch`。
- `MaxParallel`：并发切线数量，当前会强制至少 16。
- `EnableGitOperationTimeout`、`GitOperationTimeoutSeconds`。
- `GcThreads`、`GcWindowMemoryMB`、`GcTimeoutHours`。
- `DarkMode`、`SelectedTheme`、`SelectedCollectionItem`。
- `LeaderboardPath`、`UpdateSourcePath`、`FrameWorkImgPath`。

注意：`Load()` 会强制覆盖共享路径到 `\\s4.biubiubiu.io\share`，即使用户旧 settings 里保存过其他路径。

### `GitRepo.cs`

仓库 UI 状态模型。包含仓库名、路径、当前分支、切线状态、Fetch 状态、同步 ahead/behind、脏工作区等字段。

### 其他文件

- `ThemeManager.cs`：控件主题、深色模式、ListView/Tab/ToolStrip 等 owner draw。
- `BranchFavoritesForm.cs`：收藏常用分支，支持备注。
- `CloneForm.cs`：批量 clone 新仓库，包含特定仓库配置和自定义 clone 逻辑。
- `CollectionService.cs` / `CollectionForm.cs`：按用户保存藏品 JSON，兼容旧 `List<string>` 格式。
- `LeaderboardService.cs`：共享 JSON 排行榜，使用文件独占锁和重试写入。
- `UpdateService.cs`：检查共享目录中的远程版本，若更高则生成临时 `.cmd` 替换当前 exe 并重启。
- `FloatIconForm.cs` / `GlobalKeyboardHook.cs`：悬浮窗和键盘 Hook。
- `ImageHelper.cs`：从内嵌资源加载图片/图标。
- `NetworkShare.cs`：调用 `mpr.dll` 连接 Windows 网络共享。

## 主要业务流程

### 启动流程

1. `Program.Main()` 调用 `Application.Run(new MainForm())`。
2. `MainForm` 加载 `AppSettings`。
3. 初始化主题、排行榜路径、藏品、父目录 UI、缓存分支列表。
4. 后台检查自动更新。
5. 根据缓存/父目录加载仓库，必要时刷新分支与自动 Fetch。

### 仓库扫描与缓存

1. 用户添加父目录。
2. `GitHelper.ScanForGitRepositories(rootPath)` 递归扫描 `.git`。
3. 扫描结果转成 `ParentRepoCache` 写入 settings。
4. 后续启动优先使用 `RepositoryCache`，手动刷新/重扫才重新扫磁盘。

### 批量切线

1. 用户选择目标分支和要操作的仓库。
2. 可选：stash 本地修改、切完 reapply stash、极速本地切换、超时限制。
3. `MainForm.SwitchAllAsync()` 进入切线流程，显示进度与取消按钮。
4. `GitWorkflowService.SwitchReposAsync()` 并发处理每个仓库。
5. 每个仓库调用 `GitHelper.SwitchAndPull()`。
6. 如果遇到 Git lock 错误，可以触发确认并自动恢复。
7. 切线完成后刷新状态、更新统计、上传排行榜、抽取/记录藏品。

### Fetch / 同步状态

- 快捷键默认 `F5`。
- `FetchCurrentBranch` 只拉当前分支，速度优先。
- `FetchFast` 是 `git fetch origin --prune --no-tags`。
- UI 支持单仓库 Fetch、所有仓库 Fetch、启动后自动 Fetch、定时自动同步。
- 同步状态通过 ahead/behind 计数展示 Pull/Push 提示。

### 仓库详情操作

选中仓库后会加载：

- 文件变更列表。
- 单文件 diff。
- stage / unstage / stage all / unstage all。
- commit / pull / push / stash / fetch。

这些入口由 `MainForm` 编排，底层大多走 `GitHelper`。

### 仓库瘦身

入口在主界面“瘦身”相关按钮。流程大致是：

1. 选择父目录/仓库范围。
2. 确认 GC 参数，例如线程数、window memory、timeout。
3. 对每个仓库执行 `GitHelper.GarbageCollect()`。
4. 记录 `SlimHistory` 和 `SlimLog`。
5. 上传节省空间到排行榜。

最近一次版本说明已更新为 `1.4.2`，release note 是“优化瘦身功能”。

### 排行榜与藏品

这部分是“无后端”的共享文件方案：

- 排行榜文件：`\\s4.biubiubiu.io\share\rank.json`
- 藏品目录：`\\s4.biubiubiu.io\share\Collect`
- 资源/图片路径：`\\s4.biubiubiu.io\share\FrameWork`

`LeaderboardService` 读写共享 JSON，写入时使用 `FileShare.None` 加随机退避重试。`CollectionService` 按 Windows 用户名读写 `{UserName}.json`。

## 已做过的主要事情

从 README、项目文件和最近提交记录看，项目已经完成过这些能力：

- 面向 Unity/多仓库工作区的一键批量分支切换。
- 自动递归扫描父目录下的 Git 仓库，并缓存扫描结果。
- 分支列表缓存、远端分支查询、分支收藏和备注。
- 并发切线，默认 16 并发。
- Fast Mode：跳过 Fetch，做本地快速切换。
- Fetch 当前分支、Fetch All、自动 Fetch、快捷键 Fetch。
- 切线前 stash，切线后 reapply stash。
- 切线取消按钮和 git 进程树终止。
- Git lock 错误检测与恢复确认。
- 深色主题/浅色主题和主题设置。
- 仓库详情操作：diff、stage、commit、pull、push、stash。
- 仓库瘦身：`git gc` / aggressive GC、历史日志、节省空间统计。
- 内网共享排行榜：切线次数、耗时、瘦身节省空间、藏品分数。
- 藏品图鉴和抽卡/收藏数据。
- 悬浮图标模式、全局键盘 Hook。
- 自动更新：从共享目录读取版本并替换本地 exe。
- 发布自动部署到共享目录。

最近 20 条提交主题集中在：

- 优化瘦身功能。
- 增加分支拉取按钮。
- 增加取消切线按钮、快捷键、自动 fetch。
- 修复/优化深色主题和仓库池选中逻辑。
- 增加 reapply stash。
- 优化性能、打印错误日志。
- 支持切线超时配置与取消超时限制。
- 自动修复 lock 文件。
- fetch all。
- 版本号更新。
- 收藏夹备注、工程区目录显示当前 git 分支。
- 修复右键菜单在拖动悬浮图标后无效。

## 当前工作区状态

截至本次分析，工作区不是干净状态：

```text
 M Folder.DotSettings.user
 M GitBranchSwitcher.csproj
?? .claude/
?? PROJECT_INDEX.md
```

其中：

- `GitBranchSwitcher.csproj` 已从 `1.4.1` 更新到 `1.4.2`，release note 从“新增拉取远端分支功能”改为“优化瘦身功能”。
- `Folder.DotSettings.user` 是 Rider/ReSharper 用户配置变更。
- `.claude/` 是本地 Claude 配置目录，当前未跟踪。
- `PROJECT_INDEX.md` 是本次新增的项目索引文档。

后续 agent 操作前应先确认这些变更是否属于用户当前意图，不要随手回滚。

## 维护注意点

- 代码结构非常集中，`MainForm.cs` 已接近 200KB。改 UI 时要特别小心事件处理和控件字段的副作用。
- `GitHelper.cs` 直接执行 git 命令，涉及进程管理和用户工作区数据；改这里要优先考虑本地修改、stash 冲突、lock 文件、取消流程。
- 共享路径被 `AppSettings.Load()` 强制覆盖，若要支持用户自定义共享路径，需要先改这个行为。
- 没看到独立测试项目；变更后建议至少执行 `dotnet build`，并手动验证核心路径。
- README 和源码注释中部分中文在当前终端输出时出现乱码；如果要维护文档或注释，建议先确认文件编码和编辑器编码。
- `Assets\**\*` 被配置为嵌入资源，但当前根目录扫描没有看到 `Assets` 目录。若图片/资源加载异常，优先检查构建产物或资源目录是否缺失。

## 推荐给后续 agent 的工作方式

1. 先读 `PROJECT_INDEX.md`、`GitBranchSwitcher.csproj`、`AppSettings.cs`，确认运行环境和共享路径。
2. 如果任务是 UI/交互，读 `MainForm.cs` 对应按钮或方法名，不要先全文件重构。
3. 如果任务是 Git 行为，读 `GitHelper.cs` 的相关 public 方法，再看 `GitWorkflowService.cs` 如何调用。
4. 如果任务是设置/缓存/排行榜/藏品，优先看 `AppSettings.cs`、`LeaderboardService.cs`、`CollectionService.cs`。
5. 修改前先跑 `git status --short`，避免覆盖用户已有变更。
6. 修改后至少跑 `dotnet build GitBranchSwitcher.csproj`；涉及发布则再检查 `Publish` Target 是否会写入共享目录。
