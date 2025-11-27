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
            }
            catch { return null; }
        }

        // [修复] 专门加载 Icon，支持 .png/.jpg 自动转 .ico
        public static Icon? LoadIconFromResource(string key)
        {
            if (!_resourceMap.ContainsKey(key) || _resourceMap[key].Count == 0) return null;
            try {
                var list = _resourceMap[key];
                // 优先找真正的 .ico
                var bestMatch = list.FirstOrDefault(x => x.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) ?? list[0];
                
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(bestMatch);
                if (stream == null) return null;

                if (bestMatch.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    return new Icon(stream);
                }
                else
                {
                    // 如果是 png/jpg，转换成 Icon
                    using var bmp = new Bitmap(stream);
                    // GetHicon 创建的句柄需要管理，但在此简单场景下依赖系统回收或 Form 生命周期即可
                    return Icon.FromHandle(bmp.GetHicon());
                }
            } catch { return null; }
        }
    }
}