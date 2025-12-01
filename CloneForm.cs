using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GitBranchSwitcher
{
    public class CloneForm : Form
    {
        private TextBox txtParentDir; // [修改] 改为文本框，不再下拉选择现有目录
        private TextBox txtPrefix;
        private TextBox txtBranches;
        private TextBox txtRepos;
        private Button btnStart;
        private TextBox txtLog;
        private ProgressBar progressBar;
        private Label lblStatus;
        private TabControl tabMode;
        
        // [新增] 用于回传给主界面：本次新生成的父节点路径列表
        public List<string> CreatedWorkspaces { get; private set; } = new List<string>();

        private class RepoConfig
        {
            public string Name { get; set; }
            public string RemoteUrl { get; set; }
            public string RelativePath { get; set; }
        }

        // [修改] 构造函数不需要参数了
        public CloneForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "🏗️ 批量拉线助手 (Workspace Generator)";
            Width = 800; Height = 750;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9F);

            var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 5 };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); 
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); 
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); 
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); 
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150)); 

            var pnlCommon = new GroupBox { Text = "1. 基础设置", Dock = DockStyle.Fill, Height = 170 }; // 稍微调高一点
            var pnlCommonFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true };
            
            // [修改] 目标根目录选择
            var pnlP = new FlowLayoutPanel { AutoSize = true };
            pnlP.Controls.Add(new Label { Text = "生成位置:", AutoSize = true, Padding = new Padding(0,6,0,0), Width = 60 });
            
            txtParentDir = new TextBox { Width = 500, Text = @"D:\Projects" }; // 给个默认值方便点
            var btnBrowse = new Button { Text = "📂 浏览...", AutoSize = true };
            btnBrowse.Click += (_, __) => { using var fbd = new FolderBrowserDialog(); if (fbd.ShowDialog() == DialogResult.OK) txtParentDir.Text = fbd.SelectedPath; };
            
            pnlP.Controls.Add(txtParentDir);
            pnlP.Controls.Add(btnBrowse);
            pnlP.Controls.Add(new Label { Text = "(将在该目录下创建新文件夹)", ForeColor = Color.Gray, AutoSize = true });
            
            // 前缀
            var pnlPre = new FlowLayoutPanel { AutoSize = true };
            pnlPre.Controls.Add(new Label { Text = "前缀:", AutoSize = true, Padding = new Padding(0,6,0,0), Width = 60 });
            txtPrefix = new TextBox { Width = 200, Text = "Sausage" };
            pnlPre.Controls.Add(txtPrefix);
            pnlPre.Controls.Add(new Label { Text = "(生成如: Sausage_develop)", ForeColor = Color.Gray, AutoSize = true, Padding = new Padding(0,6,0,0) });

            // 分支
            var pnlBr = new FlowLayoutPanel { AutoSize = true };
            pnlBr.Controls.Add(new Label { Text = "分支:", AutoSize = true, Padding = new Padding(0,6,0,0), Width = 60 });
            txtBranches = new TextBox { Width = 200, Text = "develop" };
            pnlBr.Controls.Add(txtBranches);
            pnlBr.Controls.Add(new Label { Text = "(支持多行，生成多个)", ForeColor = Color.Gray, AutoSize = true, Padding = new Padding(0,6,0,0) });

            pnlCommonFlow.Controls.Add(pnlP);
            pnlCommonFlow.Controls.Add(pnlPre);
            pnlCommonFlow.Controls.Add(pnlBr);
            pnlCommon.Controls.Add(pnlCommonFlow);

            tabMode = new TabControl { Dock = DockStyle.Fill };
            
            var pageSausage = new TabPage { Text = "🌭 香肠派对 (内部项目)", Padding = new Padding(10) };
            var lblSausageInfo = new Label { 
                Dock = DockStyle.Fill, 
                Text = "已内置配置：\n\n1. 自动拉取 Client, Script, Art, Audio, Scene, Bundles\n2. 自动处理 Biubiubiu2 / GoldDash 依赖逻辑\n3. 自动构建 Assets 嵌套目录结构\n\n只需在上方设定位置和分支，点击“开始”即可。",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.DarkGreen
            };
            pageSausage.Controls.Add(lblSausageInfo);

            var pageCustom = new TabPage { Text = "🛠️ 自定义 / 其他项目", Padding = new Padding(10) };
            txtRepos = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, PlaceholderText = "请输入Git仓库地址，一行一个..." };
            pageCustom.Controls.Add(txtRepos);

            tabMode.TabPages.Add(pageSausage);
            tabMode.TabPages.Add(pageCustom);

            btnStart = new Button { Text = "🚀 开始拉线 (Generate)", Height = 45, Dock = DockStyle.Fill, BackColor = Color.AliceBlue, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
            btnStart.Click += async (_, __) => await StartProcess();

            var pnlStatus = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Dock = DockStyle.Fill };
            lblStatus = new Label { Text = "Ready", AutoSize = true, Width = 600 };
            progressBar = new ProgressBar { Width = 750, Height = 10 };
            pnlStatus.Controls.Add(lblStatus);
            pnlStatus.Controls.Add(progressBar);

            txtLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), BackColor = Color.WhiteSmoke };

            mainLayout.Controls.Add(pnlCommon, 0, 0);
            mainLayout.Controls.Add(tabMode, 0, 1);
            mainLayout.Controls.Add(btnStart, 0, 2);
            mainLayout.Controls.Add(pnlStatus, 0, 3);
            mainLayout.Controls.Add(txtLog, 0, 4);

            Controls.Add(mainLayout);
        }

        private void Log(string s) => txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");

        private async Task StartProcess()
        {
            var parentRoot = txtParentDir.Text.Trim(); // 获取根位置
            var prefix = txtPrefix.Text.Trim();
            var branches = txtBranches.Lines.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();

            if (string.IsNullOrEmpty(parentRoot) || !Directory.Exists(parentRoot)) { MessageBox.Show("请选择有效的生成位置（硬盘目录）"); return; }
            if (branches.Count == 0) { MessageBox.Show("请至少输入一个分支"); return; }

            btnStart.Enabled = false;
            tabMode.Enabled = false;
            CreatedWorkspaces.Clear(); // 清空旧记录
            
            try 
            {
                if (tabMode.SelectedTab.Text.Contains("香肠"))
                {
                    await RunSausageLogic(parentRoot, prefix, branches);
                }
                else
                {
                    await RunCustomLogic(parentRoot, prefix, branches);
                }

                // 成功后关闭
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"发生错误: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Log($"Critical Error: {ex}");
            }
            finally
            {
                btnStart.Enabled = true;
                tabMode.Enabled = true;
            }
        }

        private async Task RunSausageLogic(string parentRoot, string prefix, List<string> branches)
        {
            Log("=== 启动香肠派对拉线流程 ===");

            var config = new List<RepoConfig>
            {
                new RepoConfig { Name = "Client", RemoteUrl = "git@git.tube:sausage-man/u3d-client.git", RelativePath = "." },
                new RepoConfig { Name = "Scripts", RemoteUrl = "git@git.tube:sausage-man/u3d-scripts.git", RelativePath = "Assets/Script" },
                new RepoConfig { Name = "Biubiubiu2", RemoteUrl = "git@git.tube:sausage-man/u3d-biubiubiu2-scripts.git", RelativePath = "Assets/Script/Biubiubiu2" },
                new RepoConfig { Name = "Bundles", RemoteUrl = "git@git.tube:sausage-man/u3d-bundles.git", RelativePath = "Assets/ToBundle" },
                new RepoConfig { Name = "Art", RemoteUrl = "git@git.tube:sausage-man/u3d-art.git", RelativePath = "Assets/Art" },
                new RepoConfig { Name = "Scene", RemoteUrl = "git@git.tube:sausage-man/u3d-scenes.git", RelativePath = "Assets/Scenes" },
                new RepoConfig { Name = "Audio", RemoteUrl = "git@git.tube:sausage-man/u3d-audio.git", RelativePath = "Assets/Audio" },
            };

            progressBar.Maximum = branches.Count * config.Count; 
            progressBar.Value = 0;

            foreach (var branch in branches)
            {
                string folderName = string.IsNullOrEmpty(prefix) ? branch.Replace("/", "_") : $"{prefix}_{branch.Replace("/", "_")}";
                string rootPath = Path.Combine(parentRoot, folderName);
                
                Log($"\r\n>>> 准备工作区: {folderName}");

                Directory.CreateDirectory(rootPath);
                
                // [重点] 记录这个新生成的目录，以便回传给主界面
                if (!CreatedWorkspaces.Contains(rootPath)) CreatedWorkspaces.Add(rootPath);

                // ... (中间的拉取逻辑 CloneOneRepo 保持不变，请直接使用上一版代码的中间部分) ...
                // 为了代码简洁，这里省略重复的 Clone 逻辑
                // 请确保 Copy 上一次 RunSausageLogic 的完整内容，尤其是 Biubiubiu2 的判断
                
                // --- 补全逻辑 Start ---
                var clientCfg = config.First(c => c.Name == "Client");
                if (!await CloneOneRepo(clientCfg, rootPath, branch)) return;

                var scriptCfg = config.First(c => c.Name == "Scripts");
                if (!await CloneOneRepo(scriptCfg, rootPath, branch)) return;

                string goldDashPath = Path.Combine(rootPath, "Assets", "Script", "GoldDash");
                string biuPath = Path.Combine(rootPath, "Assets", "Script", "Biubiubiu2");
                bool hasGoldDash = Directory.Exists(goldDashPath) && Directory.GetFiles(goldDashPath, "*", SearchOption.AllDirectories).Any();

                if (hasGoldDash) {
                    if (Directory.Exists(biuPath)) Directory.Delete(biuPath, true);
                } else {
                    if (Directory.Exists(goldDashPath)) Directory.Delete(goldDashPath, true);
                    var biuCfg = config.First(c => c.Name == "Biubiubiu2");
                    await CloneOneRepo(biuCfg, rootPath, branch);
                }

                var restRepos = config.Where(c => c.Name != "Client" && c.Name != "Scripts" && c.Name != "Biubiubiu2").ToList();
                var tasks = new List<Task>();
                foreach (var repo in restRepos) tasks.Add(CloneOneRepo(repo, rootPath, branch));
                await Task.WhenAll(tasks);
                // --- 补全逻辑 End ---
                
                Log($"✅ 工作区 {folderName} 准备就绪！");
            }
        }

        private async Task RunCustomLogic(string parentRoot, string prefix, List<string> branches)
        {
            var repoUrls = txtRepos.Lines.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
            if (repoUrls.Count == 0) { MessageBox.Show("请填写 Git 地址"); return; }

            progressBar.Maximum = branches.Count * repoUrls.Count;
            progressBar.Value = 0;

            await Task.Run(async () =>
            {
                foreach (var branch in branches)
                {
                    string folderName = string.IsNullOrEmpty(prefix) ? branch.Replace("/", "_") : $"{prefix}_{branch.Replace("/", "_")}";
                    string rootPath = Path.Combine(parentRoot, folderName);
                    Directory.CreateDirectory(rootPath);
                    
                    // [重点] 记录
                    if (!CreatedWorkspaces.Contains(rootPath)) CreatedWorkspaces.Add(rootPath);

                    Log($"\r\n>>> 工作区: {folderName}");

                    var tasks = new List<Task>();
                    foreach (var url in repoUrls)
                    {
                        string repoName = Path.GetFileNameWithoutExtension(url);
                        var cfg = new RepoConfig { Name = repoName, RemoteUrl = url, RelativePath = repoName };
                        tasks.Add(CloneOneRepo(cfg, rootPath, branch));
                    }
                    await Task.WhenAll(tasks);
                }
            });
        }
        
        // ... (CloneOneRepo 方法保持不变) ...
        private async Task<bool> CloneOneRepo(RepoConfig cfg, string rootPath, string branch)
        {
            return await Task.Run(() =>
            {
                string targetPath = Path.GetFullPath(Path.Combine(rootPath, cfg.RelativePath));
                string? targetParent = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetParent) && !Directory.Exists(targetParent)) Directory.CreateDirectory(targetParent);
                Invoke((Action)(() => lblStatus.Text = $"[{cfg.Name}] Clone -> {branch} ..."));
                var (ok, msg) = GitHelper.Clone(cfg.RemoteUrl, targetPath, branch, null);
                if (ok) Log($"   ✅ [{cfg.Name}] OK"); else Log($"   ❌ [{cfg.Name}] Fail: {msg}");
                Invoke((Action)(() => progressBar.PerformStep()));
                return ok;
            });
        }
    }
}