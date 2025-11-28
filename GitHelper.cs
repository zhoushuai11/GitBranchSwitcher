using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace GitBranchSwitcher
{
    public static class GitHelper
    {
        // ==================== 基础辅助方法 ====================

        public static string? FindGitRoot(string startPath)
        {
            var dir = new DirectoryInfo(startPath);
            while (dir != null)
            {
                var gitDir = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(gitDir) || File.Exists(gitDir)) return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        public static string GetFriendlyBranch(string repoPath)
        {
            { var (c, s, _) = RunGit(repoPath, "branch --show-current", 15000); if (c == 0 && !string.IsNullOrWhiteSpace(s)) return s.Trim(); }
            { var (c, s, _) = RunGit(repoPath, "rev-parse --abbrev-ref HEAD", 15000); if (c == 0 && !string.IsNullOrWhiteSpace(s) && s.Trim() != "HEAD") return s.Trim(); }
            { var (c, s, _) = RunGit(repoPath, "rev-parse --short=7 HEAD", 15000); if (c == 0 && !string.IsNullOrWhiteSpace(s)) return $"(detached @{s.Trim()})"; }
            return "(unknown)";
        }

        public static IEnumerable<string> GetAllBranches(string repoPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 本地分支
            {
                var (code, stdout, _) = RunGit(repoPath, "for-each-ref --format=%(refname:short) refs/heads", 20000);
                if (code == 0) foreach (var l in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) set.Add(l.Trim());
            }
            // 远程分支
            {
                var (code, stdout, _) = RunGit(repoPath, "for-each-ref --format=%(refname:short) refs/remotes/origin", 20000);
                if (code == 0) foreach (var l in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var name = l.Trim();
                        if (name.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase)) continue;
                        var idx = name.IndexOf('/');
                        set.Add(idx >= 0 ? name[(idx + 1)..] : name);
                    }
            }
            return set;
        }

        private static bool HasLocalChanges(string repoPath)
        {
            var (code, stdout, _) = RunGit(repoPath, "status --porcelain", 15000);
            return code == 0 && !string.IsNullOrWhiteSpace(stdout);
        }

        // ==================== 核心切线逻辑 ====================

        public static (bool ok, string message) SwitchAndPull(string repoPath, string targetBranch, bool useStash, bool fastMode)
        {
            var log = new StringBuilder();
            void Step(string s) => log.AppendLine(s);

            // 1. 网络操作 (Fetch)
            if (fastMode)
            {
                Step("> [极速模式] 跳过 Fetch");
            }
            else
            {
                Step($"> 尝试极速拉取: origin {targetBranch}...");
                // 优先拉取单分支，超时 60s
                var fetchRes = RunGit(repoPath, $"fetch origin {targetBranch} --no-tags --prune --no-progress", 60_000);
                if (fetchRes.code != 0)
                {
                    Step($"⚠️ 极速拉取失败 ({fetchRes.stderr?.Trim()}), 尝试全量拉取...");
                    // 降级全量拉取，超时 3分钟
                    RunGit(repoPath, $"fetch --all --tags --prune --no-progress", 180_000);
                }
            }

            // 2. 本地修改处理
            bool stashed = false;
            if (useStash)
            {
                if (HasLocalChanges(repoPath))
                {
                    Step($"> stash push...");
                    var (cs, ss, es) = RunGit(repoPath, "stash push -u -m \"GitBranchSwitcher-auto\"", 120_000);
                    if (cs != 0) return (false, log.AppendLine($"❌ Stash失败: {es}").ToString());
                    stashed = true;
                }
            }
            else
            {
                Step($"> 强制清理...");
                RunGit(repoPath, "reset --hard", 60_000);
                if (!fastMode) RunGit(repoPath, "clean -fd", 60_000);
            }

            // 3. 检查与切换
            bool localExists = RunGit(repoPath, $"show-ref --verify --quiet refs/heads/{targetBranch}", 20_000).code == 0;
            if (localExists)
            {
                Step($"> checkout -f \"{targetBranch}\"");
                var (c1, s1, e1) = RunGit(repoPath, $"checkout -f \"{targetBranch}\"", 90_000);
                if (c1 != 0) return (false, log.AppendLine($"checkout 失败: {e1}").ToString());
            }
            else
            {
                // 如果是极速模式且本地无分支，尝试临时 fetch 一下
                if (fastMode)
                {
                    Step($"> 本地无分支，补充 Fetch...");
                    RunGit(repoPath, $"fetch origin {targetBranch} --no-tags", 60_000);
                }

                bool remoteExists = RunGit(repoPath, $"show-ref --verify --quiet refs/remotes/origin/{targetBranch}", 20_000).code == 0;
                if (!remoteExists) return (false, log.AppendLine($"❌ 分支不存在: {targetBranch}").ToString());

                if (!useStash) RunGit(repoPath, "reset --hard", 60_000);

                Step($"> checkout -B (new track)");
                var (c2, s2, e2) = RunGit(repoPath, $"checkout -B \"{targetBranch}\" \"origin/{targetBranch}\"", 120_000);
                if (c2 != 0) return (false, log.AppendLine($"创建分支失败: {e2}").ToString());
            }

            // 4. Pull
            if (fastMode)
            {
                Step($"> [极速模式] 跳过 Pull");
            }
            else
            {
                Step($"> pull --ff-only");
                var (c3, s3, e3) = RunGit(repoPath, "pull --ff-only --no-progress", 120_000);
                if (c3 != 0) log.AppendLine($"⚠️ Pull警告: {e3}");
            }

            // 5. Stash Pop
            if (useStash && stashed)
            {
                Step($"> stash pop");
                var (cp, sp, ep) = RunGit(repoPath, "stash pop --index", 180_000);
                if (cp != 0)
                {
                    log.AppendLine($"⚠️ Stash Pop 冲突: 请手动处理。");
                    return (false, log.ToString());
                }
            }

            return (true, log.AppendLine($"OK").ToString());
        }

        // ==================== 仓库瘦身 (GC) 逻辑 ====================
        public static (bool ok, string log, string sizeInfo) GarbageCollect(string repoPath, bool aggressive)
        {
            var log = new StringBuilder();
            void Step(string s) => log.AppendLine(s);

            // 1. 计算清理前大小
            string gitDir = Path.Combine(repoPath, ".git");
            long sizeBefore = GetDirectorySize(gitDir);
            Step($"初始大小: {FormatSize(sizeBefore)}");

            // 2. 执行清理
            Step("> Prune remote origin...");
            RunGit(repoPath, "remote prune origin", 60_000);

            string args;
            int timeout;

            if (aggressive)
            {
                // 方案 B：深度清理 -> 无限等待 (-1)
                Step("> 🚀 深度清理 (--aggressive)... 这可能需要数小时，请挂机等待。");
                args = "gc --prune=now --aggressive";
                timeout = -1; // [修改] 无限超时
            }
            else
            {
                // 方案 A：快速清理 -> 1小时超时
                Step("> 🧹 快速清理... 大仓库可能需要 10-30 分钟。");
                args = "gc --prune=now";
                timeout = 3_600_000; // [修改] 1小时 (3600s)
            }

            var (code, stdout, stderr) = RunGit(repoPath, args, timeout);

            if (code != 0) 
                return (false, log.AppendLine($"❌ 失败: {stderr}").ToString(), "无变化");

            // 3. 计算清理后大小
            long sizeAfter = GetDirectorySize(gitDir);
            long saved = sizeBefore - sizeAfter;
            if (saved < 0) saved = 0;

            string resultMsg = $"{FormatSize(saved)} ({FormatSize(sizeBefore)} -> {FormatSize(sizeAfter)})";
            log.AppendLine($"✅ 完成！ 瘦身: {resultMsg}");

            return (true, log.ToString(), FormatSize(saved));
        }

        private static long GetDirectorySize(string path)
        {
            try {
                if (!Directory.Exists(path)) return 0;
                // 忽略 .lock 文件避免访问冲突
                return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
            } catch { return 0; }
        }

        private static string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1}{1}", number, suffixes[counter]);
        }

        // ==================== 底层执行逻辑 ====================

        public static (int code, string stdout, string stderr) RunGit(string workingDir, string args, int timeoutMs = 120000)
        {
            var stdoutSb = new StringBuilder();
            var stderrSb = new StringBuilder();
            string safeArgs = $"-c core.quotepath=false -c credential.helper= {args}";

            var psi = new ProcessStartInfo {
                FileName = "git", Arguments = safeArgs, WorkingDirectory = workingDir,
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GCM_INTERACTIVE"] = "Never";
            psi.Environment["GIT_ASKPASS"] = "echo";

            try {
                using var p = new Process();
                p.StartInfo = psi;

                var outWait = new System.Threading.ManualResetEvent(false);
                var errWait = new System.Threading.ManualResetEvent(false);

                p.OutputDataReceived += (_, e) => { if (e.Data == null) outWait.Set(); else stdoutSb.AppendLine(e.Data); };
                p.ErrorDataReceived  += (_, e) => { if (e.Data == null) errWait.Set(); else stderrSb.AppendLine(e.Data); };

                if (!p.Start()) return (-1, "", "Git无法启动");

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                // [关键修改] 支持 -1 无限等待
                if (timeoutMs < 0)
                {
                    p.WaitForExit(); // 无限等待
                }
                else
                {
                    if (!p.WaitForExit(timeoutMs)) {
                        try { p.Kill(true); } catch { }
                        return (-2, stdoutSb.ToString(), $"超时(>{timeoutMs/1000}s)");
                    }
                }

                outWait.WaitOne(5000); 
                errWait.WaitOne(5000);

                return (p.ExitCode, stdoutSb.ToString(), stderrSb.ToString());
            } catch (Exception ex) { return (-3, "", ex.Message); }
        }
        
        // [新增] 仓库修复工具：删除锁文件 + 健康检查
        public static (bool ok, string log) RepairRepo(string repoPath)
        {
            var log = new StringBuilder();
            string gitDir = Path.Combine(repoPath, ".git");
            
            if (!Directory.Exists(gitDir)) 
                return (false, "找不到 .git 目录");

            // 1. 暴力解锁 (删除 .lock 文件)
            log.AppendLine("=== 正在扫描锁文件 (.lock) ===");
            int delCount = 0;
            
            // 常见的锁文件位置
            var lockFiles = new List<string>();
            
            // 根目录锁
            lockFiles.Add(Path.Combine(gitDir, "index.lock")); // 最常见的
            lockFiles.Add(Path.Combine(gitDir, "HEAD.lock"));
            lockFiles.Add(Path.Combine(gitDir, "config.lock"));
            lockFiles.Add(Path.Combine(gitDir, "packed-refs.lock"));
            
            // 递归搜索 refs 目录下的锁 (refs/heads/master.lock 等)
            string refsDir = Path.Combine(gitDir, "refs");
            if (Directory.Exists(refsDir))
            {
                lockFiles.AddRange(Directory.GetFiles(refsDir, "*.lock", SearchOption.AllDirectories));
            }

            foreach (var f in lockFiles)
            {
                if (File.Exists(f))
                {
                    try
                    {
                        File.Delete(f);
                        log.AppendLine($"✅ 已删除锁文件: {Path.GetFileName(f)}");
                        delCount++;
                    }
                    catch (Exception ex)
                    {
                        log.AppendLine($"❌ 删除失败 {Path.GetFileName(f)}: {ex.Message}");
                    }
                }
            }

            if (delCount == 0) log.AppendLine("未发现锁文件，仓库未被锁定。");
            else log.AppendLine($"共清理了 {delCount} 个锁文件。");

            // 2. 健康检查 (fsck)
            log.AppendLine("\n=== 执行健康检查 (git fsck) ===");
            // fsck 检查数据库完整性
            var (code, stdout, stderr) = RunGit(repoPath, "fsck --full --no-progress", 60_000);
            
            if (code == 0)
            {
                log.AppendLine("✅ 仓库数据库健康 (Healthy)");
            }
            else
            {
                log.AppendLine("⚠️ 发现潜在问题 (不一定是损坏，可能是悬空对象):");
                log.AppendLine(stdout);
                log.AppendLine(stderr);
            }

            return (true, log.ToString());
        }
    }
}