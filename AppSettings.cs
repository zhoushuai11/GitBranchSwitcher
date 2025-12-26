using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GitBranchSwitcher {
    // 子仓库信息结构
    public class SubRepoItem {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
    }

    // 父节点缓存结构
    public class ParentRepoCache {
        public string ParentPath { get; set; } = "";
        public List<SubRepoItem> Children { get; set; } = new List<SubRepoItem>();
    }

    public class AppSettings {
        public bool StashOnSwitch { get; set; } = true;
        public bool FastMode { get; set; } = false;
        public bool ConfirmOnSwitch { get; set; } = false;
        public int MaxParallel { get; set; } = 16;

        public List<string> ParentPaths { get; set; } = new List<string>();
        public List<ParentRepoCache> RepositoryCache { get; set; } = new List<ParentRepoCache>();
        public List<string> CachedBranchList { get; set; } = new List<string>();

        // 图库根目录名称
        public string PostcardLibraryRoot { get; set; } = "Img";

        // [修改] 移除了 CollectedPostcards，改为由 CollectionService 独立管理

        public DateTime LastStatDate { get; set; } = DateTime.MinValue;
        public int TodaySwitchCount { get; set; } = 0;
        public double TodayTotalSeconds { get; set; } = 0;
        public string LeaderboardPath { get; set; } = @"\\SS-ZHOUSHUAI\GitRankData\rank.json";
        public string UpdateSourcePath { get; set; } = @"\\SS-ZHOUSHUAI\GitRankData";

        private static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitBranchSwitcher");
        private static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

        public static AppSettings Load() {
            AppSettings s = new AppSettings();
            try {
                if (File.Exists(SettingsFile)) {
                    var json = File.ReadAllText(SettingsFile);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                        s = loaded;
                }
            } catch {
            }

            if (s.MaxParallel < 16)
                s.MaxParallel = 16;
            if (string.IsNullOrWhiteSpace(s.LeaderboardPath))
                s.LeaderboardPath = @"\\SS-ZHOUSHUAI\GitRankData\rank.json";
            if (s.RepositoryCache == null)
                s.RepositoryCache = new List<ParentRepoCache>();
            if (s.CachedBranchList == null)
                s.CachedBranchList = new List<string>();
            if (string.IsNullOrWhiteSpace(s.UpdateSourcePath))
                s.UpdateSourcePath = @"\\SS-ZHOUSHUAI\GitRankData\";
            if (string.IsNullOrWhiteSpace(s.PostcardLibraryRoot))
                s.PostcardLibraryRoot = "Img";

            s.CheckDateReset();
            return s;
        }

        public void Save() {
            try {
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
}