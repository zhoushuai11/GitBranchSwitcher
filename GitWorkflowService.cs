using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace GitBranchSwitcher
{
    // 定义进度报告的数据结构
    public class RepoSwitchResult
    {
        public GitRepo Repo { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public double DurationSeconds { get; set; }
        public int ProgressIndex { get; set; } // 当前是第几个
        public int TotalCount { get; set; }    // 总数
    }

    public class RepoSwitchLogEntry
    {
        public GitRepo Repo { get; set; }
        public string Message { get; set; }
    }

    public class RepoLockRecoveryRequest
    {
        public GitRepo Repo { get; set; }
        public string Command { get; set; }
        public string Error { get; set; }
    }

    public class GitWorkflowService
    {
        private readonly int _maxParallel;

        public GitWorkflowService(int maxParallel = 16)
        {
            _maxParallel = maxParallel;
        }

        public async Task<double> SwitchReposAsync(
            List<GitRepo> repos, 
            string targetBranch, 
            bool useStash, 
            bool fastMode, 
            bool enableOperationTimeout,
            int operationTimeoutSeconds,
            IProgress<RepoSwitchResult> progress,
            IProgress<RepoSwitchLogEntry>? logProgress = null,
            Func<RepoLockRecoveryRequest, bool>? confirmLockRecovery = null)
        {
            using var sem = new SemaphoreSlim(_maxParallel);
            var tasks = new List<Task>();
            int finishedCount = 0;
            var batchSw = Stopwatch.StartNew();

            foreach (var repo in repos)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await sem.WaitAsync();
                    var sw = Stopwatch.StartNew();
                    bool ok = false;
                    string msg = "";
                    try
                    {
                        var res = GitHelper.SwitchAndPull(
                            repo.Path,
                            targetBranch,
                            useStash,
                            fastMode,
                            enableOperationTimeout,
                            operationTimeoutSeconds,
                            (command, error) => confirmLockRecovery?.Invoke(new RepoLockRecoveryRequest
                            {
                                Repo = repo,
                                Command = command,
                                Error = error
                            }) ?? false,
                            line => logProgress?.Report(new RepoSwitchLogEntry
                            {
                                Repo = repo,
                                Message = line
                            }));
                        ok = res.ok;
                        msg = res.message;
                        repo.SwitchOk = ok;
                        repo.LastMessage = msg;
                        if (ok)
                            repo.CurrentBranch = targetBranch;
                        repo.IsSyncChecked = false;
                    }
                    catch (Exception ex)
                    {
                        ok = false;
                        msg = ex.Message;
                        logProgress?.Report(new RepoSwitchLogEntry
                        {
                            Repo = repo,
                            Message = ex.Message
                        });
                    }
                    finally
                    {
                        sw.Stop();
                        sem.Release();
                        
                        // 线程安全地增加计数
                        int currentDone = Interlocked.Increment(ref finishedCount);

                        // 报告进度
                        progress?.Report(new RepoSwitchResult
                        {
                            Repo = repo,
                            Success = ok,
                            Message = msg,
                            DurationSeconds = sw.Elapsed.TotalSeconds,
                            ProgressIndex = currentDone,
                            TotalCount = repos.Count
                        });
                    }
                }));
            }

            await Task.WhenAll(tasks);
            batchSw.Stop();
            return batchSw.Elapsed.TotalSeconds;
        }
    }
}
