using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GitBranchSwitcher
{
    public static class CollectionService
    {
        /// <summary>
        /// 加载指定玩家的藏品列表
        /// </summary>
        /// <param name="rootPath">共享根目录 (例如 \\Server\Share)</param>
        /// <param name="playerName">用户名</param>
        public static List<string> Load(string rootPath, string playerName)
        {
            try
            {
                string collectDir = Path.Combine(rootPath, "Collect");
                if (!Directory.Exists(collectDir))
                {
                    try { Directory.CreateDirectory(collectDir); } catch { }
                }

                string filePath = Path.Combine(collectDir, $"{playerName}.json");
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    return list ?? new List<string>();
                }
            }
            catch { }
            
            return new List<string>();
        }

        /// <summary>
        /// 保存指定玩家的藏品列表
        /// </summary>
        public static void Save(string rootPath, string playerName, List<string> items)
        {
            try
            {
                string collectDir = Path.Combine(rootPath, "Collect");
                if (!Directory.Exists(collectDir))
                {
                    Directory.CreateDirectory(collectDir);
                }

                string filePath = Path.Combine(collectDir, $"{playerName}.json");
                string json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch { }
        }
    }
}