using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GitBranchSwitcher {
    public class UserStat {
        public string Name { get; set; } = "";
        public int TotalSwitches { get; set; } = 0;
        public double TotalDuration { get; set; } = 0;
        public long TotalSpaceCleaned { get; set; } = 0;

        // 累计收集卡片数
        public int TotalCardsCollected { get; set; } = 0;
        public DateTime LastActive { get; set; }
    }

    public static class LeaderboardService {
        private static string _sharedFilePath = "";
        private static List<UserStat>? _cachedList = null;
        private static DateTime _lastFetchTime = DateTime.MinValue;

        public static void SetPath(string path) => _sharedFilePath = path;

        private static readonly Random _jitter = new Random();

        // [修改] 参数3改为 nullable int，代表“当前藏品总数”
        // 如果传入 null，则不修改藏品数；如果传入数字，则直接覆盖更新
        public static async Task<(int totalCount, double totalTime, long totalSpace)> UploadMyScoreAsync(double durationAdd, long spaceAdd, int? currentTotalCards = null) {
            if (string.IsNullOrEmpty(_sharedFilePath))
                return (0, 0, 0);

            return await Task.Run(async () => {
                int retry = 0;
                while (retry < 5) {
                    try {
                        FileStream fs = null;
                        try {
                            if (!File.Exists(_sharedFilePath)) {
                                try {
                                    var d = Path.GetDirectoryName(_sharedFilePath);
                                    if (!string.IsNullOrEmpty(d))
                                        Directory.CreateDirectory(d);
                                    File.WriteAllText(_sharedFilePath, "[]");
                                } catch {
                                }
                            }

                            fs = new FileStream(_sharedFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                            using var reader = new StreamReader(fs, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true);
                            var text = await reader.ReadToEndAsync();
                            var list = string.IsNullOrWhiteSpace(text)? new List<UserStat>() : (JsonSerializer.Deserialize<List<UserStat>>(text) ?? new List<UserStat>());

                            var me = list.FirstOrDefault(x => x.Name == Environment.UserName);
                            if (me == null) {
                                me = new UserStat {
                                    Name = Environment.UserName
                                };
                                list.Add(me);
                            }

                            // 更新数据
                            if (durationAdd > 0) {
                                me.TotalSwitches++;
                                me.TotalDuration += durationAdd;
                            }

                            me.TotalSpaceCleaned += spaceAdd;

                            // [关键修改] 如果传入了具体的藏品数量，直接赋值（全量同步），防止计数偏差
                            if (currentTotalCards.HasValue) {
                                me.TotalCardsCollected = currentTotalCards.Value;
                            }

                            me.LastActive = DateTime.Now;

                            fs.Position = 0;
                            fs.SetLength(0);
                            using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8);
                            await writer.WriteAsync(JsonSerializer.Serialize(list, new JsonSerializerOptions {
                                WriteIndented = true
                            }));

                            _cachedList = list;
                            _lastFetchTime = DateTime.Now;

                            return (me.TotalSwitches, me.TotalDuration, me.TotalSpaceCleaned);
                        } finally {
                            fs?.Dispose();
                        }
                    } catch (IOException) {
                        retry++;
                        await Task.Delay(_jitter.Next(100, 500));
                    } catch {
                        break;
                    }
                }

                return (0, 0, 0);
            });
        }

        public static async Task<List<UserStat>> GetLeaderboardAsync() {
            if (_cachedList != null && (DateTime.Now - _lastFetchTime).TotalSeconds < 60)
                return _cachedList;

            if (string.IsNullOrEmpty(_sharedFilePath))
                return new List<UserStat>();

            return await Task.Run(() => {
                try {
                    if (!File.Exists(_sharedFilePath))
                        return new List<UserStat>();
                    using var fs = new FileStream(_sharedFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    var text = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(text))
                        return new List<UserStat>();
                    var list = JsonSerializer.Deserialize<List<UserStat>>(text) ?? new List<UserStat>();
                    _cachedList = list;
                    _lastFetchTime = DateTime.Now;
                    return list;
                } catch {
                    return new List<UserStat>();
                }
            });
        }

        public static async Task<(int, double, long)> GetMyStatsAsync() {
            var list = await GetLeaderboardAsync();
            var me = list.FirstOrDefault(u => u.Name == Environment.UserName);
            if (me != null)
                return (me.TotalSwitches, me.TotalDuration, me.TotalSpaceCleaned);
            return (0, 0, 0);
        }
    }
}