using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GitBranchSwitcher
{
    // [新增] 简单的缓存结构
    public class RepoCacheItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string ParentName { get; set; } = "";
    }

    public class AppSettings
    {
        public bool StashOnSwitch { get; set; } = true;
        public bool FastMode { get; set; } = false;
        public int MaxParallel { get; set; } = 16;
        public List<string> ParentPaths { get; set; } = new List<string>();

        // [新增] 缓存扫描结果，下次启动直接用
        public List<RepoCacheItem> CachedRepos { get; set; } = new List<RepoCacheItem>();

        // 统计
        public DateTime LastStatDate { get; set; } = DateTime.MinValue; 
        public int TodaySwitchCount { get; set; } = 0;                  
        public double TodayTotalSeconds { get; set; } = 0;              
        public string LeaderboardPath { get; set; } = @"\\SS-ZHOUSHUAI\GitRankData\rank.json"; 

        public string DirNotStarted { get; set; } = "";
        public string DirSwitching { get; set; } = "";
        public string DirDone { get; set; } = "";
        public string DirFlash { get; set; } = "";

        private static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitBranchSwitcher");
        private static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try {
                if (File.Exists(SettingsFile)) {
                    var json = File.ReadAllText(SettingsFile);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null) s = loaded;
                }
            } catch { }

            if (s.MaxParallel < 16) s.MaxParallel = 16;
            if (string.IsNullOrWhiteSpace(s.LeaderboardPath)) s.LeaderboardPath = @"\\SS-ZHOUSHUAI\GitRankData\rank.json";
            if (s.CachedRepos == null) s.CachedRepos = new List<RepoCacheItem>();

            s.CheckDateReset();
            return s;
        }

        public void Save()
        {
            try {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            } catch { }
        }

        public void CheckDateReset()
        {
            if (LastStatDate.Date != DateTime.Now.Date)
            {
                LastStatDate = DateTime.Now.Date;
                TodaySwitchCount = 0;
                TodayTotalSeconds = 0;
            }
        }
    }
}