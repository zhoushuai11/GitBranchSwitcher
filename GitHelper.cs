using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GitBranchSwitcher
{
    public static class GitHelper
    {
        public static string? FindGitRoot(string startPath)
        {
            var dir = new DirectoryInfo(startPath);
            while (dir != null)
            {
                var gitDir = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(gitDir) || File.Exists(gitDir))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        public static string GetFriendlyBranch(string repoPath)
        {
            // 1) branch --show-current
            {
                var (c, s, _) = RunGit(repoPath, "branch --show-current", 15000);
                var line = s?.Trim();
                if (c == 0 && !string.IsNullOrEmpty(line)) return line;
            }
            // 2) rev-parse --abbrev-ref HEAD
            {
                var (c, s, _) = RunGit(repoPath, "rev-parse --abbrev-ref HEAD", 15000);
                var line = s?.Trim();
                if (c == 0 && !string.IsNullOrEmpty(line) && !string.Equals(line, "HEAD", StringComparison.OrdinalIgnoreCase))
                    return line;
            }
            // 3) fallback 到短 SHA
            {
                var (c, s, _) = RunGit(repoPath, "rev-parse --short=7 HEAD", 15000);
                var sha = s?.Trim();
                if (c == 0 && !string.IsNullOrEmpty(sha)) return $"(detached @{sha})";
            }
            return "(unknown)";
        }

        public static IEnumerable<string> GetAllBranches(string repoPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // local
            {
                var (code, stdout, _) = RunGit(repoPath, "for-each-ref --format=%(refname:short) refs/heads", 20000);
                if (code == 0)
                    foreach (var l in stdout.Split(new[] { '\r','\n' }, StringSplitOptions.RemoveEmptyEntries))
                        set.Add(l.Trim());
            }
            // origin/*
            {
                var (code, stdout, _) = RunGit(repoPath, "for-each-ref --format=%(refname:short) refs/remotes/origin", 20000);
                if (code == 0)
                    foreach (var l in stdout.Split(new[] { '\r','\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var name = l.Trim();
                        if (name.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase)) continue;
                        var idx = name.IndexOf('/');
                        var b = idx >= 0 ? name[(idx+1)..] : name;
                        set.Add(b);
                    }
            }
            return set;
        }

        private static bool HasLocalChanges(string repoPath)
        {
            var (code, stdout, _) = RunGit(repoPath, "status --porcelain", 15000);
            return code == 0 && !string.IsNullOrWhiteSpace(stdout);
        }

        public static (bool ok, string message) SwitchAndPull(string repoPath, string targetBranch, bool useStash)
        {
            var log = new StringBuilder();
            void Step(string s) => log.AppendLine(s);

            Step($"> fetch --all --prune --tags --no-progress");
            // working tree handling (1.0 RunGit signature: (code, stdout, stderr))
            if (useStash)
            {
                var dirty = RunGit(repoPath, "status --porcelain", 20000);
                var hasChanges = dirty.code == 0 && !string.IsNullOrWhiteSpace(dirty.stdout);
                if (hasChanges)
                    _ = RunGit(repoPath, "stash push -u -m \"GitBranchSwitcher-auto\"", 120000);
            }
            else
            {
                RunGit(repoPath, "reset --hard", 60000);
                RunGit(repoPath, "clean -fd", 60000);
            }
        
            RunGit(repoPath, "fetch --all --prune --tags --no-progress", 180_000);

            bool stashed = false;

            if (useStash)
            {
                if (HasLocalChanges(repoPath))
                {
                    Step($"> stash push -u -m \"GitBranchSwitcher auto-stash\"");
                    var (cs, ss, es) = RunGit(repoPath, "stash push -u -m \"GitBranchSwitcher auto-stash\"", 120_000);
                    if (cs != 0) return (false, log.AppendLine($"stash 失败：{es} {ss}").ToString());
                    stashed = true;
                }
            }
            else
            {
                Step($"> reset --hard");
                RunGit(repoPath, "reset --hard", 60_000);
                Step($"> clean -fd");
                RunGit(repoPath, "clean -fd", 60_000);
            }

            bool localExists = RunGit(repoPath, $"show-ref --verify --quiet refs/heads/{targetBranch}", 20_000).code == 0;
            if (localExists)
            {
                Step($"> checkout -f \"{targetBranch}\"");
                var (c1, s1, e1) = RunGit(repoPath, $"checkout -f \"{targetBranch}\"", 90_000);
                if (c1 != 0) return (false, log.AppendLine($"checkout 失败：{e1} {s1}").ToString());
            }
            else
            {
                bool remoteExists = RunGit(repoPath, $"show-ref --verify --quiet refs/remotes/origin/{targetBranch}", 20_000).code == 0;
                if (!remoteExists) return (false, log.AppendLine($"分支不存在（本地/远程均未找到）：{targetBranch}").ToString());

                if (!useStash)
                {
                    RunGit(repoPath, "reset --hard", 60_000);
                    RunGit(repoPath, "clean -fd", 60_000);
                }
                Step($"> checkout -B \"{targetBranch}\" \"origin/{targetBranch}\"");
                var (c2, s2, e2) = RunGit(repoPath, $"checkout -B \"{targetBranch}\" \"origin/{targetBranch}\"", 120_000);
                if (c2 != 0) return (false, log.AppendLine($"创建追踪分支失败：{e2} {s2}").ToString());
            }

            Step($"> pull --ff-only --no-progress");
            var (c3, s3, e3) = RunGit(repoPath, "pull --ff-only --no-progress", 240_000);
            if (c3 != 0) return (false, log.AppendLine($"pull 失败：{e3} {s3}").ToString());

            if (useStash && stashed)
            {
                Step($"> stash pop --index");
                var (cp, sp, ep) = RunGit(repoPath, "stash pop --index", 180_000);
                if (cp != 0)
                {
                    log.AppendLine($"stash pop 冲突或失败：{ep} {sp} —— 已保留 stash，请手动解决后再 pop/drop。");
                    return (false, log.ToString());
                }
            }

            return (true, log.AppendLine($"OK：已切换到 {targetBranch} 并完成 pull。").ToString());
        }

        public static (int code, string stdout, string stderr) RunGit(string workingDir, string args, int timeoutMs = 120000)
        {
            var stdoutSb = new StringBuilder();
            var stderrSb = new StringBuilder();

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
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

            try
            {
                using var p = new Process();
                p.StartInfo = psi;

                var stdoutDone = new System.Threading.ManualResetEvent(false);
                var stderrDone = new System.Threading.ManualResetEvent(false);

                p.OutputDataReceived += (_, e) => { if (e.Data == null) stdoutDone.Set(); else stdoutSb.AppendLine(e.Data); };
                p.ErrorDataReceived  += (_, e) => { if (e.Data == null) stderrDone.Set();  else stderrSb.AppendLine(e.Data);  };

                if (!p.Start()) return (-1, "", "无法启动 git 进程。");

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    return (-2, stdoutSb.ToString(), $"git 超时（>{timeoutMs}ms）：{args}");
                }

                stdoutDone.WaitOne(5000);
                stderrDone.WaitOne(5000);

                return (p.ExitCode, stdoutSb.ToString(), stderrSb.ToString());
            }
            catch (Exception ex)
            {
                return (-3, "", "执行 git 异常：" + ex.Message);
            }
        }
    }
}
