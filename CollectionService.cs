using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GitBranchSwitcher
{
    // [新增] 藏品数据模型
    public class CollectedItem
    {
        public string FileName { get; set; } = "";
        public string Rarity { get; set; } = "N"; // 稀有度
        public int Score { get; set; } = 1;       // 单卡分数
        public DateTime CollectTime { get; set; } = DateTime.Now;
    }

    public static class CollectionService
    {
        /// <summary>
        /// 加载指定玩家的藏品列表
        /// </summary>
        public static List<CollectedItem> Load(string rootPath, string playerName)
        {
            try
            {
                string collectDir = Path.Combine(rootPath, "Collect");
                if (!Directory.Exists(collectDir)) try { Directory.CreateDirectory(collectDir); } catch { }

                string filePath = Path.Combine(collectDir, $"{playerName}.json");
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    try 
                    {
                        // 1. 尝试读取新格式 (List<CollectedItem>)
                        return JsonSerializer.Deserialize<List<CollectedItem>>(json) ?? new List<CollectedItem>();
                    }
                    catch 
                    {
                        // 2. 兼容旧格式 (List<string>)，防止报错
                        try 
                        {
                            var oldList = JsonSerializer.Deserialize<List<string>>(json);
                            var newList = new List<CollectedItem>();
                            if (oldList != null)
                            {
                                // 旧数据默认视为 N 卡，分数 1
                                foreach (var f in oldList) 
                                    newList.Add(new CollectedItem { FileName = f, Rarity = "N", Score = 1 });
                            }
                            return newList;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            
            return new List<CollectedItem>();
        }

        /// <summary>
        /// 保存指定玩家的藏品列表
        /// </summary>
        public static void Save(string rootPath, string playerName, List<CollectedItem> items)
        {
            try
            {
                string collectDir = Path.Combine(rootPath, "Collect");
                if (!Directory.Exists(collectDir)) Directory.CreateDirectory(collectDir);

                string filePath = Path.Combine(collectDir, $"{playerName}.json");
                string json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch { }
        }
    }
}