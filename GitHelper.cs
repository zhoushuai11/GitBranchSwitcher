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
        // ==================== 1. 基础查询与操作 ====================

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

        // 快速 Fetch，用于后台静默刷新
        public static void FetchFast(string repoPath)
        {
            RunGit(repoPath, "fetch origin --prune --no-tags", 15000);
        }

        private static bool HasLocalChanges(string repoPath)
        {
            var (code, stdout, _) = RunGit(repoPath, "status --porcelain", 15000);
            return code == 0 && !string.IsNullOrWhiteSpace(stdout);
        }

        // ==================== 2. 核心业务逻辑 ====================

        // [核心 1] 切换与拉取 (Switch & Pull)
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
                var fetchRes = RunGit(repoPath, $"fetch origin {targetBranch} --no-tags --prune --no-progress", 60_000);
                if (fetchRes.code != 0)
                {
                    Step($"⚠️ 极速拉取失败 ({fetchRes.stderr?.Trim()}), 尝试全量拉取...");
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
                Step($"> 强制清理工作区 (clean)...");
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
                if (fastMode) RunGit(repoPath, $"fetch origin {targetBranch} --no-tags", 60_000);
                bool remoteExists = RunGit(repoPath, $"show-ref --verify --quiet refs/remotes/origin/{targetBranch}", 20_000).code == 0;
                if (!remoteExists) return (false, log.AppendLine($"❌ 分支不存在: {targetBranch}").ToString());

                if (!useStash) RunGit(repoPath, "reset --hard", 60_000);
                Step($"> checkout -B (new track)");
                var (c2, s2, e2) = RunGit(repoPath, $"checkout -B \"{targetBranch}\" \"origin/{targetBranch}\"", 120_000);
                if (c2 != 0) return (false, log.AppendLine($"创建分支失败: {e2}").ToString());
            }

            // 4. 同步远程
            if (!fastMode)
            {
                bool remoteTrackingExists = RunGit(repoPath, $"show-ref --verify --quiet refs/remotes/origin/{targetBranch}", 20_000).code == 0;
                if (remoteTrackingExists)
                {
                    if (!useStash)
                    {
                        Step($"> [强制模式] Reset to origin/{targetBranch}...");
                        var (cr, sr, er) = RunGit(repoPath, $"reset --hard origin/{targetBranch}", 60_000);
                        if (cr != 0) return (false, log.AppendLine($"❌ 强制同步失败: {er}").ToString());
                    }
                    else
                    {
                        Step($"> 尝试同步 (Fast-forward)...");
                        var (cm, sm, em) = RunGit(repoPath, $"merge --ff-only origin/{targetBranch}", 60_000);
                        if (cm != 0)
                        {
                            log.AppendLine($"❌ 同步失败: 本地分支与远程分叉，无法快进。");
                            log.AppendLine($"原因: {em}");
                            return (false, log.ToString());
                        }
                    }
                }
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

        // [核心 2] 批量拉取 (Pull) - 修复编译报错的方法
        public static (bool ok, string message) PullCurrentBranch(string repoPath)
        {
            string branch = GetFriendlyBranch(repoPath);
            if (branch.Contains("detached") || branch.Contains("unknown") || branch == "HEAD")
            {
                return (false, $"跳过: 游离状态 ({branch})");
            }

            var (code, stdout, stderr) = RunGit(repoPath, "pull --no-rebase --no-tags", 60_000);

            if (code != 0)
            {
                if (stderr.Contains("Your local changes")) return (false, "❌ 失败: 本地有修改未提交");
                if (stderr.Contains("no tracking information")) return (false, "⚠️ 失败: 无上游分支");
                return (false, $"❌ Error: {stderr.Trim()}");
            }

            if (stdout.Contains("Already up to date")) return (true, "最新");
            return (true, "✅ 更新成功");
        }

        // [核心 3] 克隆 (Clone) - 支持拉线助手
        public static (bool ok, string message) Clone(string repoUrl, string localPath, string branch, Action<string>? onProgress = null)
        {
            if (Directory.Exists(localPath) && Directory.GetFileSystemEntries(localPath).Length > 0)
            {
                if (IsGitRoot(localPath)) return (false, "目录已存在 Git 仓库");
                return (false, "目标目录非空，跳过");
            }

            string args = $"clone --branch \"{branch}\" \"{repoUrl}\" \"{localPath}\" --recursive --progress";
            var stderrSb = new StringBuilder();

            var psi = new ProcessStartInfo
            {
                FileName = "git", Arguments = args, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

            try
            {
                using var p = new Process(); p.StartInfo = psi;
                p.OutputDataReceived += (_, e) => { if (e.Data != null) onProgress?.Invoke(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) { stderrSb.AppendLine(e.Data); onProgress?.Invoke(e.Data); } };
                p.Start();
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                p.WaitForExit();

                if (p.ExitCode == 0) return (true, "克隆成功");
                return (false, $"失败 (Code {p.ExitCode}): {stderrSb}");
            }
            catch (Exception ex) { return (false, $"异常: {ex.Message}"); }
        }

        // ==================== 3. 维护与修复 ====================

        public static (bool ok, string log, string sizeInfo, long bytesSaved) GarbageCollect(string repoPath, bool aggressive)
        {
            var log = new StringBuilder();
            void Step(string s) => log.AppendLine(s);

            string gitDir = Path.Combine(repoPath, ".git");
            long sizeBefore = GetDirectorySize(gitDir);
            Step($"初始大小: {FormatSize(sizeBefore)}");

            Step("> Expire reflog (强制清理)...");
            RunGit(repoPath, "reflog expire --expire=now --all", 30_000); // 强力瘦身关键

            Step("> Prune remote origin...");
            RunGit(repoPath, "remote prune origin", 60_000);

            string args = aggressive ? "gc --prune=now --aggressive --window=50" : "gc --prune=now";
            Step($"> 执行 GC ({args})...");
            
            var (code, stdout, stderr) = RunGit(repoPath, args, -1);

            if (code != 0) return (false, log.AppendLine($"❌ 失败: {stderr}").ToString(), "无变化", 0);

            long sizeAfter = GetDirectorySize(gitDir);
            long saved = sizeBefore - sizeAfter;

            string resultMsg;
            if (saved >= 0) {
                resultMsg = $"{FormatSize(saved)} ({FormatSize(sizeBefore)} -> {FormatSize(sizeAfter)})";
                log.AppendLine($"✅ 完成！ 瘦身: {resultMsg}");
            } else {
                resultMsg = $"⚠️ 膨胀 {FormatSize(-saved)}";
                log.AppendLine($"✅ 完成，但体积增加了。");
            }

            return (true, log.ToString(), resultMsg, saved);
        }

        public static (bool ok, string log) RepairRepo(string repoPath)
        {
            var log = new StringBuilder();
            string gitDir = Path.Combine(repoPath, ".git");
            if (!Directory.Exists(gitDir)) return (false, "找不到 .git");
            var locks = Directory.GetFiles(gitDir, "*.lock", SearchOption.AllDirectories);
            foreach (var f in locks) { try { File.Delete(f); log.AppendLine($"Deleted {Path.GetFileName(f)}"); } catch { } }
            var r = RunGit(repoPath, "fsck --full --no-progress", -1);
            return (true, log.ToString() + "\n" + (r.code == 0 ? "Healthy" : r.stdout + r.stderr));
        }

        // ==================== 4. 底层与工具 ====================

        public static (int code, string stdout, string stderr) RunGit(string workingDir, string args, int timeoutMs = 120000)
        {
            var stdoutSb = new StringBuilder(); var stderrSb = new StringBuilder();
            string safeArgs = $"-c core.quotepath=false -c credential.helper= {args}";
            var psi = new ProcessStartInfo
            {
                FileName = "git", Arguments = safeArgs, WorkingDirectory = workingDir,
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0"; psi.Environment["GCM_INTERACTIVE"] = "Never"; psi.Environment["GIT_ASKPASS"] = "echo";

            try
            {
                using var p = new Process(); p.StartInfo = psi;
                var outWait = new System.Threading.ManualResetEvent(false); var errWait = new System.Threading.ManualResetEvent(false);
                p.OutputDataReceived += (_, e) => { if (e.Data == null) outWait.Set(); else stdoutSb.AppendLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data == null) errWait.Set(); else stderrSb.AppendLine(e.Data); };
                if (!p.Start()) return (-1, "", "Git无法启动");
                p.BeginOutputReadLine(); p.BeginErrorReadLine();

                if (timeoutMs < 0) { p.WaitForExit(); }
                else { if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-2, stdoutSb.ToString(), $"超时(>{timeoutMs / 1000}s)"); } }
                
                outWait.WaitOne(5000); errWait.WaitOne(5000);
                return (p.ExitCode, stdoutSb.ToString(), stderrSb.ToString());
            }
            catch (Exception ex) { return (-3, "", ex.Message); }
        }

        public static List<string> ScanForGitRepositories(string rootPath)
        {
            var repos = new List<string>();
            try
            {
                if (!Directory.Exists(rootPath)) return repos;
                if (IsGitRoot(rootPath)) repos.Add(rootPath);
                var subDirs = Directory.GetDirectories(rootPath);
                foreach (var dir in subDirs)
                {
                    var name = Path.GetFileName(dir);
                    if (IsIgnoredFolder(name)) continue;
                    repos.AddRange(ScanForGitRepositories(dir));
                }
            }
            catch { }
            return repos;
        }

        private static bool IsGitRoot(string path) => Directory.Exists(Path.Combine(path, ".git"));

        private static bool IsIgnoredFolder(string name)
        {
            return name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals(".idea", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase);
        }

        private static long GetDirectorySize(string path) { try { if (!Directory.Exists(path)) return 0; return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length); } catch { return 0; } }

        private static string FormatSize(long bytes)
        {
            if (bytes == 0) return "0B";
            string prefix = bytes < 0 ? "-" : "";
            long absBytes = Math.Abs(bytes);
            if (absBytes < 1024) return $"{prefix}{absBytes}B";
            long gb = absBytes / (1024 * 1024 * 1024);
            long rem = absBytes % (1024 * 1024 * 1024);
            long mb = rem / (1024 * 1024);
            rem = rem % (1024 * 1024);
            long kb = rem / 1024;
            var sb = new StringBuilder();
            sb.Append(prefix);
            if (gb > 0) sb.Append($"{gb}GB ");
            if (mb > 0) sb.Append($"{mb}MB ");
            if (kb > 0) sb.Append($"{kb}KB");
            return sb.ToString().Trim();
        }
    }
}