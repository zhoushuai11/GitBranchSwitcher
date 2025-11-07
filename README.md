# GitBranchSwitcher 2.0

功能
- 选择父目录并记忆（%AppData%/GitBranchSwitcher/settings.json）
- “使用当前分支”按钮（从任一勾选仓库读当前分支→填入目标）
- “Stash 切换 / 强制切换（丢弃改动）”开关（带记忆）
- 并发切换（默认 4，可在设置里改 `MaxParallel`）
- 内嵌状态图（Assets/** 作为 EmbeddedResource）
- 分支输入框不自动选择建议，只刷新下拉建议；空文本不崩溃

编译
- 打开 `GitBranchSwitcher.csproj`（.NET 8 Windows 桌面）并运行
