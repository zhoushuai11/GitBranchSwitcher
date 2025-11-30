using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GitBranchSwitcher
{
    // [新增] 单个仓库信息
    public class SubRepoItem
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
    }

    // [新增] 父节点缓存单元：一个父目录 -> 多个子仓库
    public class ParentRepoCache
    {
        public string ParentPath { get; set; } = "";
        public List<SubRepoItem> Children { get; set; } = new List<SubRepoItem>();
    }

    public class AppSettings
    {
        public bool StashOnSwitch { get; set; } = true;
        public bool FastMode { get; set; } = false;
        public int MaxParallel { get; set; } = 16;
        
        public List<string> ParentPaths { get; set; } = new List<string>();

        // [修改] 结构化缓存：保存每个父目录下扫描到了哪些子仓库
        public List<ParentRepoCache> RepositoryCache { get; set; } = new List<ParentRepoCache>();

        // 废弃旧缓存字段
        // public List<RepoCacheItem> CachedRepos { get; set; } 

        // 统计与配置
        public DateTime LastStatDate { get; set; } = DateTime.MinValue; 
        public int TodaySwitchCount { get; set; } = 0;                  
        public double TodayTotalSeconds { get; set; } = 0;              
        public string LeaderboardPath { get; set; } = @"\\SS-ZHOUSHUAI\GitRankData\rank.json"; 

        // 分支列表缓存
        public List<string> CachedBranchList { get; set; } = new List<string>();

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
            
            // 初始化集合防止空引用
            if (s.RepositoryCache == null) s.RepositoryCache = new List<ParentRepoCache>();
            if (s.CachedBranchList == null) s.CachedBranchList = new List<string>();

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