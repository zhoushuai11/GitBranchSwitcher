using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GitBranchSwitcher
{
    public static class CollectionService
    {
        // 藏品存放目录：程序运行目录/Collect
        private static string CollectDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Collect");

        /// <summary>
        /// 加载指定玩家的藏品列表
        /// </summary>
        public static List<string> Load(string playerName)
        {
            try
            {
                if (!Directory.Exists(CollectDir))
                {
                    Directory.CreateDirectory(CollectDir);
                }

                string filePath = Path.Combine(CollectDir, $"{playerName}.json");
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
        public static void Save(string playerName, List<string> items)
        {
            try
            {
                if (!Directory.Exists(CollectDir))
                {
                    Directory.CreateDirectory(CollectDir);
                }

                string filePath = Path.Combine(CollectDir, $"{playerName}.json");
                string json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch { }
        }
    }
}