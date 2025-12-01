using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace GitBranchSwitcher {
    public static class GitHelper {
        
        public class FileChangeItem
        {
            public string X { get; set; } // 索引状态 (Index/Staged)
            public string Y { get; set; } // 工作区状态 (WorkTree/Unstaged)
            public string FilePath { get; set; }
            // 原始状态字符串 (例如 "MM", "??", "M ")
            public string RawStatus { get; set; } 
        }
        // ==================== 1. 基础查询与操作 ====================

        public static string? FindGitRoot(string startPath) {
            var dir = new DirectoryInfo(startPath);
            while (dir != null) {
                var gitDir = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(gitDir) || File.Exists(gitDir))
                    return dir.FullName;
                dir = dir.Parent;
            }

            return null;
        }

        public static string GetFriendlyBranch(string repoPath) {
            {
                var (c, s, _) = RunGit(repoPath, "branch --show-current", 15000);
                if (c == 0 && !string.IsNullOrWhiteSpace(s))
                    return s.Trim();
            }
            {
                var (c, s, _) = RunGit(repoPath, "rev-parse --abbrev-ref HEAD", 15000);
                if (c == 0 && !string.IsNullOrWhiteSpace(s) && s.Trim() != "HEAD")
                    return s.Trim();
            }
            {
                var (c, s, _) = RunGit(repoPath, "rev-parse --short=7 HEAD", 15000);
                if (c == 0 && !string.IsNullOrWhiteSpace(s))
                    return $"(detached @{s.Trim()})";
            }
            return "(unknown)";
        }

        public static IEnumerable<string> GetAllBranches(string repoPath) {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            {
                var (code, stdout, _) = RunGit(repoPath, "for-each-ref --format=%(refname:short) refs/heads", 20000);
                if (code == 0)
                    foreach (var l in stdout.Split(new[] {
                                 '\r', '\n'
                             }, StringSplitOptions.RemoveEmptyEntries))
                        set.Add(l.Trim());
            }
            {
                var (code, stdout, _) = RunGit(repoPath, "for-each-ref --format=%(refname:short) refs/remotes/origin", 20000);
                if (code == 0)
                    foreach (var l in stdout.Split(new[] {
                                 '\r', '\n'
                             }, StringSplitOptions.RemoveEmptyEntries)) {
                        var name = l.Trim();
                        if (name.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var idx = name.IndexOf('/');
                        set.Add(idx >= 0? name[(idx + 1)..] : name);
                    }
            }
            return set;
        }

        public static void FetchFast(string repoPath) {
            RunGit(repoPath, "fetch origin --prune --no-tags", 15000);
        }
        
        public static List<FileChangeItem> GetFileChanges(string repoPath)
        {
            var list = new List<FileChangeItem>();
            var (code, stdout, _) = RunGit(repoPath, "status --porcelain", 5000);
            
            if (code == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Length < 4) continue;
                    // XY PATH
                    char x = line[0];
                    char y = line[1];
                    string path = line.Substring(3).Trim(); // 从第3个字符开始截取路径(去除引号)
                    path = path.Trim('"'); // 处理 git 输出的引号

                    list.Add(new FileChangeItem { 
                        RawStatus = line.Substring(0, 2),
                        X = x.ToString(), 
                        Y = y.ToString(), 
                        FilePath = path 
                    });
                }
            }
            return list;
        }
        
        // [新增] 加入暂存区 (git add)
        public static void StageFile(string repoPath, string filePath)
        {
            RunGit(repoPath, $"add \"{filePath}\"", 5000);
        }

        // [新增] 移出暂存区 (git reset HEAD)
        public static void UnstageFile(string repoPath, string filePath)
        {
            // 如果是新文件(??)并未暂存，reset 会报错或无视，但逻辑上没问题
            // 标准反暂存命令
            RunGit(repoPath, $"reset HEAD -- \"{filePath}\"", 5000);
        }
        public static (int behind, int ahead)? GetSyncCounts(string repoPath)
        {
            // 1. 尝试标准查询 (基于配置的 Upstream)
            // 语法: git rev-list --left-right --count @{u}...HEAD
            var res = RunGit(repoPath, "rev-list --left-right --count @{u}...HEAD", 5000);
    
            if (res.code == 0 && !string.IsNullOrWhiteSpace(res.stdout))
            {
                return ParseSyncOutput(res.stdout);
            }

            // 2. [新增] 失败重试：如果未配置 Upstream，尝试强制对比同名远程分支
            // 获取当前分支名
            string currentBranch = GetFriendlyBranch(repoPath);
            if (!string.IsNullOrEmpty(currentBranch) && !currentBranch.Contains("detached"))
            {
                // 假设远程是 origin，对比 origin/分支名
                res = RunGit(repoPath, $"rev-list --left-right --count origin/{currentBranch}...HEAD", 5000);
                if (res.code == 0 && !string.IsNullOrWhiteSpace(res.stdout))
                {
                    return ParseSyncOutput(res.stdout);
                }
            }

            // 3. 彻底失败 (可能是新分支还没推送到远程，或者网络问题)
            return null; 
        }
        
        private static (int, int) ParseSyncOutput(string output)
        {
            var parts = output.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                int.TryParse(parts[0], out int behind);
                int.TryParse(parts[1], out int ahead);
                return (behind, ahead);
            }
            return (0, 0);
        }

        private static bool HasLocalChanges(string repoPath) {
            var (code, stdout, _) = RunGit(repoPath, "status --porcelain", 15000);
            return code == 0 && !string.IsNullOrWhiteSpace(stdout);
        }

        // ==================== 2. 核心业务逻辑 ====================

        public static(bool ok, string message) SwitchAndPull(string repoPath, string targetBranch, bool useStash, bool fastMode) {
            var log = new StringBuilder();
            void Step(string s) => log.AppendLine(s);

            if (fastMode)
                Step("> [极速模式] 跳过 Fetch");
            else {
                Step($"> 尝试极速拉取: origin {targetBranch}...");
                var fetchRes = RunGit(repoPath, $"fetch origin {targetBranch} --no-tags --prune --no-progress", 60_000);
                if (fetchRes.code != 0) {
                    Step($"⚠️ 极速拉取失败，尝试全量拉取...");
                    RunGit(repoPath, $"fetch --all --tags --prune --no-progress", 180_000);
                }
            }

            bool stashed = false;
            if (useStash) {
                if (HasLocalChanges(repoPath)) {
                    Step($"> stash push...");
                    var (cs, ss, es) = RunGit(repoPath, "stash push -u -m \"GitBranchSwitcher-auto\"", 120_000);
                    if (cs != 0)
                        return (false, log.AppendLine($"❌ Stash失败: {es}").ToString());
                    stashed = true;
                }
            } else {
                Step($"> 强制清理工作区...");
                RunGit(repoPath, "reset --hard", 60_000);
                if (!fastMode)
                    RunGit(repoPath, "clean -fd", 60_000);
            }

            bool localExists = RunGit(repoPath, $"show-ref --verify --quiet refs/heads/{targetBranch}", 20_000).code == 0;
            if (localExists) {
                Step($"> checkout -f \"{targetBranch}\"");
                var (c1, s1, e1) = RunGit(repoPath, $"checkout -f \"{targetBranch}\"", 90_000);
                if (c1 != 0)
                    return (false, log.AppendLine($"checkout 失败: {e1}").ToString());
            } else {
                if (fastMode)
                    RunGit(repoPath, $"fetch origin {targetBranch} --no-tags", 60_000);
                bool remoteExists = RunGit(repoPath, $"show-ref --verify --quiet refs/remotes/origin/{targetBranch}", 20_000).code == 0;
                if (!remoteExists)
                    return (false, log.AppendLine($"❌ 分支不存在: {targetBranch}").ToString());

                if (!useStash)
                    RunGit(repoPath, "reset --hard", 60_000);
                Step($"> checkout -B (new track)");
                var (c2, s2, e2) = RunGit(repoPath, $"checkout -B \"{targetBranch}\" \"origin/{targetBranch}\"", 120_000);
                if (c2 != 0)
                    return (false, log.AppendLine($"创建分支失败: {e2}").ToString());
            }

            if (!fastMode) {
                bool remoteTrackingExists = RunGit(repoPath, $"show-ref --verify --quiet refs/remotes/origin/{targetBranch}", 20_000).code == 0;
                if (remoteTrackingExists) {
                    if (!useStash) {
                        Step($"> [强制模式] Reset to origin/{targetBranch}...");
                        var (cr, sr, er) = RunGit(repoPath, $"reset --hard origin/{targetBranch}", 60_000);
                        if (cr != 0)
                            return (false, log.AppendLine($"❌ 强制同步失败: {er}").ToString());
                    } else {
                        Step($"> 尝试同步 (Fast-forward)...");
                        var (cm, sm, em) = RunGit(repoPath, $"merge --ff-only origin/{targetBranch}", 60_000);
                        if (cm != 0) {
                            log.AppendLine($"❌ 同步失败: 本地分支与远程分叉，无法快进。");
                            log.AppendLine($"原因: {em}");
                            return (false, log.ToString());
                        }
                    }
                }
            }

            if (useStash && stashed) {
                Step($"> stash pop");
                var (cp, sp, ep) = RunGit(repoPath, "stash pop --index", 180_000);
                if (cp != 0) {
                    log.AppendLine($"⚠️ Stash Pop 冲突: 请手动处理。");
                    return (false, log.ToString());
                }
            }

            return (true, log.AppendLine($"OK").ToString());
        }

        public static(bool ok, string message) PullCurrentBranch(string repoPath) {
            string branch = GetFriendlyBranch(repoPath);
            if (branch.Contains("detached") || branch.Contains("unknown") || branch == "HEAD")
                return (false, $"跳过: 游离状态 ({branch})");

            var (code, stdout, stderr) = RunGit(repoPath, "pull --no-rebase --no-tags", 60_000);
            if (code != 0) {
                if (stderr.Contains("Your local changes"))
                    return (false, "❌ 失败: 本地有修改未提交");
                if (stderr.Contains("no tracking information"))
                    return (false, "⚠️ 失败: 无上游分支");
                return (false, $"❌ Error: {stderr.Trim()}");
            }

            if (stdout.Contains("Already up to date"))
                return (true, "最新");
            return (true, "✅ 更新成功");
        }
        
        // [修改] 提交方法：只提交暂存区 (去掉 add -A)
        public static (bool ok, string message) Commit(string repoPath, string commitMsg)
        {
            // 1. 检查暂存区是否有内容
            // git diff --cached --quiet 返回 1 表示有变更，0 表示无变更
            var (c, _, _) = RunGit(repoPath, "diff --cached --quiet", 5000);
            if (c == 0) return (false, "⚠️ 暂存区为空，请先双击文件加入暂存区 (Staged Changes)！");

            // 2. Commit
            string safeMsg = commitMsg.Replace("\"", "'");
            var (code, stdout, stderr) = RunGit(repoPath, $"commit -m \"{safeMsg}\"", 30_000);

            if (code == 0) return (true, "✅ 提交成功");
            return (false, $"❌ 失败: {stderr.Trim()}");
        }

        // [新增] Push 方法
        public static(bool ok, string msg) Push(string repoPath) {
            var (code, stdout, stderr) = RunGit(repoPath, "push", 60_000);
            if (code == 0)
                return (true, "推送成功");
            return (false, $"失败: {stderr}");
        }

        public static(bool ok, string message) Clone(string repoUrl, string localPath, string branch, Action<string>? onProgress = null) {
            if (Directory.Exists(localPath) && Directory.GetFileSystemEntries(localPath).Length > 0) {
                if (IsGitRoot(localPath))
                    return (false, "目录已存在 Git 仓库");
                return (false, "目标目录非空，跳过");
            }

            string args = $"clone --branch \"{branch}\" \"{repoUrl}\" \"{localPath}\" --recursive --progress";
            var stderrSb = new StringBuilder();
            var psi = new ProcessStartInfo {
                FileName = "git",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

            try {
                using var p = new Process();
                p.StartInfo = psi;
                p.OutputDataReceived += (_, e) => {
                    if (e.Data != null)
                        onProgress?.Invoke(e.Data);
                };
                p.ErrorDataReceived += (_, e) => {
                    if (e.Data != null) {
                        stderrSb.AppendLine(e.Data);
                        onProgress?.Invoke(e.Data);
                    }
                };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                if (p.ExitCode == 0)
                    return (true, "克隆成功");
                return (false, $"失败 (Code {p.ExitCode}): {stderrSb}");
            } catch (Exception ex) {
                return (false, $"异常: {ex.Message}");
            }
        }

        // ==================== 3. 维护与修复 ====================

        public static(bool ok, string log, string sizeInfo, long bytesSaved) GarbageCollect(string repoPath, bool aggressive) {
            var log = new StringBuilder();
            void Step(string s) => log.AppendLine(s);
            string gitDir = Path.Combine(repoPath, ".git");
            long sizeBefore = GetDirectorySize(gitDir);
            Step($"初始大小: {FormatSize(sizeBefore)}");

            Step("> Expire reflog...");
            RunGit(repoPath, "reflog expire --expire=now --all", 30_000);
            Step("> Prune remote...");
            RunGit(repoPath, "remote prune origin", 60_000);

            string args = aggressive? "gc --prune=now --aggressive --window=50" : "gc --prune=now";
            Step($"> 执行 GC ({args})...");
            var (code, stdout, stderr) = RunGit(repoPath, args, -1);

            if (code != 0)
                return (false, log.AppendLine($"❌ 失败: {stderr}").ToString(), "无变化", 0);

            long sizeAfter = GetDirectorySize(gitDir);
            long saved = sizeBefore - sizeAfter;
            string resultMsg = saved >= 0? $"{FormatSize(saved)} ({FormatSize(sizeBefore)} -> {FormatSize(sizeAfter)})" : $"⚠️ 膨胀 {FormatSize(-saved)}";
            log.AppendLine($"✅ 完成！ 瘦身: {resultMsg}");
            return (true, log.ToString(), resultMsg, saved);
        }

        public static(bool ok, string log) RepairRepo(string repoPath) {
            var log = new StringBuilder();
            string gitDir = Path.Combine(repoPath, ".git");
            if (!Directory.Exists(gitDir))
                return (false, "找不到 .git");
            var locks = Directory.GetFiles(gitDir, "*.lock", SearchOption.AllDirectories);
            foreach (var f in locks) {
                try {
                    File.Delete(f);
                    log.AppendLine($"Deleted {Path.GetFileName(f)}");
                } catch {
                }
            }

            var r = RunGit(repoPath, "fsck --full --no-progress", -1);
            return (true, log.ToString() + "\n" + (r.code == 0? "Healthy" : r.stdout + r.stderr));
        }
        
        public static string GetFileDiff(string repoPath, string filePath, bool isStaged, bool isUntracked)
        {
            // 1. 如果是未追踪的新文件 (??)，git diff 没内容，直接读文件原原本本显示
            if (isUntracked)
            {
                try 
                {
                    string fullPath = Path.Combine(repoPath, filePath);
                    if (File.Exists(fullPath)) 
                    {
                        // 限制一下大小，防止读取几个G的二进制文件卡死
                        if (new FileInfo(fullPath).Length > 1024 * 1024) return "(文件过大，不支持预览)";
                        return File.ReadAllText(fullPath);
                    }
                    return "(文件已被删除或无法读取)";
                }
                catch (Exception ex) { return $"无法读取文件: {ex.Message}"; }
            }

            // 2. 构造 Diff 命令
            // Staged: git diff --cached -- file
            // Unstaged: git diff -- file
            string args = isStaged 
                ? $"diff --cached --no-color -- \"{filePath}\"" 
                : $"diff --no-color -- \"{filePath}\"";

            var (code, stdout, stderr) = RunGit(repoPath, args, 5000);
            return code == 0 ? stdout : stderr;
        }

        // ==================== 4. 底层与工具 ====================

        public static(int code, string stdout, string stderr) RunGit(string workingDir, string args, int timeoutMs = 120000) {
            var stdoutSb = new StringBuilder();
            var stderrSb = new StringBuilder();
            string safeArgs = $"-c core.quotepath=false -c credential.helper= {args}";
            var psi = new ProcessStartInfo {
                FileName = "git",
                Arguments = safeArgs,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GCM_INTERACTIVE"] = "Never";
            psi.Environment["GIT_ASKPASS"] = "echo";

            try {
                using var p = new Process();
                p.StartInfo = psi;
                var outWait = new System.Threading.ManualResetEvent(false);
                var errWait = new System.Threading.ManualResetEvent(false);
                p.OutputDataReceived += (_, e) => {
                    if (e.Data == null)
                        outWait.Set();
                    else
                        stdoutSb.AppendLine(e.Data);
                };
                p.ErrorDataReceived += (_, e) => {
                    if (e.Data == null)
                        errWait.Set();
                    else
                        stderrSb.AppendLine(e.Data);
                };
                if (!p.Start())
                    return (-1, "", "Git无法启动");
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (timeoutMs < 0)
                    p.WaitForExit();
                else if (!p.WaitForExit(timeoutMs)) {
                    try {
                        p.Kill(true);
                    } catch {
                    }

                    return (-2, stdoutSb.ToString(), $"超时(>{timeoutMs / 1000}s)");
                }

                outWait.WaitOne(5000);
                errWait.WaitOne(5000);
                return (p.ExitCode, stdoutSb.ToString(), stderrSb.ToString());
            } catch (Exception ex) {
                return (-3, "", ex.Message);
            }
        }

        public static List<string> ScanForGitRepositories(string rootPath) {
            var repos = new List<string>();
            try {
                if (!Directory.Exists(rootPath))
                    return repos;
                if (IsGitRoot(rootPath))
                    repos.Add(rootPath);
                var subDirs = Directory.GetDirectories(rootPath);
                foreach (var dir in subDirs) {
                    var name = Path.GetFileName(dir);
                    if (IsIgnoredFolder(name))
                        continue;
                    repos.AddRange(ScanForGitRepositories(dir));
                }
            } catch {
            }

            return repos;
        }

        private static bool IsGitRoot(string path) => Directory.Exists(Path.Combine(path, ".git"));
        private static bool IsIgnoredFolder(string name) => name.Equals(".git", StringComparison.OrdinalIgnoreCase) || name.Equals(".vs", StringComparison.OrdinalIgnoreCase) || name.Equals(".idea", StringComparison.OrdinalIgnoreCase) || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) || name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase);

        private static long GetDirectorySize(string path) {
            try {
                if (!Directory.Exists(path))
                    return 0;
                return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
            } catch {
                return 0;
            }
        }

        private static string FormatSize(long bytes) {
            if (bytes == 0)
                return "0B";
            string prefix = bytes < 0? "-" : "";
            long absBytes = Math.Abs(bytes);
            if (absBytes < 1024)
                return $"{prefix}{absBytes}B";
            long gb = absBytes / (1024 * 1024 * 1024);
            long rem = absBytes % (1024 * 1024 * 1024);
            long mb = rem / (1024 * 1024);
            rem = rem % (1024 * 1024);
            long kb = rem / 1024;
            var sb = new StringBuilder();
            sb.Append(prefix);
            if (gb > 0)
                sb.Append($"{gb}GB ");
            if (mb > 0)
                sb.Append($"{mb}MB ");
            if (kb > 0)
                sb.Append($"{kb}KB");
            return sb.ToString().Trim();
        }
    }
}