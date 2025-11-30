using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GitBranchSwitcher
{
    public partial class MainForm : Form {
        private TableLayoutPanel tlTop;
        private CheckedListBox lbParents;
        private TextBox txtSearch;
        private Button btnAddParent;
        private Button btnRemoveParent;
        private Button btnSelectAllParents;
        private Button btnClearParents;
        private Label lblHintParents;
        private SplitContainer splitMain;
        private SplitContainer splitUpper;
        private ListView lvRepos;
        private FlowLayoutPanel repoToolbar;
        private Panel panelLeft;
        private Panel pnlRight;
        private Label lblTargetBranch;
        private ComboBox cmbTargetBranch;
        private Button btnSwitchAll;
        private Button btnUseCurrentBranch;
        private CheckBox chkStashOnSwitch;
        private CheckBox chkFastMode; 
        private FlowLayoutPanel statePanel;
        private PictureBox pbState; 
        private Label lblStateText;
        private PictureBox pbFlash; 
        private System.Windows.Forms.Timer flashTimer;
        private TextBox txtLog;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;
        private ToolStripProgressBar statusProgress;
        private ToolStripStatusLabel statusStats; 

        private readonly BindingList<GitRepo> _repos = new BindingList<GitRepo>();
        private List<string> _allBranches = new List<string>();
        private AppSettings _settings;
        private System.Threading.CancellationTokenSource? _loadCts;
        private int _loadSeq = 0;
        private HashSet<string> _checkedParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const int TARGET_BOX = 500; 
        private const int FLASH_BOX = 300;
        private enum SwitchState { NotStarted, Switching, Done }

        public MainForm() {
            _settings = AppSettings.Load();
            InitializeComponent();
#if !BOSS_MODE
            TrySetRuntimeIcon();
#endif
            InitUi();
#if !BOSS_MODE
            LoadStateImagesRandom(); 
            LeaderboardService.SetPath(_settings.LeaderboardPath);
            _ = InitMyStatsAsync();
#endif
            SetSwitchState(SwitchState.NotStarted);
            SeedParentsToUi();
            
            // 启动分支缓存
            if (_settings.CachedBranchList != null && _settings.CachedBranchList.Count > 0) {
                _allBranches = new List<string>(_settings.CachedBranchList);
                UpdateBranchDropdown();
            }

            // [启动] 传入 false，优先读缓存
            _ = LoadReposForCheckedParentsAsync(false); 
        }

        // ... (保持 InitMyStatsAsync, InitializeComponent, InitUi 等代码不变) ...
        // 请务必保留 InitUi 中的事件绑定，特别是 btnAddParent, btnRescan 等调用 LoadReposForCheckedParentsAsync 的地方
        
        private async Task InitMyStatsAsync() { if (!string.IsNullOrEmpty(_settings.LeaderboardPath)) { var (c, t) = await LeaderboardService.GetMyStatsAsync(); UpdateStatsUi(c, t); } }
        private void InitializeComponent() {
#if BOSS_MODE
            Text = "Git 分支管理工具 (Enterprise)";
#else
            Text = "Unity 项目切线工具 (Cache V2)";
#endif
            Width = 1400; Height = 900; StartPosition = FormStartPosition.CenterScreen;
        }

        private void InitUi() {
            tlTop = new TableLayoutPanel { Dock = DockStyle.Top, Height = 120, ColumnCount = 6, Padding = new Padding(8) };
            tlTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); tlTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); tlTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); tlTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); tlTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); tlTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); tlTop.RowCount = 2; tlTop.RowStyles.Add(new RowStyle(SizeType.AutoSize)); tlTop.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lbParents = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
            btnAddParent = new Button { Text = "添加父目录…" }; btnRemoveParent = new Button { Text = "移除选中" };
            var lblSearch = new Label { Text = "过滤：", AutoSize = true, Anchor = AnchorStyles.Left }; txtSearch = new TextBox { Width = 220, Anchor = AnchorStyles.Left };
            var parentOps = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
            btnSelectAllParents = new Button { Text = "全选父目录", AutoSize = true }; btnClearParents = new Button { Text = "全不选父目录", AutoSize = true };
            parentOps.Controls.Add(btnSelectAllParents); parentOps.Controls.Add(btnClearParents);
            lblHintParents = new Label { Text = "提示：勾选要使用的父目录；支持过滤；Delete 可删除；右键可添加/移除。", AutoSize = true, ForeColor = SystemColors.GrayText };
            tlTop.Controls.Add(lbParents, 0, 0); tlTop.Controls.Add(btnAddParent, 1, 0); tlTop.Controls.Add(btnRemoveParent, 2, 0); tlTop.Controls.Add(lblSearch, 3, 0); tlTop.Controls.Add(txtSearch, 4, 0); tlTop.Controls.Add(parentOps, 5, 0); tlTop.Controls.Add(lblHintParents, 0, 1); tlTop.SetColumnSpan(lblHintParents, 6);
            var cm = new ContextMenuStrip(); cm.Items.Add("添加父目录…", null, (_, __) => btnAddParent.PerformClick()); cm.Items.Add("移除选中", null, (_, __) => btnRemoveParent.PerformClick()); lbParents.ContextMenuStrip = cm;
            
            // [事件] 添加/勾选/移除 都会触发加载逻辑
            btnAddParent.Click += (_, __) => { using var fbd = new FolderBrowserDialog(); if (fbd.ShowDialog(this) == DialogResult.OK) { var path = fbd.SelectedPath.Trim(); if (!Directory.Exists(path)) return; if (!_settings.ParentPaths.Contains(path)) { _settings.ParentPaths.Add(path); _settings.Save(); } RefilterParentsList(); _ = LoadReposForCheckedParentsAsync(true); } }; 
            btnRemoveParent.Click += async (_, __) => { var rm = new List<string>(); foreach(var i in lbParents.SelectedItems) rm.Add(i.ToString()); foreach(var i in lbParents.CheckedItems) rm.Add(i.ToString()); foreach(var p in rm) { _settings.ParentPaths.Remove(p); _checkedParents.Remove(p); } _settings.Save(); RefilterParentsList(); await LoadReposForCheckedParentsAsync(false); };
            txtSearch.TextChanged += (_, __) => RefilterParentsList();
            lbParents.ItemCheck += async (_, e) => { var p = lbParents.Items[e.Index].ToString(); BeginInvoke(new Action(async()=> { if(lbParents.GetItemChecked(e.Index)) _checkedParents.Add(p); else _checkedParents.Remove(p); await LoadReposForCheckedParentsAsync(false); })); };
            btnSelectAllParents.Click += async (_, __) => { _checkedParents = new HashSet<string>(_settings.ParentPaths); for(int i=0;i<lbParents.Items.Count;i++) lbParents.SetItemChecked(i,true); await LoadReposForCheckedParentsAsync(false); };
            btnClearParents.Click += async (_, __) => { _checkedParents.Clear(); for(int i=0;i<lbParents.Items.Count;i++) lbParents.SetItemChecked(i,false); await LoadReposForCheckedParentsAsync(false); };
            lbParents.KeyDown += async (_, e) => { if(e.KeyCode==Keys.Delete) btnRemoveParent.PerformClick(); };
            
            splitMain = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal }; splitUpper = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
            Shown += (_, __) => { splitMain.SplitterDistance = (int)(ClientSize.Height * 0.58); splitUpper.SplitterDistance = (int)(ClientSize.Width * 0.52); };
            lvRepos = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, CheckBoxes = true };
            lvRepos.Columns.Add("结果 (耗时)", 140); lvRepos.Columns.Add("当前分支", 220); lvRepos.Columns.Add("仓库名", 240); lvRepos.Columns.Add("路径", 400);
            
            var listMenu = new ContextMenuStrip();
            var itemOpenDir = listMenu.Items.Add("📂 打开文件夹"); listMenu.Items.Add(new ToolStripSeparator());
            var itemRepair = listMenu.Items.Add("🛠️ 解锁与修复 (删除 .lock)"); listMenu.Items.Add(new ToolStripSeparator());
            var itemGcFast = listMenu.Items.Add("🧹 方案 A：快速瘦身 (推荐)"); var itemGcDeep = listMenu.Items.Add("🌪️ 方案 B：深度瘦身 (极慢)");
            itemOpenDir.Click += (_, __) => { if (lvRepos.SelectedItems.Count == 0) return; var r = (GitRepo)lvRepos.SelectedItems[0].Tag; Process.Start("explorer.exe", r.Path); };
            itemRepair.Click += async (_, __) => { if (lvRepos.SelectedItems.Count == 0) { MessageBox.Show("请先选中"); return; } var item = lvRepos.SelectedItems[0]; var r = (GitRepo)item.Tag; if (MessageBox.Show($"确定要修复 [{r.Name}] 吗？", "修复", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; item.Text = "🛠️ 修复中..."; await Task.Run(() => { var sw = Stopwatch.StartNew(); var res = GitHelper.RepairRepo(r.Path); sw.Stop(); BeginInvoke((Action)(() => { item.Text = res.ok ? "✅ 修复完成" : "❌ 失败"; MessageBox.Show(res.log); })); }); };
            async void PerformGc(bool aggressive) { if (lvRepos.SelectedItems.Count == 0) { MessageBox.Show("请先选中"); return; } var item = lvRepos.SelectedItems[0]; var r = (GitRepo)item.Tag; if (MessageBox.Show($"确定对 [{r.Name}] 进行瘦身吗？", "确认", MessageBoxButtons.YesNo) != DialogResult.Yes) return; item.Text = "🧹 清理中..."; await Task.Run(() => { var res = GitHelper.GarbageCollect(r.Path, aggressive); BeginInvoke((Action)(() => { item.Text = res.ok ? $"✅ {res.sizeInfo}" : "❌ 失败"; if(res.ok) MessageBox.Show(res.sizeInfo); })); }); }
            itemGcFast.Click += (_, __) => PerformGc(false); itemGcDeep.Click += (_, __) => PerformGc(true);
            lvRepos.ContextMenuStrip = listMenu;

            repoToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(6) };
            var btnR1 = new Button { Text = "取消" }; var btnR2 = new Button { Text = "全选" }; var btnR3 = new Button { Text = "全不选" };
            var btnRescan = new Button { Text = "🔄 刷新/重扫", AutoSize = true };
            btnRescan.Click += async (_, __) => await LoadReposForCheckedParentsAsync(true);
            repoToolbar.Controls.Add(btnR1); repoToolbar.Controls.Add(btnR2); repoToolbar.Controls.Add(btnR3); repoToolbar.Controls.Add(btnRescan);
#if !BOSS_MODE
            var btnRank = new Button { Text = "🏆 排行榜", AutoSize = true, ForeColor = Color.DarkGoldenrod, Font = new Font(DefaultFont, FontStyle.Bold) };
            btnRank.Click += (_, __) => ShowLeaderboard(); repoToolbar.Controls.Add(btnRank); 
#endif
            btnR1.Click += (_,__) => { foreach(ListViewItem i in lvRepos.Items) i.Checked=false; }; btnR2.Click += (_,__) => { foreach(ListViewItem i in lvRepos.Items) i.Checked=true; }; btnR3.Click += (_,__) => { foreach(ListViewItem i in lvRepos.Items) i.Checked=false; };
            panelLeft = new Panel { Dock = DockStyle.Fill }; panelLeft.Controls.Add(lvRepos); panelLeft.Controls.Add(repoToolbar);
            pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            var rightLayout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true };
            rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var hint = new Label { Text = "提示：全量 Fetch 模式，确保能获取所有远程分支。", AutoSize = true, ForeColor = SystemColors.HotTrack };
            rightLayout.Controls.Add(hint, 0, 0); rightLayout.SetColumnSpan(hint, 3);
            lblTargetBranch = new Label { Text = "目标分支：", AutoSize = true };
            cmbTargetBranch = new ComboBox { Width = 400, DropDownStyle = ComboBoxStyle.DropDown, Anchor = AnchorStyles.Left|AnchorStyles.Right };
            btnUseCurrentBranch = new Button { Text = "使用选中项", AutoSize = true };
            btnUseCurrentBranch.Click += (_, __) => { var item = lvRepos.Items.Cast<ListViewItem>().FirstOrDefault(i=>i.Checked); if(item == null) { MessageBox.Show("请先勾选"); return; } var repo = (GitRepo)item.Tag; var branch = repo.CurrentBranch; if (!string.IsNullOrEmpty(branch) && branch != "—") { cmbTargetBranch.SelectedIndex = -1; cmbTargetBranch.Text = branch; } else { MessageBox.Show("无效分支"); } };
            cmbTargetBranch.TextUpdate += (_, __) => UpdateBranchDropdown();
            chkStashOnSwitch = new CheckBox { Text = "尝试 Stash 本地修改 [不勾选 = 强制覆盖]", AutoSize = true, Checked = _settings.StashOnSwitch, ForeColor = Color.DarkRed }; chkStashOnSwitch.CheckedChanged += (_, __) => { _settings.StashOnSwitch = chkStashOnSwitch.Checked; _settings.Save(); };
            chkFastMode = new CheckBox { Text = "⚡ 极速本地切换 (跳过 Fetch/Pull)", AutoSize = true, Checked = _settings.FastMode, ForeColor = Color.DarkGreen, Font = new Font(DefaultFont, FontStyle.Bold) }; chkFastMode.CheckedChanged += (_, __) => { _settings.FastMode = chkFastMode.Checked; _settings.Save(); };
            btnSwitchAll = new Button { Text = "🚀 一键切线 (Switch)", Height = 40, Width = 200, Font = new Font(DefaultFont, FontStyle.Bold), Anchor = AnchorStyles.Left | AnchorStyles.Right }; btnSwitchAll.Click += async (_, __) => await SwitchAllAsync();
            statePanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            pbState = new PictureBox { Width = TARGET_BOX, Height = TARGET_BOX, SizeMode = PictureBoxSizeMode.CenterImage };
            lblStateText = new Label { Text = "Ready", Font = new Font(DefaultFont, FontStyle.Bold), AutoSize = true };
            pbFlash = new PictureBox { Width = FLASH_BOX, Height = FLASH_BOX, Visible = false, SizeMode = PictureBoxSizeMode.CenterImage };
            flashTimer = new System.Windows.Forms.Timer { Interval = 800 }; flashTimer.Tick += (_,__) => { pbFlash.Visible=false; flashTimer.Stop(); };
            statePanel.Controls.Add(pbState); statePanel.Controls.Add(lblStateText); statePanel.Controls.Add(pbFlash);
            rightLayout.Controls.Add(lblTargetBranch, 0, 1); rightLayout.Controls.Add(cmbTargetBranch, 1, 1); rightLayout.Controls.Add(btnUseCurrentBranch, 2, 1); rightLayout.Controls.Add(btnSwitchAll, 0, 2); rightLayout.SetColumnSpan(btnSwitchAll, 3);
            rightLayout.Controls.Add(chkStashOnSwitch, 0, 3); rightLayout.SetColumnSpan(chkStashOnSwitch, 3); rightLayout.Controls.Add(chkFastMode, 0, 4); rightLayout.SetColumnSpan(chkFastMode, 3); rightLayout.Controls.Add(statePanel, 0, 5); rightLayout.SetColumnSpan(statePanel, 3);
            pnlRight.Controls.Add(rightLayout); splitUpper.Panel1.Controls.Add(panelLeft); splitUpper.Panel2.Controls.Add(pnlRight); splitMain.Panel1.Controls.Add(splitUpper);
            txtLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, ReadOnly = true, Font = new Font("Consolas", 9) }; splitMain.Panel2.Controls.Add(txtLog);
            statusStrip = new StatusStrip(); statusLabel = new ToolStripStatusLabel("就绪"); statusStrip.Items.Add(statusLabel); statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
