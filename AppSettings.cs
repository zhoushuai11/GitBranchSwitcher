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

    public class AppSettings {
        public bool StashOnSwitch { get; set; } = true;
        public bool FastMode { get; set; } = false;
        public bool ConfirmOnSwitch { get; set; } = false;
        public int MaxParallel { get; set; } = 16;

        public List<string> ParentPaths { get; set; } = new List<string>();
        public List<ParentRepoCache> RepositoryCache { get; set; } = new List<ParentRepoCache>();
        public List<string> CachedBranchList { get; set; } = new List<string>();

        // [修改] 路径配置
        public string LeaderboardPath { get; set; } = @"\\SS-ZHOUSHUAI\GitRankData\rank.json";

        // 这是一个基础路径，用于推导 Img 和 Collect 目录
        public string UpdateSourcePath { get; set; } = @"\\SS-ZHOUSHUAI\GitRankData";
        public string FrameWorkImgPath { get; set; } = @"\\SS-ZHOUSHUAI\GitRankData\FrameWork";
        public string SelectedTheme { get; set; } = "";
        public string SelectedCollectionItem { get; set; } = "Random";
        public bool IsDarkMode { get; set; } = false;

        public DateTime LastStatDate { get; set; } = DateTime.MinValue;
        public int TodaySwitchCount { get; set; } = 0;
        public double TodayTotalSeconds { get; set; } = 0;

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

            // [修改] 强制默认值，确保路径正确
            if (string.IsNullOrWhiteSpace(s.LeaderboardPath))
                s.LeaderboardPath = @"\\SS-ZHOUSHUAI\GitRankData\rank.json";
            if (string.IsNullOrWhiteSpace(s.UpdateSourcePath))
                s.UpdateSourcePath = @"\\SS-ZHOUSHUAI\GitRankData";

            if (s.RepositoryCache == null)
                s.RepositoryCache = new List<ParentRepoCache>();
            if (s.CachedBranchList == null)
                s.CachedBranchList = new List<string>();

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