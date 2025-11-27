using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GitBranchSwitcher
{
    public class AppSettings
    {
        public bool StashOnSwitch { get; set; } = true;
        public int MaxParallel { get; set; } = 16;
        public List<string> ParentPaths { get; set; } = new List<string>();

        // [更新] 新的目录列表
        public List<string> SubDirectoriesToScan { get; set; } = new List<string>
        {
            "", // 根目录
            "Assets/ToBundle",
            "Assets/Script",            // [Script 仓] 确保这个文件夹里有 .git
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
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile));
                    if (s != null) {
                        // 强制更新列表 (如果旧配置存在，覆盖它以应用新路径)
                        // 注意：如果你有自定义修改过本地配置，这一步会覆盖。
                        // 这里为了确保 Script 仓生效，我暂时覆盖了。
                        s.SubDirectoriesToScan = new List<string> {
                             "", "Assets/ToBundle", "Assets/Script", "Assets/Script/Biubiubiu2",
                             "Assets/Art", "Assets/Scenes", "Library/ConfigCache", "Assets/Audio"
                        };
                        if (s.MaxParallel < 8) s.MaxParallel = 16;
                        return s;
                    }
                }
            } catch { }
            return new AppSettings();
        }
        public void Save() { try { Directory.CreateDirectory(SettingsDir); File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); } catch { } }
    }
}