#if !BOSS_MODE
            statusStats = new ToolStripStatusLabel { Alignment = ToolStripItemAlignment.Right, ForeColor = Color.Blue }; statusStrip.Items.Add(statusStats);
#endif
            statusProgress = new ToolStripProgressBar { Visible = false, Style = ProgressBarStyle.Marquee }; statusStrip.Items.Add(statusProgress);
            Controls.Add(splitMain); Controls.Add(tlTop); Controls.Add(statusStrip);
        }

        // ... (TrySetRuntimeIcon, ApplyImageTo, LoadStateImagesRandom, SetSwitchState, SeedParentsToUi, RefilterParentsList, UpdateStatsUi, FormatDuration, ShowInputBox, ShowLeaderboard 保持不变) ...
        private void TrySetRuntimeIcon() { try { var icon = ImageHelper.LoadIconFromResource("appicon"); if (icon != null) this.Icon = icon; } catch { } }
        private void ApplyImageTo(PictureBox pb, string key, int s) { 
#if BOSS_MODE
            pb.Image = null; 
#else
            if (pb.Image != null) { var o = pb.Image; pb.Image = null; o.Dispose(); } var img = ImageHelper.LoadRandomImageFromResource(key); if (img != null) { pb.SizeMode = (img.Width > s || img.Height > s) ? PictureBoxSizeMode.Zoom : PictureBoxSizeMode.CenterImage; pb.Image = img; } 
#endif
        }
        private void LoadStateImagesRandom() { ApplyImageTo(pbState, "state_notstarted", TARGET_BOX); ApplyImageTo(pbFlash, "flash_success", FLASH_BOX); }
        private void SetSwitchState(SwitchState st) { if (st == SwitchState.NotStarted) { ApplyImageTo(pbState, "state_notstarted", TARGET_BOX); lblStateText.Text = "未开始"; } if (st == SwitchState.Switching) { ApplyImageTo(pbState, "state_switching", TARGET_BOX); lblStateText.Text = "切线中..."; } if (st == SwitchState.Done) { ApplyImageTo(pbState, "state_done", TARGET_BOX); lblStateText.Text = "搞定!"; } }
        private void SeedParentsToUi() { if(lbParents==null) return; lbParents.BeginUpdate(); lbParents.Items.Clear(); foreach(var p in _settings.ParentPaths) { int i=lbParents.Items.Add(p); if(_checkedParents.Contains(p)) lbParents.SetItemChecked(i,true); } lbParents.EndUpdate(); }
        private void RefilterParentsList() { lbParents.BeginUpdate(); lbParents.Items.Clear(); var kw=txtSearch.Text.Trim(); foreach(var p in _settings.ParentPaths) { if(string.IsNullOrEmpty(kw)||p.IndexOf(kw,StringComparison.OrdinalIgnoreCase)>=0) { int i=lbParents.Items.Add(p); if(_checkedParents.Contains(p)) lbParents.SetItemChecked(i,true); } } lbParents.EndUpdate(); }
        private void UpdateStatsUi(int totalCount = -1, double totalSeconds = -1) { if (statusStats != null) { int c = totalCount >= 0 ? totalCount : _settings.TodaySwitchCount; double t = totalSeconds >= 0 ? totalSeconds : _settings.TodayTotalSeconds; statusStats.Text = $"📅 我的累计：切线 {c} 次 | 摸鱼总时长 {FormatDuration(t)}"; } }
        private string FormatDuration(double seconds) { var ts = TimeSpan.FromSeconds(seconds); if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}小时{ts.Minutes}分{ts.Seconds}秒"; if (ts.TotalMinutes >= 1) return $"{ts.Minutes}分{ts.Seconds}秒"; return $"{ts.Seconds}秒"; }
        private string ShowInputBox(string title, string prompt, string defaultVal) { Form promptForm = new Form() { Width = 500, Height = 150, FormBorderStyle = FormBorderStyle.FixedDialog, Text = title, StartPosition = FormStartPosition.CenterParent }; Label textLabel = new Label() { Left = 20, Top = 20, Text = prompt, AutoSize = true }; TextBox textBox = new TextBox() { Left = 20, Top = 50, Width = 440, Text = defaultVal }; Button confirmation = new Button() { Text = "确定", Left = 360, Width = 100, Top = 80, DialogResult = DialogResult.OK }; promptForm.Controls.Add(textLabel); promptForm.Controls.Add(textBox); promptForm.Controls.Add(confirmation); promptForm.AcceptButton = confirmation; return promptForm.ShowDialog() == DialogResult.OK ? textBox.Text : ""; }
        private async void ShowLeaderboard() {
            if (string.IsNullOrEmpty(_settings.LeaderboardPath)) { string input = ShowInputBox("设置", "请输入共享文件路径:", _settings.LeaderboardPath); if (string.IsNullOrWhiteSpace(input)) return; _settings.LeaderboardPath = input; _settings.Save(); LeaderboardService.SetPath(input); }
            var form = new Form { Text = "👑 卷王 & 摸鱼王 排行榜", Width = 940, Height = 493, StartPosition = FormStartPosition.CenterParent };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 465 };
            var listCount = new ListView { Dock = DockStyle.Fill, View = View.Details, GridLines = true, FullRowSelect = true };
            listCount.Columns.Add("排名", 50); listCount.Columns.Add("用户", 250); listCount.Columns.Add("切线次数", 100);
            var listDuration = new ListView { Dock = DockStyle.Fill, View = View.Details, GridLines = true, FullRowSelect = true };
            listDuration.Columns.Add("排名", 50); listDuration.Columns.Add("用户", 250); listDuration.Columns.Add("摸鱼总时长", 130);
            var lblMy = new Label { Dock = DockStyle.Bottom, Height = 40, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(DefaultFont, FontStyle.Bold), Text = "正在加载数据..." };
            split.Panel1.Controls.Add(listCount); split.Panel2.Controls.Add(listDuration); form.Controls.Add(split); form.Controls.Add(lblMy);
            form.Shown += async (_, __) => {
                var data = await LeaderboardService.GetLeaderboardAsync();
                var sortedCount = data.OrderByDescending(x => x.TotalSwitches).ToList();
                for (int i = 0; i < sortedCount.Count; i++) { var u = sortedCount[i]; var icon = i == 0 ? "🥇" : (i == 1 ? "🥈" : (i == 2 ? "🥉" : "")); string name = $"{icon} {u.Name}"; if (i == 0) name += " (🌭香肠切线王)"; listCount.Items.Add(new ListViewItem(new[] { (i + 1).ToString(), name, u.TotalSwitches.ToString() })); }
                var sortedTime = data.OrderByDescending(x => x.TotalDuration).ToList();
                for (int i = 0; i < sortedTime.Count; i++) { var u = sortedTime[i]; var icon = i == 0 ? "👑" : (i == 1 ? "🥈" : (i == 2 ? "🥉" : "")); string name = $"{icon} {u.Name}"; if (i == 0) name += " (🐟香肠摸鱼王)"; listDuration.Items.Add(new ListViewItem(new[] { (i + 1).ToString(), name, FormatDuration(u.TotalDuration) })); }
                var me = data.FirstOrDefault(x => x.Name == Environment.UserName);
                if (me != null) { int r1 = sortedCount.IndexOf(me) + 1; int r2 = sortedTime.IndexOf(me) + 1; lblMy.Text = $"我 ({me.Name})：切线 {me.TotalSwitches} 次 (第{r1}名) | 摸鱼总时长 {FormatDuration(me.TotalDuration)} (第{r2}名)"; } else { lblMy.Text = "暂无数据"; }
            };
            form.ShowDialog(this);
        }

        // [核心修改]：根据父节点加载子节点
        private async Task LoadReposForCheckedParentsAsync(bool forceRescan = false) {
            _loadCts?.Cancel(); _loadCts = new System.Threading.CancellationTokenSource(); var token = _loadCts.Token; var seq = ++_loadSeq;
            
            lvRepos.BeginUpdate(); lvRepos.Items.Clear(); lvRepos.EndUpdate(); 
            _repos.Clear(); _allBranches.Clear(); cmbTargetBranch.Items.Clear();

            var parents = _checkedParents.Where(Directory.Exists).ToList();
            if(!parents.Any()) { statusLabel.Text="就绪"; SetSwitchState(SwitchState.NotStarted); return; }

            // 临时集合，用于收集结果
            var finalRepos = new List<(string name, string path, string parent)>();
            var tasksToScan = new List<string>();

            // 1. 遍历每个勾选的父节点
            foreach (var p in parents)
            {
                // 尝试从缓存获取
                var cache = _settings.RepositoryCache.FirstOrDefault(x => string.Equals(x.ParentPath, p, StringComparison.OrdinalIgnoreCase));
                
                if (!forceRescan && cache != null && cache.Children != null && cache.Children.Count > 0)
                {
                    // 命中缓存：直接添加
                    foreach (var child in cache.Children)
                    {
                        // 校验路径是否存在 (防止缓存过期)
                        if (Directory.Exists(child.FullPath))
                        {
                            finalRepos.Add((child.Name, child.FullPath, Path.GetFileName(p)));
                        }
                    }
                }
                else
                {
                    // 未命中或强制刷新：加入待扫描队列
                    tasksToScan.Add(p);
                }
            }

            // 2. 如果有需要扫描的，在后台扫描
            if (tasksToScan.Count > 0)
            {
                statusLabel.Text = $"正在扫描 {tasksToScan.Count} 个新目录 (跳过 Library)...";
                statusProgress.Visible = true;

                var scannedResults = await Task.Run(() => {
                    var dict = new Dictionary<string, List<SubRepoItem>>();
                    foreach (var p in tasksToScan)
                    {
                        if (token.IsCancellationRequested) break;
                        var gitPaths = GitHelper.ScanForGitRepositories(p);
                        var items = new List<SubRepoItem>();
                        foreach (var path in gitPaths)
                        {
                            string name = string.Equals(path, p, StringComparison.OrdinalIgnoreCase) ? "Root" : path.Substring(p.Length).TrimStart(Path.DirectorySeparatorChar);
                            items.Add(new SubRepoItem { Name = name, FullPath = path });
                        }
                        dict[p] = items;
                    }
                    return dict;
                });

                if (token.IsCancellationRequested || seq != _loadSeq) { statusProgress.Visible = false; return; }

                // 3. 更新缓存并合并结果
                foreach (var kvp in scannedResults)
                {
                    // 更新 AppSettings 里的缓存
                    var existing = _settings.RepositoryCache.FirstOrDefault(x => string.Equals(x.ParentPath, kvp.Key, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) _settings.RepositoryCache.Remove(existing);
                    
                    _settings.RepositoryCache.Add(new ParentRepoCache { ParentPath = kvp.Key, Children = kvp.Value });

                    // 添加到本次显示列表
                    foreach (var item in kvp.Value)
                    {
                        finalRepos.Add((item.Name, item.FullPath, Path.GetFileName(kvp.Key)));
                    }
                }
                _settings.Save(); // 保存缓存到硬盘
            }

            // 4. 渲染 ListView
            lvRepos.BeginUpdate();
            // 去重 (防止父子嵌套导致的重复)
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, path, parentName) in finalRepos)
            {
                if (seenPaths.Contains(path)) continue;
                seenPaths.Add(path);

                var r = new GitRepo(name, path);
                string display = $"[{parentName}] {name}";
                if (name == "Root") display = $"[{parentName}] (根目录)";
                lvRepos.Items.Add(new ListViewItem(new[] { "⏳", "—", display, path }) { Tag=r, Checked=true });
            }
            lvRepos.EndUpdate();

            statusProgress.Visible = false; 
            statusLabel.Text = $"加载完成，共 {lvRepos.Items.Count} 个仓库";
            
            StartReadBranches(token);
        }

        private void StartReadBranches(System.Threading.CancellationToken token) {
            var tasks = new List<Task>();
            foreach(ListViewItem item in lvRepos.Items) tasks.Add(Task.Run(()=>{ if(token.IsCancellationRequested) return; ((GitRepo)item.Tag).CurrentBranch = GitHelper.GetFriendlyBranch(((GitRepo)item.Tag).Path); }));
            _ = Task.WhenAll(tasks).ContinueWith(t => {
                if(token.IsCancellationRequested) return;
                BeginInvoke((Action)(() => {
                    lvRepos.BeginUpdate();
                    foreach(ListViewItem item in lvRepos.Items) item.SubItems[1].Text = ((GitRepo)item.Tag).CurrentBranch;
                    lvRepos.EndUpdate();
                    RefreshBranchesAsync();
                }));
            });
        }

        private async Task RefreshBranchesAsync() {
            if (lvRepos == null || lvRepos.IsDisposed || lvRepos.Items.Count == 0) return;
            if (statusLabel != null) statusLabel.Text = "正在读取所有分支...";
            var targetPaths = new List<string>();
            foreach (ListViewItem item in lvRepos.Items) {
                if (item.Tag is GitRepo r && !string.IsNullOrEmpty(r.Path)) targetPaths.Add(r.Path);
            }
            var all = new HashSet<string>();
            var tasks = new List<Task<IEnumerable<string>>>();
            foreach (var path in targetPaths) tasks.Add(Task.Run(() => GitHelper.GetAllBranches(path)));
            try {
                var results = await Task.WhenAll(tasks);
                foreach (var list in results) if (list != null) foreach (var b in list) all.Add(b);
            } catch (Exception ex) { Log($"⚠️ 读取分支列表出错: {ex.Message}"); }
            _allBranches = all.OrderBy(x => x).ToList();
            
            // 保存分支缓存
            if (_allBranches.Count > 0) {
                if (_settings.CachedBranchList == null) _settings.CachedBranchList = new List<string>();
                _settings.CachedBranchList = _allBranches;
                _settings.Save();
            }

            if (cmbTargetBranch != null && !cmbTargetBranch.IsDisposed) UpdateBranchDropdown();
            if (statusLabel != null) statusLabel.Text = "就绪";
        }

        private void UpdateBranchDropdown() {
            try {
                if (cmbTargetBranch == null || cmbTargetBranch.IsDisposed) return;
                cmbTargetBranch.BeginUpdate(); cmbTargetBranch.Items.Clear();
                var txt = cmbTargetBranch.Text ?? "";
                var sourceSnapshot = _allBranches?.ToList() ?? new List<string>();
                var list = string.IsNullOrEmpty(txt) ? sourceSnapshot : sourceSnapshot.Where(b => b != null && b.IndexOf(txt, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                foreach(var b in list.Take(500)) cmbTargetBranch.Items.Add(b);
                cmbTargetBranch.EndUpdate();
                if (txt.Length <= cmbTargetBranch.Text.Length) cmbTargetBranch.SelectionStart = txt.Length;
                if (list.Count > 0 && cmbTargetBranch.Focused) { cmbTargetBranch.DroppedDown = true; Cursor.Current = Cursors.Default; }
            } catch (Exception ex) { Console.WriteLine(ex.Message); }
        }

        private async Task SwitchAllAsync() {
            var target = cmbTargetBranch.Text.Trim();
            if(string.IsNullOrEmpty(target)) { MessageBox.Show("请输入分支名"); return; }
            var items = lvRepos.Items.Cast<ListViewItem>().Where(i=>i.Checked).ToList();
            if(!items.Any()) return;
            btnSwitchAll.Enabled=false; statusProgress.Visible=true; SetSwitchState(SwitchState.Switching);
            foreach(var i in items) { i.Text="⏳"; i.SubItems[1].Text="..."; }
            int done=0; 
            var sem = new System.Threading.SemaphoreSlim(_settings.MaxParallel);
            var tasks = new List<Task>();
            Log($">>> 开始一键切线：{target} [极速模式:{_settings.FastMode}]");
            
            var batchSw = Stopwatch.StartNew();

            foreach(var item in items) {
                tasks.Add(Task.Run(async () => {
                    await sem.WaitAsync();
                    var r = (GitRepo)item.Tag;
                    var sw = Stopwatch.StartNew();
                    try {
                        var res = GitHelper.SwitchAndPull(r.Path, target, _settings.StashOnSwitch, _settings.FastMode);
                        r.SwitchOk = res.ok;
                        r.LastMessage = res.message;
                        r.CurrentBranch = GitHelper.GetFriendlyBranch(r.Path);
                    } finally { sw.Stop(); sem.Release(); }
                    BeginInvoke((Action)(() => {
                        item.Text = (r.SwitchOk?"✅":"❌") + $" {sw.Elapsed.TotalSeconds:F1}s";
                        item.SubItems[1].Text = r.CurrentBranch;
                        Log($"[{r.Name}] {r.LastMessage?.Replace("\n"," ")}");
                        
#if !BOSS_MODE
                        if(r.SwitchOk) { ApplyImageTo(pbFlash,"flash_success",FLASH_BOX); pbFlash.Visible=true; flashTimer.Start(); }
#endif
                        statusLabel.Text = $"处理中 {++done}/{items.Count}";
                    }));
                }));
            }
            await Task.WhenAll(tasks);
            batchSw.Stop();

#if !BOSS_MODE
            if(!string.IsNullOrEmpty(_settings.LeaderboardPath)) {
                var (newCount, newTime) = await LeaderboardService.UploadMyScoreAsync(batchSw.Elapsed.TotalSeconds);
                UpdateStatsUi(newCount, newTime);
            }
#endif

            SetSwitchState(SwitchState.Done); statusProgress.Visible=false; btnSwitchAll.Enabled=true; statusLabel.Text="完成"; Log("🏁 全部完成");
        }
        private void Log(string s) => txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");
    }
}