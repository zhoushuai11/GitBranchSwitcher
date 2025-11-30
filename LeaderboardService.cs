using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GitBranchSwitcher
{
    public class UserStat
    {
        public string Name { get; set; } = "";
        public int TotalSwitches { get; set; } = 0;     // 累计次数
        public double TotalDuration { get; set; } = 0;  // 累计时长(秒)
        public DateTime LastActive { get; set; }
    }

    public static class LeaderboardService
    {
        private static string _sharedFilePath = ""; 

        public static void SetPath(string path)
        {
            _sharedFilePath = path;
        }

        // [关键修改] 返回值必须是 Task<(int, double)>，否则 MainForm 会报错
        public static async Task<(int totalCount, double totalTime)> UploadMyScoreAsync(double durationSeconds)
        {
            if (string.IsNullOrEmpty(_sharedFilePath)) return (0, 0);

            return await Task.Run(() =>
            {
                string myName = Environment.UserName; 
                int finalCount = 0;
                double finalTime = 0;

                // 重试 5 次防止文件被别人锁住
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        var data = ReadAndLock(out var fileStream);
                        using (fileStream) 
                        {
                            var me = data.FirstOrDefault(u => u.Name == myName);
                            if (me == null)
                            {
                                me = new UserStat { Name = myName, TotalSwitches = 0, TotalDuration = 0 };
                                data.Add(me);
                            }
                            
                            // 累加数据
                            me.TotalSwitches++;
                            me.TotalDuration += durationSeconds;
                            me.LastActive = DateTime.Now;

                            // [关键] 记录最新值用于返回给界面
                            finalCount = me.TotalSwitches;
                            finalTime = me.TotalDuration;

                            // 写入文件
                            fileStream.SetLength(0);
                            using var writer = new StreamWriter(fileStream);
                            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                            writer.Write(json);
                        }
                        
                        // 成功，返回最新数据
                        return (finalCount, finalTime); 
                    }
                    catch (IOException) 
                    { 
                        // 文件被锁，稍等重试
                        Thread.Sleep(200); 
                    }
                    catch 
                    { 
                        // 其他错误直接退出
                        return (0, 0); 
                    }
                }
                return (0, 0);
            });
        }

        // 获取排行榜列表
        public static async Task<List<UserStat>> GetLeaderboardAsync()
        {
            if (string.IsNullOrEmpty(_sharedFilePath) || !File.Exists(_sharedFilePath))
                return new List<UserStat>();

            return await Task.Run(() =>
            {
                try
                {
                    using var fs = new FileStream(_sharedFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);
                    var text = sr.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(text)) return new List<UserStat>();
                    return JsonSerializer.Deserialize<List<UserStat>>(text) ?? new List<UserStat>();
                }
                catch { return new List<UserStat>(); }
            });
        }

        // 获取自己的数据（用于初始化）
        public static async Task<(int, double)> GetMyStatsAsync()
        {
            var list = await GetLeaderboardAsync();
            var me = list.FirstOrDefault(u => u.Name == Environment.UserName);
            if (me != null) return (me.TotalSwitches, me.TotalDuration);
            return (0, 0);
        }

        // 辅助：读取并加锁
        private static List<UserStat> ReadAndLock(out FileStream fs)
        {
            if (!File.Exists(_sharedFilePath))
            {
                try {
                    var dir = Path.GetDirectoryName(_sharedFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(_sharedFilePath, "[]");
                } catch { }
            }
            
            // FileShare.None 表示独占访问
            fs = new FileStream(_sharedFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            
            using var reader = new StreamReader(fs, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true);
            var text = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(text)) return new List<UserStat>();
            return JsonSerializer.Deserialize<List<UserStat>>(text) ?? new List<UserStat>();
        }
    }
}