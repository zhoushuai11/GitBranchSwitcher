using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace GitBranchSwitcher
{
    public class RepoSwitchResult
    {
        public GitRepo Repo { get; set; }
        public bool Success { get; set; }
        public bool Warning { get; set; }
        public bool Cancelled { get; set; }
        public string Message { get; set; }
        public string StatusText { get; set; }
        public double DurationSeconds { get; set; }
        public int ProgressIndex { get; set; }
        public int TotalCount { get; set; }
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
            bool reapplyStash,
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
                    bool warning = false;
                    string msg = "";
                    string statusText = "";
                    try
                    {
                        var res = GitHelper.SwitchAndPull(
                            repo.Path,
                            targetBranch,
                            useStash,
                            reapplyStash,
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
                        warning = res.warning;
                        msg = res.message;
                        statusText = res.statusText;
                        repo.SwitchOk = ok;
                        repo.LastMessage = msg;
                        bool cancelled = !ok && GitHelper.IsCancelRequested;
                        repo.SwitchSeverity = cancelled ? RepoSwitchSeverity.Cancelled
                            : warning ? RepoSwitchSeverity.Warning
                            : ok ? RepoSwitchSeverity.None : RepoSwitchSeverity.Error;
                        repo.SwitchStatusText = warning ? statusText : "";
                        if (ok)
                            repo.CurrentBranch = targetBranch;
                        repo.IsSyncChecked = false;
                    }
                    catch (Exception ex)
                    {
                        ok = false;
                        warning = false;
                        msg = ex.Message;
                        statusText = "";
                        repo.SwitchSeverity = RepoSwitchSeverity.Error;
                        repo.SwitchStatusText = "";
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

                        int currentDone = Interlocked.Increment(ref finishedCount);

                        progress?.Report(new RepoSwitchResult
                        {
                            Repo = repo,
                            Success = ok,
                            Warning = warning,
                            Cancelled = !ok && GitHelper.IsCancelRequested,
                            Message = msg,
                            StatusText = statusText,
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
