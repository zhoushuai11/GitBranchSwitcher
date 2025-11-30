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
        public int TotalSwitches { get; set; } = 0;     
        public double TotalDuration { get; set; } = 0;  
        // [新增] 累计瘦身 (字节)
        public long TotalSpaceCleaned { get; set; } = 0; 
        public DateTime LastActive { get; set; }
    }

    public static class LeaderboardService
    {
        private static string _sharedFilePath = ""; 
        private static List<UserStat>? _cachedList = null;
        private static DateTime _lastFetchTime = DateTime.MinValue;

        public static void SetPath(string path) => _sharedFilePath = path;

        // [修改] 上传数据：增加 cleanedBytes 参数
        // 返回：最新累计值 (次数, 时长, 空间)
        public static async Task<(int totalCount, double totalTime, long totalSpace)> UploadMyScoreAsync(double durationSeconds, long cleanedBytes)
        {
            if (string.IsNullOrEmpty(_sharedFilePath)) return (0, 0, 0);

            return await Task.Run(() =>
            {
                string myName = Environment.UserName; 
                int fCount = 0; double fTime = 0; long fSpace = 0;

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
                                me = new UserStat { Name = myName };
                                data.Add(me);
                            }
                            
                            // 累加
                            if (durationSeconds > 0) {
                                me.TotalSwitches++;
                                me.TotalDuration += durationSeconds;
                            }
                            if (cleanedBytes > 0) {
                                me.TotalSpaceCleaned += cleanedBytes;
                            }
                            
                            me.LastActive = DateTime.Now;

                            // 记录最新值
                            fCount = me.TotalSwitches;
                            fTime = me.TotalDuration;
                            fSpace = me.TotalSpaceCleaned;

                            fileStream.SetLength(0);
                            using var writer = new StreamWriter(fileStream);
                            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                            writer.Write(json);
                        }
                        
                        _cachedList = null; // 清除缓存
                        return (fCount, fTime, fSpace); 
                    }
                    catch (IOException) { Thread.Sleep(200); }
                    catch { return (0, 0, 0); }
                }
                return (0, 0, 0);
            });
        }

        public static async Task<List<UserStat>> GetLeaderboardAsync()
        {
            if (string.IsNullOrEmpty(_sharedFilePath)) return new List<UserStat>();
            if (_cachedList != null && (DateTime.Now - _lastFetchTime).TotalSeconds < 30) return _cachedList;

            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(_sharedFilePath)) return new List<UserStat>();
                    using var fs = new FileStream(_sharedFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);
                    var text = sr.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(text)) return new List<UserStat>();
                    var list = JsonSerializer.Deserialize<List<UserStat>>(text) ?? new List<UserStat>();
                    _cachedList = list; _lastFetchTime = DateTime.Now;
                    return list;
                }
                catch { return new List<UserStat>(); }
            });
        }

        // 获取我的最新数据
        public static async Task<(int, double, long)> GetMyStatsAsync()
        {
            var list = await GetLeaderboardAsync();
            var me = list.FirstOrDefault(u => u.Name == Environment.UserName);
            if (me != null) return (me.TotalSwitches, me.TotalDuration, me.TotalSpaceCleaned);
            return (0, 0, 0);
        }

        private static List<UserStat> ReadAndLock(out FileStream fs)
        {
            if (!File.Exists(_sharedFilePath)) { try { var d=Path.GetDirectoryName(_sharedFilePath); if(!string.IsNullOrEmpty(d))Directory.CreateDirectory(d); File.WriteAllText(_sharedFilePath, "[]"); } catch {} }
            fs = new FileStream(_sharedFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var reader = new StreamReader(fs, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true);
            var text = reader.ReadToEnd();
            return string.IsNullOrWhiteSpace(text) ? new List<UserStat>() : (JsonSerializer.Deserialize<List<UserStat>>(text) ?? new List<UserStat>());
        }
    }
}