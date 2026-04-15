using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GitBranchSwitcher {
    public class SubRepoItem {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
    }

    public class ParentRepoCache {
        public string ParentPath { get; set; } = "";
        public List<SubRepoItem> Children { get; set; } = new List<SubRepoItem>();
    }

    public class SlimRecord {
        public DateTime LastRunAt { get; set; }
        public long BeforeBytes { get; set; }
        public long AfterBytes { get; set; }
        public long SavedBytes { get; set; }
    }

    public class SlimLogEntry {
        public DateTime RunAt { get; set; }
        public string RepoName { get; set; } = "";
        public string RepoPath { get; set; } = "";
        public long BeforeBytes { get; set; }
        public long AfterBytes { get; set; }
        public long SavedBytes { get; set; }
        public bool Success { get; set; }
    }

    public class AppSettings {
        public bool StashOnSwitch { get; set; } = true;
        public bool ReapplyStashOnSwitch { get; set; } = true;
        public bool FastMode { get; set; } = false;
        public bool ConfirmOnSwitch { get; set; } = false;
        public int MaxParallel { get; set; } = 16;
        public bool EnableGitOperationTimeout { get; set; } = false;
        public int GitOperationTimeoutSeconds { get; set; } = 300;
        public bool DarkMode { get; set; } = false;
        public int AutoSyncIntervalMinutes { get; set; } = 0; // 0 = 禁用自动同步
        public int AutoSyncIntervalSeconds { get; set; } = 0; // 0-59 附加秒数

        // 瘦身参数
        public int GcThreads { get; set; } = 2;          // pack.threads
        public int GcWindowMemoryMB { get; set; } = 256; // pack.windowMemory (MB)
        public int GcTimeoutHours { get; set; } = 3;     // -1 = 不限制
        // 键盘快捷键（存 System.Windows.Forms.Keys 枚举的 int 值）
        public int ShortcutFetchKey  { get; set; } = 116;  // Keys.F5
        public int ShortcutSwitchKey { get; set; } = 13;   // Keys.Return
        public int ShortcutFillKey   { get; set; } = 81;   // Keys.Q

        public List<string> ParentPaths { get; set; } = new List<string>();
        public List<ParentRepoCache> RepositoryCache { get; set; } = new List<ParentRepoCache>();
        public List<string> CachedBranchList { get; set; } = new List<string>();
        
        public List<FavoriteItem> FavoriteBranches { get; set; } = new List<FavoriteItem>();

        // key = 仓库完整路径，value = 最近一次瘦身记录
        public Dictionary<string, SlimRecord> SlimHistory { get; set; } = new Dictionary<string, SlimRecord>();

        // 全量历史日志，保留最近 500 条
        public List<SlimLogEntry> SlimLog { get; set; } = new List<SlimLogEntry>();
        
        // [修改] 路径配置：更新为新的共享地址
        public string LeaderboardPath { get; set; } = @"\\s4.biubiubiu.io\share\rank.json";

        // 这是一个基础路径，用于推导 Img 和 Collect 目录
        public string UpdateSourcePath { get; set; } = @"\\s4.biubiubiu.io\share";
        public string FrameWorkImgPath { get; set; } = @"\\s4.biubiubiu.io\share\FrameWork";
        
        public string SelectedTheme { get; set; } = "";
        public string SelectedCollectionItem { get; set; } = "Random";
        public DateTime LastStatDate { get; set; } = DateTime.MinValue;
        public int TodaySwitchCount { get; set; } = 0;
        public double TodayTotalSeconds { get; set; } = 0;

        private static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitBranchSwitcher");
        private static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

        public static AppSettings Load() {
            AppSettings s = new AppSettings();
            try {
                // 尝试加载本地缓存
                if (File.Exists(SettingsFile)) {
                    var json = File.ReadAllText(SettingsFile);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                        s = loaded;
                }
            } catch {
                // 加载失败则使用默认值
            }

            if (s.MaxParallel < 16)
                s.MaxParallel = 16;
            if (s.GitOperationTimeoutSeconds <= 0)
                s.GitOperationTimeoutSeconds = 300;

            // [核心修改] 强制使用新路径，忽略缓存文件中的旧路径
            // 无论之前 settings.json 里存了什么旧的 \\SS-ZHOUSHUAI 路径，这里都会被覆盖
            s.LeaderboardPath = @"\\s4.biubiubiu.io\share\rank.json";
            s.UpdateSourcePath = @"\\s4.biubiubiu.io\share";
            s.FrameWorkImgPath = @"\\s4.biubiubiu.io\share\FrameWork";

            // 初始化集合防止空引用
            if (s.RepositoryCache == null)
                s.RepositoryCache = new List<ParentRepoCache>();
            if (s.CachedBranchList == null)
                s.CachedBranchList = new List<string>();
            if (s.ParentPaths == null)
                s.ParentPaths = new List<string>();
            if (s.SlimHistory == null)
                s.SlimHistory = new Dictionary<string, SlimRecord>();
            if (s.SlimLog == null)
                s.SlimLog = new List<SlimLogEntry>();

            s.CheckDateReset();
            return s;
        }

        public void Save() {
            try {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);
                    
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions {
                    WriteIndented = true
                }));
            } catch {
            }
        }

        public void CheckDateReset() {
            if (LastStatDate.Date != DateTime.Now.Date) {
                LastStatDate = DateTime.Now.Date;
                TodaySwitchCount = 0;
                TodayTotalSeconds = 0;
            }
        }
    }
    
    // [新增] 用于存储单条收藏记录的类
    public class FavoriteItem
    {
        public string Branch { get; set; } = ""; // 分支名称
        public string Remark { get; set; } = ""; // 备注信息
    }
}
