using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GitBranchSwitcher
{
    public static class StatusImageService
    {
        private static readonly Dictionary<string, List<string>> _embedded = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new();

        static StatusImageService()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var names = asm.GetManifestResourceNames() ?? Array.Empty<string>();
                foreach (var n in names)
                {
                    var low = n.ToLowerInvariant();
                    var idx = low.IndexOf(".assets.");
                    if (idx < 0) continue;
                    var rest = low.Substring(idx + ".assets.".Length);
                    var dot = rest.IndexOf('.');
                    if (dot < 0) continue;
                    var dir = rest.Substring(0, dot); // e.g., state_idle
                    lock (_lock)
                    {
                        if (!_embedded.TryGetValue(dir, out var list))
                            _embedded[dir] = list = new List<string>();
                        list.Add(n);
                    }
                }
            }
            catch { }
        }

        private static IEnumerable<string> KeyCandidates(string key)
        {
            var k = (key ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(k)) yield break;
            yield return k;
            if (!k.StartsWith("state_")) yield return "state_" + k;
            if (k is "idle") yield return "state_idle";
            if (k is "working" or "busy" or "progress") yield return "state_working";
            if (k is "success" or "ok" or "done") yield return "state_success";
            if (k is "error" or "fail" or "failed") yield return "state_error";
        }

        public static Image? GetRandom(string key)
        {
            foreach (var cand in KeyCandidates(key))
            {
                var img = GetFromEmbedded(cand);
                if (img != null) return img;
                img = GetFromDisk(cand);
                if (img != null) return img;
            }
            return null;
        }

        private static Image? GetFromEmbedded(string key)
        {
            try
            {
                if (_embedded.TryGetValue(key, out var list) && list.Count > 0)
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var rnd = new Random();
                    var pick = list[rnd.Next(list.Count)];
                    using var s = asm.GetManifestResourceStream(pick);
                    if (s == null) return null;
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    ms.Position = 0;
                    using var tmp = Image.FromStream(ms);
                    return new Bitmap(tmp);
                }
            }
            catch { }
            return null;
        }

        private static Image? GetFromDisk(string key)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] dirs = new string[] {
                    Path.Combine(baseDir, "Assets", key),
                    (!key.StartsWith("state_") ? Path.Combine(baseDir, "Assets", "state_" + key) : "")
                }.Where(d => !string.IsNullOrEmpty(d)).ToArray();

                foreach (var dir in dirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    var files = Directory.GetFiles(dir, "*.*")
                        .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (files.Length == 0) continue;
                    var rnd = new Random();
                    return Image.FromFile(files[rnd.Next(files.Length)]);
                }
            }
            catch { }
            return null;
        }

        public static void DebugDump(string filePath)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var names = asm.GetManifestResourceNames() ?? Array.Empty<string>();
                using var sw = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
                sw.WriteLine($"[Manifest Resources] count={names.Length}");
                foreach (var n in names) sw.WriteLine(n);
                sw.WriteLine("\n[Embedded keys]");
                foreach (var kv in _embedded) sw.WriteLine($"{kv.Key} -> {kv.Value.Count} item(s)");
            }
            catch { }
        }
    }
}
