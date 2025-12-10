using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GitBranchSwitcher
{
    public static class UpdateService
    {
        public static async Task CheckAndUpdateAsync(string updateRootPath, Form owner)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(updateRootPath) || !Directory.Exists(updateRootPath)) return;

                string versionDir = Path.Combine(updateRootPath, "Version");
                string exeDir = Path.Combine(updateRootPath, "Exe");
                string versionFilePath = Path.Combine(versionDir, "version.txt");

                // 获取远程文件名（使用程序集名称，如 GitBranchSwitcher.exe）
                var asmName = Assembly.GetEntryAssembly().GetName().Name;
                string remoteExePath = Path.Combine(exeDir, asmName + ".exe");

                if (!File.Exists(versionFilePath) || !File.Exists(remoteExePath)) return;

                await Task.Run(() =>
                {
                    try
                    {
                        string verStr = File.ReadAllText(versionFilePath).Trim();
                        if (!Version.TryParse(verStr, out Version? remoteVer) || remoteVer == null) return;

                        var localVer = Assembly.GetExecutingAssembly().GetName().Version;

                        if (remoteVer > localVer)
                        {
                            string notePath = Path.Combine(versionDir, "release_note.txt");
                            string notes = "（本次更新包含若干性能优化与修复）";
                            if (File.Exists(notePath))
                            {
                                try { notes = File.ReadAllText(notePath, Encoding.UTF8); } catch { }
                            }

                            if (owner != null && !owner.IsDisposed && owner.IsHandleCreated)
                            {
                                owner.BeginInvoke((Action)(() =>
                                {
                                    // 弹窗提示
                                    MessageBox.Show(
                                        $"🎉 发现新版本 v{remoteVer} (当前 v{localVer})\n\n【更新公告】\n{notes}\n\n点击“确定”后将自动重启更新。", 
                                        "自动更新", 
                                        MessageBoxButtons.OK, 
                                        MessageBoxIcon.Information);

                                    // 执行更新
                                    PerformUpdate(remoteExePath);
                                }));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.ToString());
                    }
                });
            }
            catch { }
        }

        private static void PerformUpdate(string remoteExePath) {
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(currentExe))
                return;

            // [新增] 获取当前进程的文件名（例如 GitBranchSwitcher.exe）
            string exeName = Path.GetFileName(currentExe);

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string batchPath = Path.Combine(appDir, $"update_{Guid.NewGuid().ToString("N")}.cmd");

            var batContent = new StringBuilder();

            batContent.AppendLine("@chcp 65001 >NUL");
            batContent.AppendLine("@echo off");

            // [关键修改 1] 强制结束所有同名进程
            // /F: 强制终止
            // /IM: 指定映像名称
            // >NUL 2>&1: 屏蔽输出，防止如果没有其他进程时报错干扰
            batContent.AppendLine($"taskkill /F /IM \"{exeName}\" >NUL 2>&1");

            // [关键修改 2] 稍微延长等待时间，确保操作系统释放文件句柄
            batContent.AppendLine("timeout /t 2 /nobreak >NUL");

            // 循环尝试复制（防止杀进程后句柄释放延迟导致的偶尔失败）
            batContent.AppendLine(":TRY_COPY");
            batContent.AppendLine($"copy /Y \"{remoteExePath}\" \"{currentExe}\"");
            batContent.AppendLine("if %errorlevel% neq 0 (");
            batContent.AppendLine("    timeout /t 1 /nobreak >NUL");
            batContent.AppendLine("    goto TRY_COPY");
            batContent.AppendLine(")");

            // 启动更新后的程序
            batContent.AppendLine($"start \"\" \"{currentExe}\"");

            // 删除脚本自身
            batContent.AppendLine($"del \"%~f0\"");

            File.WriteAllText(batchPath, batContent.ToString(), new UTF8Encoding(false));

            var psi = new ProcessStartInfo {
                FileName = batchPath, UseShellExecute = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);
            Application.Exit();
        }
    }
}