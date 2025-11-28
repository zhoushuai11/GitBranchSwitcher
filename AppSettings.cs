using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; // 必须引用
using System.Text.Json;

namespace GitBranchSwitcher
{
    public class AppSettings
    {
        public bool StashOnSwitch { get; set; } = true;
        public bool FastMode { get; set; } = false;
        public int MaxParallel { get; set; } = 16;
        public List<string> ParentPaths { get; set; } = new List<string>();

        // 默认列表
        public List<string> SubDirectoriesToScan { get; set; } = new List<string>
        {
            "", // 根目录
            "Assets/ToBundle",
            "Assets/Script",            // 确保这里有
            "Assets/Script/Biubiubiu2", 
            "Assets/Art",
            "Assets/Scenes",
            "Library/ConfigCache",
            "Assets/Audio"
        };

        public string DirNotStarted { get; set; } = "";
        public string DirSwitching { get; set; } = "";
        public string DirDone { get; set; } = "";
        public string DirFlash { get; set; } = "";

        private static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitBranchSwitcher");
        private static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

        public static AppSettings Load()
        {
            try {
                if (File.Exists(SettingsFile)) {
                    var json = File.ReadAllText(SettingsFile);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null) {
                        // [关键修复] 即使读取了旧配置，也强制检查 Assets/Script 是否存在
                        // 如果不存在，说明是旧配置缓存，强制加上！
                        if (s.SubDirectoriesToScan == null) s.SubDirectoriesToScan = new List<string>();
                        
                        var requiredPaths = new[] { "Assets/Script", "Assets/Script/Biubiubiu2" };
                        bool changed = false;
                        foreach (var req in requiredPaths)
                        {
                            // 不区分大小写检查是否存在
                            if (!s.SubDirectoriesToScan.Any(x => string.Equals(x, req, StringComparison.OrdinalIgnoreCase)))
                            {
                                s.SubDirectoriesToScan.Add(req);
                                changed = true;
                            }
                        }

                        // 强制更新并发数
                        if (s.MaxParallel < 16) s.MaxParallel = 16;
                        
                        // 如果刚才补全了路径，顺便保存一下，方便下次
                        if (changed) s.Save();

                        return s;
                    }
                }
            } catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            } catch { }
        }
    }
}