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
        
        public List<FavoriteItem> FavoriteBranches { get; set; } = new List<FavoriteItem>();
        
        // [修改] 路径配置：更新为新的共享地址
        public string LeaderboardPath { get; set; } = @"\\s4.biubiubiu.io\share\rank.json";

        // 这是一个基础路径，用于推导 Img 和 Collect 目录
        public string UpdateSourcePath { get; set; } = @"\\s4.biubiubiu.io\share";
        public string FrameWorkImgPath { get; set; } = @"\\s4.biubiubiu.io\share\FrameWork";
        
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