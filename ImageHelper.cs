using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GitBranchSwitcher
{
    public static class ImageHelper
    {
        private static readonly Dictionary<string, List<string>> _resourceMap = new Dictionary<string, List<string>>();
        private static readonly Random _rnd = new Random();

        static ImageHelper()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var names = asm.GetManifestResourceNames(); 
                
                foreach (var name in names)
                {
                    var lower = name.ToLowerInvariant();
                    // 支持常见图片格式
                    if (!lower.EndsWith(".png") && !lower.EndsWith(".jpg") && !lower.EndsWith(".jpeg") && !lower.EndsWith(".gif") && !lower.EndsWith(".ico"))
                        continue;

                    string key = "default";
                    if (lower.Contains("state_notstarted")) key = "state_notstarted";
                    else if (lower.Contains("state_switching")) key = "state_switching";
                    else if (lower.Contains("state_done")) key = "state_done";
                    else if (lower.Contains("flash_success")) key = "flash_success";
                    // 只要名字里包含 AppIcon 就行 (不区分大小写)
                    else if (lower.Contains("appicon")) key = "appicon";

                    if (!_resourceMap.ContainsKey(key)) _resourceMap[key] = new List<string>();
                    _resourceMap[key].Add(name);
                }
            }
            catch { }
        }

        public static Image? LoadRandomImageFromResource(string key)
        {
            if (!_resourceMap.ContainsKey(key) || _resourceMap[key].Count == 0) return null;

            try
            {
                var list = _resourceMap[key];
                var resourceName = list[_rnd.Next(list.Count)];

                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null) return null;

                // 如果直接请求图片流，Icon 也能转 Bitmap
                if (resourceName.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    return new Icon(stream).ToBitmap();
                
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                return Image.FromStream(ms, true, true);
            } catch {
                return null;
            }
        }

        // [重构] 精准加载嵌入的 .ico 资源
        public static Icon? LoadIconFromResource(string key) {
            try {
                var asm = Assembly.GetExecutingAssembly();

                // 因为我们在 csproj 里指定了 <LogicalName>，所以名字是固定的
                // 这里的 key 参数其实可以忽略了，或者保留作为扩展
                string resourceName = "GitBranchSwitcher.AppIcon.ico";

                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null) {
                    // 如果找不到，打印一下所有资源名，方便调试 (调试时用)
                    // var allNames = asm.GetManifestResourceNames();
                    return null;
                }

                // 直接从流创建 Icon，效果最好，支持多尺寸自动切换
                return new Icon(stream);
            } catch {
                return null;
            }
        }
    }
}