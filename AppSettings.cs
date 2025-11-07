using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GitBranchSwitcher
{
    public class AppSettings
    {
        public bool StashOnSwitch { get; set; } = true;
        public int MaxParallel { get; set; } = 4;

    
        public List<string> ParentPaths { get; set; } = new List<string>();

        // 自定义四个随机图片目录（可为空，表示使用 Assets 下默认）
        public string DirNotStarted { get; set; } = "";
        public string DirSwitching { get; set; } = "";
        public string DirDone { get; set; } = "";
        public string DirFlash { get; set; } = "";

        private static string SettingsDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitBranchSwitcher");

        private static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null) return s;
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }
    }
}
