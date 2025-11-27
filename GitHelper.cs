using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GitBranchSwitcher
{
    public static class GitHelper
    {
        // 查找 Git 根目录
        public static string? FindGitRoot(string startPath) {
            var dir = new DirectoryInfo(startPath);
            while (dir != null) {
                var gitDir = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(gitDir) || File.Exists(gitDir)) return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        // 获取友好的分支名显示
        public static string GetFriendlyBranch(string repoPath) {
            { var (c, s, _) = RunGit(repoPath, "branch --show-current", 15000); if (c == 0 && !string.IsNullOrWhiteSpace(s)) return s.Trim(); }
            { var (c, s, _) = RunGit(repoPath, "rev-parse --abbrev-ref HEAD", 15000); if (c == 0 && !string.IsNullOrWhiteSpace(s) && s.Trim() != "HEAD") return s.Trim(); }
            { var (c, s, _) = RunGit(repoPath, "rev-parse --short=7 HEAD", 15000); if (c == 0 && !string.IsNullOrWhiteSpace(s)) return $"(detached @{s.Trim()})"; }
            return "(unknown)";
        }

        // 获取所有分支
        public static IEnumerable<string> GetAllBranches(string repoPath) {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 本地
            { var (code, stdout, _) = RunGit(repoPath, "for-each-ref --format=%(refname:short) refs/heads", 20000); 
              if (code == 0) foreach (var l in stdout.Split(new[] { '\r','\n' }, StringSplitOptions.RemoveEmptyEntries)) set.Add(l.Trim()); }
            // 远程
            { var (code, stdout, _) = RunGit(repoPath, "for-each-ref --format=%(refname:short) refs/remotes/origin", 20000);
              if (code == 0) foreach (var l in stdout.Split(new[] { '\r','\n' }, StringSplitOptions.RemoveEmptyEntries)) {
                  var name = l.Trim();
                  if (name.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase)) continue;
                  var idx = name.IndexOf('/');
                  set.Add(idx >= 0 ? name[(idx+1)..] : name);
              }}
            return set;
        }

        private static bool HasLocalChanges(string repoPath) {
            var (code, stdout, _) = RunGit(repoPath, "status --porcelain", 15000);
            return code == 0 && !string.IsNullOrWhiteSpace(stdout);
        }

        // [核心优化] 智能极速切线逻辑
        public static (bool ok, string message) SwitchAndPull(string repoPath, string targetBranch, bool useStash)
        {
            var log = new StringBuilder();
            void Step(string s) => log.AppendLine(s);

            // 1. 尝试极速拉取 (Fast Path)
            // 策略：只拉取目标分支，不拉 Tag，超时时间短。如果成功，速度极快 (<1s)。
            Step($"> 尝试极速拉取: origin {targetBranch}...");
            var fetchRes = RunGit(repoPath, $"fetch origin {targetBranch} --no-tags --prune --no-progress", 60_000);
            
            if (fetchRes.code != 0)
            {
                // 失败回退 (Slow Path)：如果极速拉取失败（可能是本地完全没这个分支记录），则执行全量拉取。
                Step($"⚠️ 极速拉取失败 ({fetchRes.stderr?.Trim()}), 正在尝试全量拉取...");
                // 全量拉取时间较长，给 3 分钟超时
                RunGit(repoPath, $"fetch --all --tags --prune --no-progress", 180_000);
            }

            // 2. 本地修改处理 (Stash 或 强制清理)
            bool stashed = false;
            if (useStash)
            {
                if (HasLocalChanges(repoPath))
                {
                    Step($"> stash push...");
                    var (cs, ss, es) = RunGit(repoPath, "stash push -u -m \"GitBranchSwitcher-auto\"", 120_000);
                    if (cs != 0) return (false, log.AppendLine($"❌ Stash失败(无法保存修改): {es}").ToString());
                    stashed = true;
                }
            }
            else
            {
                // 强制模式：丢弃一切
                Step($"> 强制模式: reset --hard & clean");
                RunGit(repoPath, "reset --hard", 60_000);
                RunGit(repoPath, "clean -fd", 60_000);
            }

            // 3. 检查与切换
            // 先看本地有没有这个分支
            bool localExists = RunGit(repoPath, $"show-ref --verify --quiet refs/heads/{targetBranch}", 20_000).code == 0;
            if (localExists)
            {
                Step($"> checkout -f \"{targetBranch}\"");
                var (c1, s1, e1) = RunGit(repoPath, $"checkout -f \"{targetBranch}\"", 90_000);
                if (c1 != 0) return (false, log.AppendLine($"checkout 失败: {e1}").ToString());
            }
            else
            {
                // 检查远程 (经过上面的 fetch，应该有了)
                bool remoteExists = RunGit(repoPath, $"show-ref --verify --quiet refs/remotes/origin/{targetBranch}", 20_000).code == 0;
                
                if (!remoteExists) 
                    return (false, log.AppendLine($"❌ 分支不存在: {targetBranch} (本地/远程均未找到)").ToString());

                // 再次确保干净 (如果是强制模式)
                if (!useStash) { RunGit(repoPath, "reset --hard", 60_000); RunGit(repoPath, "clean -fd", 60_000); }

                Step($"> checkout -B (new track)");
                var (c2, s2, e2) = RunGit(repoPath, $"checkout -B \"{targetBranch}\" \"origin/{targetBranch}\"", 120_000);
                if (c2 != 0) return (false, log.AppendLine($"创建分支失败: {e2}").ToString());
            }

            // 4. Pull
            // 只要不是刚创建的分支，都需要 pull 一下以防万一。使用 ff-only 保证不产生 merge commit
            Step($"> pull --ff-only");
            var (c3, s3, e3) = RunGit(repoPath, "pull --ff-only --no-progress", 120_000);
            if (c3 != 0) log.AppendLine($"⚠️ Pull警告: {e3} (但分支已切换)");

            // 5. Stash Pop
            if (useStash && stashed)
            {
                Step($"> stash pop");
                var (cp, sp, ep) = RunGit(repoPath, "stash pop --index", 180_000);
                if (cp != 0)
                {
                    log.AppendLine($"⚠️ Stash Pop 冲突: 请手动处理冲突。");
                    return (false, log.ToString());
                }
            }

            return (true, log.AppendLine($"OK").ToString());
        }

        // 执行 Git 命令的底层封装
        public static (int code, string stdout, string stderr) RunGit(string workingDir, string args, int timeoutMs = 120000)
        {
            var stdoutSb = new StringBuilder();
            var stderrSb = new StringBuilder();

            // [重要] 解决中文路径乱码 + 禁用交互式弹窗
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
            
            // 禁用 Git 终端提示
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GCM_INTERACTIVE"] = "Never";
            psi.Environment["GIT_ASKPASS"] = "echo"; // 暴力禁用

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

                if (!p.WaitForExit(timeoutMs)) {
                    try { p.Kill(true); } catch { }
                    return (-2, stdoutSb.ToString(), $"超时(>{timeoutMs}ms)");
                }

                outWait.WaitOne(5000);
                errWait.WaitOne(5000);

                return (p.ExitCode, stdoutSb.ToString(), stderrSb.ToString());
            } catch (Exception ex) {
                return (-3, "", ex.Message);
            }
        }
    }
}