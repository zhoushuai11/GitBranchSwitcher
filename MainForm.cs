using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text;

namespace GitBranchSwitcher {
    public partial class MainForm : Form {
        // ==========================================
        // 布局容器
        // ==========================================
        private GroupBox grpTop, grpList, grpActions;
        private SplitContainer splitGlobal, splitUpper, splitMiddle;
        private Form consoleWindow;
        private Form? _leaderboardForm = null;

        // ==========================================
        // 控件定义
        // ==========================================
        // 1. 顶部工程区
        private CheckedListBox lbParents;
        private Button btnAddParent, btnRemoveParent;

        // 2. 仓库列表区
        private ListView lvRepos;
        private FlowLayoutPanel repoToolbar;

        // 3. 快捷操作区
        private Label lblTargetBranch, lblFetchStatus;
        private ComboBox cmbTargetBranch;
        private Button btnSwitchAll, btnUseCurrentBranch, btnToggleConsole, btnMyCollection;
        private CheckBox chkStashOnSwitch, chkFastMode, chkConfirmOnSwitch;

        // 状态与动画区
        private FlowLayoutPanel statePanel;
        private PictureBox pbState;
        private Label lblStateText;
        private System.Windows.Forms.Timer flashTimer;

        // 默认图片大小
        private const int DEFAULT_IMG_SIZE = 180;

        // 4. Git 控制台
        private GroupBox grpDetails;
        private SplitContainer splitConsole;
        private ListView lvFileChanges;
        private RichTextBox rtbDiff;
        private Panel pnlDetailRight, pnlActions;
        private Label lblRepoInfo;
        private TextBox txtCommitMsg;
        private Button btnCommit, btnPull, btnPush, btnStash;
        private ListViewGroup grpStaged, grpUnstaged;

        // 5. 日志区
        private GroupBox grpLog;
        private TextBox txtLog;

        // 底部状态栏
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel, statusStats;
        private ToolStripProgressBar statusProgress;
        private ToolStripStatusLabel statusTheme; // [修改] 增加 statusTheme
        // ==========================================
        // 数据与逻辑对象
        // ==========================================
        private readonly BindingList<GitRepo> _repos = new BindingList<GitRepo>();
        private List<string> _allBranches = new List<string>();
        private AppSettings _settings;
        private System.Threading.CancellationTokenSource? _loadCts;
        private int _loadSeq = 0;
        private HashSet<string> _checkedParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private GitWorkflowService _workflowService;

        // 本地内存缓存
        private List<CollectedItem> _myCollection = new List<CollectedItem>();
        
        private const string THEME_NONE = "🚫 无主题 (None)";
        private const string THEME_COLLECTION = "🌟 我的收藏 (My Collection)";
        private const string COLL_RANDOM = "🎲 随机展示 (Random)";

        private enum SwitchState {
            NotStarted,
            Switching,
            Done
        }

        // === 🐸 青蛙旅行 & 抽卡系统 ===
        private enum Rarity {
            N,
            R,
            SR,
            SSR,
            UR
        }

        // [修改] 1. 定义两套概率表

        // 欧皇池 (切线 >= 5 个)：原版概率
        private readonly Dictionary<Rarity, int> _rarityWeightsHigh = new Dictionary<Rarity, int> {
            {
                Rarity.N, 625
            }, {
                Rarity.R, 125
            }, {
                Rarity.SR, 25
            }, {
                Rarity.SSR, 5
            }, {
                Rarity.UR, 1
            } // 有 1% 几率出 UR
        };

        // 非酋池 (切线 < 5 个)：概率降低，UR 绝迹
        private readonly Dictionary<Rarity, int> _rarityWeightsLow = new Dictionary<Rarity, int> {
            {
                Rarity.N, 625
            }, // N 卡概率大增
            {
                Rarity.R, 125
            }, {
                Rarity.SR, 25
            }, // SR 只有 4%
            {
                Rarity.SSR, 1
            }, {
                Rarity.UR, 0
            } // 无法获得 UR
        };

        private readonly Dictionary<Rarity, Color> _rarityColors = new Dictionary<Rarity, Color> {
            {
                Rarity.N, Color.Gray
            }, {
                Rarity.R, Color.DodgerBlue
            }, {
                Rarity.SR, Color.MediumPurple
            }, {
                Rarity.SSR, Color.Gold
            }, {
                Rarity.UR, Color.Crimson
            }
        };

        // 稀有度分数 (欧气值)
        private readonly Dictionary<Rarity, int> _rarityScores = new Dictionary<Rarity, int> {
            {
                Rarity.UR, 625
            }, {
                Rarity.SSR, 125
            }, {
                Rarity.SR, 25
            }, {
                Rarity.R, 5
            }, {
                Rarity.N, 1
            }
        };

        public MainForm() {
            _settings = AppSettings.Load();

            // 加载我的藏品
            _myCollection = CollectionService.Load(_settings.UpdateSourcePath, Environment.UserName);

            InitializeComponent();
            TrySetRuntimeIcon();
            InitUi();
#if !BOSS_MODE
            LoadRandomFrameWorkImage();
            LeaderboardService.SetPath(_settings.LeaderboardPath);
            _ = InitMyStatsAsync();
#endif
            UpdateThemeLabel();
            ApplyThemeColors();
            SetSwitchState(SwitchState.NotStarted);
            SeedParentsToUi();

            if (_settings.CachedBranchList != null && _settings.CachedBranchList.Count > 0) {
                _allBranches = new List<string>(_settings.CachedBranchList);
                UpdateBranchDropdown();
            }

            _ = LoadReposForCheckedParentsAsync(false);
            _workflowService = new GitWorkflowService(_settings.MaxParallel);
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            ConfigureInitialLayout();
            AdjustPbSizeMode(pbState);
#if !PURE_MODE
            _ = UpdateService.CheckAndUpdateAsync(_settings.UpdateSourcePath, this);
#endif
        }

        private void ConfigureInitialLayout() {
            try {
                splitGlobal.SplitterDistance = (int)(this.Height * 0.75);
                splitUpper.SplitterDistance = 140;
                splitMiddle.SplitterDistance = (int)(splitMiddle.Width * 0.65);
            } catch {
            }
        }

        private async Task InitMyStatsAsync() {
            if (!string.IsNullOrEmpty(_settings.LeaderboardPath)) {
                var (c, t, s) = await LeaderboardService.GetMyStatsAsync();
                UpdateStatsUi(c, t, s);
            }
        }

        private void InitializeComponent() {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string vStr = $"{version.Major}.{version.Minor}.{version.Build}";
            Text = $"Git 分支管理工具 - v{vStr}";
            Width = 1800;
            Height = 1150;
            StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.BackColor = Color.WhiteSmoke;
        }
        
        private void InitRandomTheme() {
            // 如果已经有设置了，直接跳过
            if (!string.IsNullOrEmpty(_settings.SelectedTheme)) return;

            try {
                string root = _settings.FrameWorkImgPath;
                if (Directory.Exists(root)) {
                    var dirs = Directory.GetDirectories(root);
                    if (dirs.Length > 0) {
                        // 随机选一个文件夹名作为默认主题
                        string randomTheme = Path.GetFileName(dirs[new Random().Next(dirs.Length)]);
                        _settings.SelectedTheme = randomTheme;
                        _settings.Save();
                        Log($"[System] 初始化随机主题: {randomTheme}");
                    }
                }
            } catch (Exception ex) {
                Log($"[System] 初始化主题失败: {ex.Message}");
            }
        }
        
        // [修改] UpdateThemeLabel 方法
        private void UpdateThemeLabel() {
            if (statusTheme == null) return;
    
            string display;
            if (string.IsNullOrEmpty(_settings.SelectedTheme) || _settings.SelectedTheme == THEME_NONE) {
                display = "无主题";
            } else if (_settings.SelectedTheme == THEME_COLLECTION) {
                string sub = _settings.SelectedCollectionItem == "Random" ? "随机" : "固定";
                display = $"收藏品 ({sub})";
            } else {
                display = _settings.SelectedTheme; // 普通主题名
            }

            statusTheme.Text = $"🎨 主题: {display}";
            statusTheme.ForeColor = _settings.IsDarkMode ? Color.Gray : Color.DimGray;
        }

        private Button MakeBtn(string text, Color? backColor = null) {
            var b = new Button {
                Text = text,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Height = 28
            };
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Color.LightGray;
            if (backColor.HasValue)
                b.BackColor = backColor.Value;
            else
                b.BackColor = Color.White;
            return b;
        }

        private void InitUi() {
            // === 全局布局容器 ===
            splitGlobal = new SplitContainer {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6
            };
            splitUpper = new SplitContainer {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6
            };
            splitMiddle = new SplitContainer {
                Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6
            };

            // ==========================================
            // 1. 工程区 (grpTop)
            // ==========================================
            grpTop = new GroupBox {
                Text = "① 工程区 (Project Workspace)", Dock = DockStyle.Fill, Padding = new Padding(10)
            };
            var pnlTopContent = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1
            };
            pnlTopContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pnlTopContent.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            lbParents = new CheckedListBox {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                BorderStyle = BorderStyle.None,
                BackColor = Color.WhiteSmoke
            };
            var pnlTopBtns = new FlowLayoutPanel {
                AutoSize = true, FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Right, Margin = new Padding(0)
            };
            btnAddParent = MakeBtn("📂 添加父目录...", Color.AliceBlue);
            btnAddParent.Width = 140;
            btnRemoveParent = MakeBtn("🗑️ 移除选中");
            btnRemoveParent.Width = 140;
            var btnToggleParents = MakeBtn("✅ 全选/反选");
            btnToggleParents.Width = 140;
            var pnlSelectBtns = new FlowLayoutPanel {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0)
            };
            pnlSelectBtns.Controls.Add(btnToggleParents);
            pnlTopBtns.Controls.Add(btnAddParent);
            pnlTopBtns.Controls.Add(btnRemoveParent);
            pnlTopBtns.Controls.Add(pnlSelectBtns);
            pnlTopContent.Controls.Add(lbParents, 0, 0);
            pnlTopContent.Controls.Add(pnlTopBtns, 1, 0);
            grpTop.Controls.Add(pnlTopContent);

            var cm = new ContextMenuStrip();
            cm.Items.Add("添加父目录…", null, (_, __) => btnAddParent.PerformClick());
            cm.Items.Add("移除选中", null, (_, __) => btnRemoveParent.PerformClick());
            lbParents.ContextMenuStrip = cm;
            btnAddParent.Click += (_, __) => {
                using var fbd = new FolderBrowserDialog();
                if (fbd.ShowDialog(this) == DialogResult.OK) {
                    var path = fbd.SelectedPath.Trim();
                    if (!Directory.Exists(path))
                        return;
                    if (!_settings.ParentPaths.Contains(path)) {
                        _settings.ParentPaths.Add(path);
                        _settings.Save();
                    }

                    SeedParentsToUi();
                    _ = LoadReposForCheckedParentsAsync(true);
                }
            };
            btnRemoveParent.Click += async (_, __) => {
                var rm = new List<string>();
                foreach (var i in lbParents.SelectedItems)
                    rm.Add(i.ToString());
                foreach (var i in lbParents.CheckedItems)
                    rm.Add(i.ToString());
                foreach (var p in rm) {
                    _settings.ParentPaths.Remove(p);
                    _checkedParents.Remove(p);
                }

                _settings.Save();
                SeedParentsToUi();
                await LoadReposForCheckedParentsAsync(true);
            };
            btnToggleParents.Click += async (_, __) => {
                bool isAllChecked = lbParents.CheckedItems.Count == lbParents.Items.Count;
                bool targetState = !isAllChecked;
                _checkedParents.Clear();
                if (targetState) {
                    foreach (var item in lbParents.Items)
                        _checkedParents.Add(item.ToString());
                }

                for (int i = 0; i < lbParents.Items.Count; i++)
                    lbParents.SetItemChecked(i, targetState);
                await LoadReposForCheckedParentsAsync(targetState? false : true);
            };
            lbParents.ItemCheck += async (_, e) => {
                var p = lbParents.Items[e.Index].ToString();
                BeginInvoke(new Action(async () => {
                    if (lbParents.GetItemChecked(e.Index))
                        _checkedParents.Add(p);
                    else
                        _checkedParents.Remove(p);
                    await LoadReposForCheckedParentsAsync(false);
                }));
            };
            splitUpper.Panel1.Controls.Add(grpTop);

            // ==========================================
            // 2. 仓库列表 (grpList)
            // ==========================================
            grpList = new GroupBox {
                Text = "② 仓库列表 (Repositories)", Dock = DockStyle.Fill, Padding = new Padding(5)
            };
            repoToolbar = new FlowLayoutPanel {
                Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 5)
            };
            var btnToggleSelect = MakeBtn("✅ 全选/反选");
            var btnRescan = MakeBtn("🔄 刷新");
            var btnNewClone = MakeBtn("➕ 新建拉线", Color.Azure);
            btnNewClone.ForeColor = Color.DarkBlue;
#if !BOSS_MODE && !PURE_MODE
            var btnRank = MakeBtn("🏆 排行榜", Color.Ivory);
            btnRank.ForeColor = Color.DarkGoldenrod;
#endif
            var btnSuperSlim = MakeBtn("🔥 一键瘦身", Color.MistyRose);
            btnSuperSlim.ForeColor = Color.DarkRed;
            
            // [新增] 设置按钮
            var btnSettings = MakeBtn("⚙️ 设置", Color.WhiteSmoke);
            btnSettings.ForeColor = Color.DimGray;

            repoToolbar.Controls.Add(btnToggleSelect);
            repoToolbar.Controls.Add(btnRescan);
            repoToolbar.Controls.Add(new Label {
                Width = 10
            });
            repoToolbar.Controls.Add(btnNewClone);
            repoToolbar.Controls.Add(new Label {
                Width = 10
            });
#if !BOSS_MODE && !PURE_MODE
            repoToolbar.Controls.Add(btnRank);
#endif
            repoToolbar.Controls.Add(btnSuperSlim);
            
            // [新增] 将设置按钮加在瘦身按钮后面
            repoToolbar.Controls.Add(btnSettings);

            lvRepos = new ListView {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                CheckBoxes = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            lvRepos.Columns.Add("状态", 50);
            lvRepos.Columns.Add("当前分支", 240);
            lvRepos.Columns.Add("同步", 90);
            lvRepos.Columns.Add("仓库名", 180);
            lvRepos.Columns.Add("路径", 400);
            try {
                var prop = typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                prop?.SetValue(lvRepos, true, null);
            } catch {
            }

            grpList.Controls.Add(lvRepos);
            grpList.Controls.Add(repoToolbar);
            splitMiddle.Panel1.Controls.Add(grpList);

            lvRepos.SelectedIndexChanged += async (_, __) => await RefreshRepoDetails();

            btnToggleSelect.Click += (_, __) => {
                bool hasUn = lvRepos.Items.Cast<ListViewItem>().Any(i => !i.Checked);
                lvRepos.BeginUpdate();
                foreach (ListViewItem i in lvRepos.Items)
                    i.Checked = hasUn;
                lvRepos.EndUpdate();
                if (hasUn) {
                    var topBranch = lvRepos.Items.Cast<ListViewItem>().Select(i => ((GitRepo)i.Tag).CurrentBranch).Where(b => !string.IsNullOrEmpty(b) && b != "—").GroupBy(b => b).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
                    if (!string.IsNullOrEmpty(topBranch)) {
                        cmbTargetBranch.Text = topBranch;
                    }
                }
            };
            btnRescan.Click += async (_, __) => await LoadReposForCheckedParentsAsync(true);
            btnNewClone.Click += (_, __) => {
                var form = new CloneForm();
                if (form.ShowDialog(this) == DialogResult.OK && form.CreatedWorkspaces.Count > 0) {
                    bool c = false;
                    foreach (var p in form.CreatedWorkspaces) {
                        if (!_settings.ParentPaths.Contains(p)) {
                            _settings.ParentPaths.Add(p);
                            _checkedParents.Add(p);
                            c = true;
                        }
                    }

                    if (c) {
                        _settings.Save();
                        SeedParentsToUi();
                        _ = LoadReposForCheckedParentsAsync(true);
                    }
                }
            };
            btnSettings.Click += (_, __) => ShowThemeSettingsDialog();
#if !BOSS_MODE && !PURE_MODE
            btnRank.Click += (_, __) => ShowLeaderboard();
#endif
            btnSuperSlim.Click += (_, __) => StartSuperSlimProcess();
            var listMenu = new ContextMenuStrip();
            listMenu.Items.Add("📂 打开文件夹", null, (_, __) => {
                if (lvRepos.SelectedItems.Count > 0)
                    Process.Start("explorer.exe", ((GitRepo)lvRepos.SelectedItems[0].Tag).Path);
            });
            listMenu.Items.Add("🛠️ 修复锁文件", null, async (_, __) => {
                if (lvRepos.SelectedItems.Count == 0)
                    return;
                var r = (GitRepo)lvRepos.SelectedItems[0].Tag;
                await Task.Run(() => GitHelper.RepairRepo(r.Path));
                MessageBox.Show("修复完成");
            });
            lvRepos.ContextMenuStrip = listMenu;

            // ==========================================
            // 3. 快捷操作 (grpActions)
            // ==========================================
            grpActions = new GroupBox {
                Text = "③ 快捷操作 (Actions)", Dock = DockStyle.Fill, Padding = new Padding(10)
            };
            var pnlActionContent = new Panel {
                Dock = DockStyle.Fill, AutoScroll = true
            };

            lblFetchStatus = new Label {
                Text = "",
                AutoSize = true,
                ForeColor = Color.Magenta,
                Font = new Font("Segoe UI", 9, FontStyle.Italic),
                Dock = DockStyle.Top
            };
            lblTargetBranch = new Label {
                Text = "🎯 目标分支：",
                AutoSize = true,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Margin = new Padding(0, 2, 0, 2),
                Dock = DockStyle.Top
            };
            var pnlComboRow = new Panel {
                Height = 28, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 2)
            };
            cmbTargetBranch = new ComboBox {
                Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown
            };
            btnUseCurrentBranch = MakeBtn("👈 填入");
            btnUseCurrentBranch.Dock = DockStyle.Right;
            btnUseCurrentBranch.Width = 60;
            pnlComboRow.Controls.Add(cmbTargetBranch);
            pnlComboRow.Controls.Add(btnUseCurrentBranch);

            var pnlSpacer1 = new Panel {
                Height = 5, Dock = DockStyle.Top
            };
            btnSwitchAll = new Button {
                Text = "🚀 一键切线 (Switch)",
                Height = 40,
                Dock = DockStyle.Top,
                BackColor = Color.DodgerBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 12, FontStyle.Bold)
            };
            btnSwitchAll.FlatAppearance.BorderSize = 0;
            chkStashOnSwitch = new CheckBox {
                Text = "🔒 尝试 Stash 本地修改",
                AutoSize = true,
                Checked = _settings.StashOnSwitch,
                ForeColor = Color.DarkSlateBlue,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 5, 0, 0)
            };
            chkFastMode = new CheckBox {
                Text = "⚡ 极速本地切换 (跳过 Fetch)",
                AutoSize = true,
                Checked = _settings.FastMode,
                ForeColor = Color.DarkGreen,
                Font = new Font(DefaultFont, FontStyle.Bold),
                Dock = DockStyle.Top,
                Padding = new Padding(0, 2, 0, 0)
            };
            chkConfirmOnSwitch = new CheckBox {
                Text = "🛡️ 开启切线二次确认弹窗",
                AutoSize = true,
                Checked = _settings.ConfirmOnSwitch,
                ForeColor = Color.DarkRed,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 2, 0, 0)
            };
            chkConfirmOnSwitch.CheckedChanged += (_, __) => {
                _settings.ConfirmOnSwitch = chkConfirmOnSwitch.Checked;
                _settings.Save();
            };

            btnToggleConsole = MakeBtn("💻 打开 Git 控制台", Color.OldLace);
            btnToggleConsole.Height = 32;
            btnToggleConsole.Dock = DockStyle.Top;

            btnMyCollection = MakeBtn("🖼️ 我的藏品 (Album)", Color.LavenderBlush);
            btnMyCollection.Height = 32;
            btnMyCollection.Dock = DockStyle.Top;
            btnMyCollection.Click += (_, __) => new CollectionForm().Show();

            var pnlBtnsWrap = new Panel {
                Height = 70, Dock = DockStyle.Top, Padding = new Padding(0, 6, 0, 0)
            };
            pnlBtnsWrap.Controls.Add(btnMyCollection);
            pnlBtnsWrap.Controls.Add(btnToggleConsole);

            statePanel = new FlowLayoutPanel {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 5, 0, 0)
            };
            pbState = new PictureBox {
                Width = DEFAULT_IMG_SIZE, Height = DEFAULT_IMG_SIZE, SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.Hand
            };

            grpActions.Resize += (s, e) => {
                try {
                    int newWidth = grpActions.ClientSize.Width - 25;
                    if (newWidth < 100)
                        newWidth = 100;
                    if (newWidth > 350)
                        newWidth = 350;
                    pbState.Size = new Size(newWidth, newWidth);
                    AdjustPbSizeMode(pbState);
                } catch {
                }
            };

            var menuFrog = new ContextMenuStrip();
            menuFrog.Items.Add("🖼️ 查看我的藏品 (Album)", null, (_, __) => new CollectionForm().Show());
            menuFrog.Items.Add(new ToolStripSeparator());
            menuFrog.Items.Add("📂 打开图库目录 (Img)", null, (_, __) => {
                string path = Path.Combine(_settings.UpdateSourcePath, "Img");
                try {
                    Process.Start("explorer.exe", path);
                } catch {
                    MessageBox.Show("无法访问共享目录: " + path);
                }
            });
            menuFrog.Items.Add("📂 打开存档目录 (Collect)", null, (_, __) => {
                string path = Path.Combine(_settings.UpdateSourcePath, "Collect");
                try {
                    Process.Start("explorer.exe", path);
                } catch {
                    MessageBox.Show("无法访问共享目录: " + path);
                }
            });
            pbState.ContextMenuStrip = menuFrog;

            lblStateText = new Label {
                Text = "Ready",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                ForeColor = Color.Gray,
                Margin = new Padding(0, 5, 0, 0)
            };
            statePanel.Controls.Add(pbState);
            statePanel.Controls.Add(lblStateText);

            pnlActionContent.Controls.Add(statePanel);
            pnlActionContent.Controls.Add(pnlBtnsWrap);
            pnlActionContent.Controls.Add(chkConfirmOnSwitch);
            pnlActionContent.Controls.Add(chkFastMode);
            pnlActionContent.Controls.Add(chkStashOnSwitch);
            pnlActionContent.Controls.Add(btnSwitchAll);
            pnlActionContent.Controls.Add(pnlSpacer1);
            pnlActionContent.Controls.Add(pnlComboRow);
            pnlActionContent.Controls.Add(lblTargetBranch);
            pnlActionContent.Controls.Add(lblFetchStatus);
            grpActions.Controls.Add(pnlActionContent);
            splitMiddle.Panel2.Controls.Add(grpActions);
            splitUpper.Panel2.Controls.Add(splitMiddle);

            btnUseCurrentBranch.Click += (_, __) => {
                var item = lvRepos.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Checked);
                if (item == null) {
                    MessageBox.Show("请先勾选");
                    return;
                }

                var repo = (GitRepo)item.Tag;
                if (!string.IsNullOrEmpty(repo.CurrentBranch) && repo.CurrentBranch != "—") {
                    cmbTargetBranch.Text = repo.CurrentBranch;
                } else
                    MessageBox.Show("无效分支");
            };
            cmbTargetBranch.TextUpdate += (_, __) => {
                try {
                    UpdateBranchDropdown();
                } catch {
                }
            };
            chkStashOnSwitch.CheckedChanged += (_, __) => {
                _settings.StashOnSwitch = chkStashOnSwitch.Checked;
                _settings.Save();
            };
            chkFastMode.CheckedChanged += (_, __) => {
                _settings.FastMode = chkFastMode.Checked;
                _settings.Save();
            };
            btnSwitchAll.Click += async (_, __) => await SwitchAllAsync();
            flashTimer = new System.Windows.Forms.Timer {
                Interval = 1500
            };
            flashTimer.Tick += (_, __) => {
                flashTimer.Stop();
            };
            btnToggleConsole.Click += (_, __) => {
                if (consoleWindow.Visible) {
                    consoleWindow.Hide();
                    btnToggleConsole.Text = "💻 打开 Git 控制台";
                } else {
                    consoleWindow.Show();
                    if (consoleWindow.WindowState == FormWindowState.Minimized)
                        consoleWindow.WindowState = FormWindowState.Normal;
                    consoleWindow.Activate();
                    btnToggleConsole.Text = "💻 关闭 Git 控制台";
                }
            };

            // ==========================================
            // 4. Git 控制台 (grpDetails)
            // ==========================================
            grpDetails = new GroupBox {
                Text = "④ Git 控制台 (Console)", Dock = DockStyle.Fill, Padding = new Padding(5), BackColor = Color.White
            };
            splitConsole = new SplitContainer {
                Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 5
            };
            lvFileChanges = new ListView {
                Dock = DockStyle.Fill,
                View = View.Details,
                GridLines = false,
                FullRowSelect = true,
                BorderStyle = BorderStyle.FixedSingle,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                ShowGroups = true,
                MultiSelect = false
            };
            grpStaged = new ListViewGroup("staged", "已暂存 (Staged)");
            grpUnstaged = new ListViewGroup("unstaged", "未暂存 (Unstaged)");
            lvFileChanges.Groups.Add(grpStaged);
            lvFileChanges.Groups.Add(grpUnstaged);
            lvFileChanges.Columns.Add("状态", 40);
            lvFileChanges.Columns.Add("文件路径", 500);
            pnlDetailRight = new Panel {
                Dock = DockStyle.Fill
            };
            lblRepoInfo = new Label {
                Dock = DockStyle.Top,
                Height = 25,
                Text = "请选择仓库...",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.DarkSlateGray,
                BackColor = Color.WhiteSmoke
            };
            pnlActions = new Panel {
                Dock = DockStyle.Bottom, Height = 95, Padding = new Padding(5)
            };
            txtCommitMsg = new TextBox {
                Dock = DockStyle.Top,
                Height = 55,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                PlaceholderText = "Commit Message...",
                BorderStyle = BorderStyle.FixedSingle
            };
            var pnlBtns = new FlowLayoutPanel {
                Dock = DockStyle.Bottom, Height = 32, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 3, 0, 0)
            };
            btnCommit = new Button {
                Text = "Commit",
                Width = 90,
                Height = 28,
                BackColor = Color.DodgerBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCommit.FlatAppearance.BorderSize = 0;
            btnPush = MakeBtn("⬆ Push", Color.AliceBlue);
            btnPush.Width = 80;
            btnPull = MakeBtn("⬇ Pull", Color.AliceBlue);
            btnPull.Width = 80;
            btnStash = MakeBtn("📦 Stash");
            btnStash.Width = 70;
            pnlBtns.Controls.Add(btnCommit);
            pnlBtns.Controls.Add(btnPush);
            pnlBtns.Controls.Add(btnPull);
            pnlBtns.Controls.Add(btnStash);
            pnlActions.Controls.Add(txtCommitMsg);
            pnlActions.Controls.Add(pnlBtns);
            rtbDiff = new RichTextBox {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.Gainsboro,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                WordWrap = false
            };
            pnlDetailRight.Controls.Add(rtbDiff);
            pnlDetailRight.Controls.Add(lblRepoInfo);
            pnlDetailRight.Controls.Add(pnlActions);
            splitConsole.Panel1.Controls.Add(lvFileChanges);
            splitConsole.Panel2.Controls.Add(pnlDetailRight);
            grpDetails.Controls.Add(splitConsole);
            lvFileChanges.SelectedIndexChanged += async (_, __) => await ShowSelectedFileDiff();
            lvFileChanges.DoubleClick += async (_, __) => await ToggleStagedStatus();
            btnCommit.Click += async (_, __) => await RunDetailAction("Commit");
            btnPull.Click += async (_, __) => await RunDetailAction("Pull");
            btnPush.Click += async (_, __) => await RunDetailAction("Push");
            btnStash.Click += async (_, __) => await RunDetailAction("Stash");
            var fileMenu = new ContextMenuStrip();
            fileMenu.Items.Add("➕ 加入/移出 暂存区", null, async (_, __) => await ToggleStagedStatus());
            fileMenu.Items.Add("📂 打开目录", null, (_, __) => {
                if (lvFileChanges.SelectedItems.Count > 0 && lvRepos.SelectedItems.Count > 0)
                    Process.Start("explorer.exe", "/select,\"" + Path.Combine(((GitRepo)lvRepos.SelectedItems[0].Tag).Path, lvFileChanges.SelectedItems[0].SubItems[1].Text) + "\"");
            });
            var itemDiscard = fileMenu.Items.Add("🧨 还原", null, async (_, __) => {
                if (lvFileChanges.SelectedItems.Count == 0)
                    return;
                var item = lvFileChanges.SelectedItems[0];
                if (item.Group == grpStaged) {
                    MessageBox.Show("请先 Unstage。");
                    return;
                }

                if (MessageBox.Show("确定丢弃修改？", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                    var r = (GitRepo)lvRepos.SelectedItems[0].Tag;
                    await Task.Run(() => {
                        GitHelper.RunGit(r.Path, $"checkout -- \"{item.SubItems[1].Text}\"", 5000);
                        if (item.Text.Contains("??"))
                            GitHelper.RunGit(r.Path, $"clean -f \"{item.SubItems[1].Text}\"", 5000);
                    });
                    await RefreshRepoDetails();
                }
            });
            lvFileChanges.ContextMenuStrip = fileMenu;
            consoleWindow = new Form {
                Text = "Git 控制台 (独立视图)",
                Width = 1000,
                Height = 700,
                StartPosition = FormStartPosition.CenterScreen,
                Icon = this.Icon,
                ShowInTaskbar = false
            };
            consoleWindow.Controls.Add(grpDetails);
            consoleWindow.FormClosing += (s, e) => {
                if (e.CloseReason == CloseReason.UserClosing) {
                    e.Cancel = true;
                    consoleWindow.Hide();
                    btnToggleConsole.Text = "💻 打开 Git 控制台";
                }
            };

            grpLog = new GroupBox {
                Text = "⑤ 运行日志 (Logs)", Dock = DockStyle.Fill
            };
            txtLog = new TextBox {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                Font = new Font("Consolas", 9),
                BackColor = Color.WhiteSmoke,
                BorderStyle = BorderStyle.None
            };
            grpLog.Controls.Add(txtLog);

            splitMiddle.Panel1.Controls.Add(grpList);
            splitMiddle.Panel2.Controls.Add(grpActions);
            splitUpper.Panel1.Controls.Add(grpTop);
            splitUpper.Panel2.Controls.Add(splitMiddle);
            splitGlobal.Panel1.Controls.Add(splitUpper);
            splitGlobal.Panel2.Controls.Add(grpLog);

            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("就绪") {
                Margin = new Padding(10, 0, 0, 0)
            };
            statusStrip.Items.Add(statusLabel);
            statusStrip.Items.Add(new ToolStripStatusLabel {
                Spring = true
            });
            // [新增] 主题显示标签
            statusTheme = new ToolStripStatusLabel {
                Alignment = ToolStripItemAlignment.Right,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 0, 10, 0)
            };
            statusStrip.Items.Add(statusTheme);
#if !BOSS_MODE && !PURE_MODE
            statusStats = new ToolStripStatusLabel {
                Alignment = ToolStripItemAlignment.Right, ForeColor = Color.SteelBlue, Margin = new Padding(0, 0, 10, 0)
            };
            statusStrip.Items.Add(statusStats);
#endif
            statusProgress = new ToolStripProgressBar {
                Visible = false, Style = ProgressBarStyle.Marquee, Width = 200
            };
            statusStrip.Items.Add(statusProgress);
            Controls.Add(statusStrip);
            Controls.Add(splitGlobal);
            lvRepos.DoubleClick += (_, __) => {
                if (lvRepos.SelectedItems.Count == 0)
                    return;
                var item = lvRepos.SelectedItems[0];
                var repo = (GitRepo)item.Tag;
                lvRepos.BeginUpdate();
                foreach (ListViewItem i in lvRepos.Items) {
                    i.Checked = (i == item);
                }

                lvRepos.EndUpdate();
                if (!string.IsNullOrEmpty(repo.CurrentBranch) && repo.CurrentBranch != "—") {
                    cmbTargetBranch.Text = repo.CurrentBranch;
                }
            };
        }

        // ... (SeedParentsToUi, RenderRepoItem, BatchSyncStatusUpdate 等逻辑) ...
        private void SeedParentsToUi() {
            if (lbParents == null)
                return;
            lbParents.BeginUpdate();
            lbParents.Items.Clear();
            foreach (var p in _settings.ParentPaths) {
                int i = lbParents.Items.Add(p);
                if (_checkedParents.Contains(p))
                    lbParents.SetItemChecked(i, true);
            }

            lbParents.EndUpdate();
        }

        private void RenderRepoItem(ListViewItem item) {
            if (item == null || item.Tag == null)
                return;
            var repo = (GitRepo)item.Tag;
            item.SubItems[1].Text = repo.CurrentBranch;
            item.UseItemStyleForSubItems = false;
            // [修改] 适配深色模式：普通文字颜色跟随列表的前景色，或者手动指定
            Color defaultTextColor = _settings.IsDarkMode ? Color.Gainsboro : Color.Black;

            if (repo.IsDirty)
                item.SubItems[1].ForeColor = Color.ForestGreen; // 绿色在黑底白底都清楚
            else
                item.SubItems[1].ForeColor = defaultTextColor; // [修改] 使用动态颜色
            string syncText = "";
            Color syncColor = Color.Gray;
            Font syncFont = item.Font;
            if (repo.IsSyncChecked) {
                if (!repo.HasUpstream) {
                    syncText = "⚠️ 无远程";
                    syncColor = Color.Gray;
                } else if (repo.Incoming == 0 && repo.Outgoing == 0) {
                    syncText = "✔ 最新";
                    syncColor = defaultTextColor;
                } else {
                    var sb = new List<string>();
                    bool hasPull = repo.Incoming > 0;
                    bool hasPush = repo.Outgoing > 0;
                    if (hasPull)
                        sb.Add($"↓ {repo.Incoming}");
                    if (hasPush)
                        sb.Add($"↑ {repo.Outgoing}");
                    syncText = string.Join(" ", sb);
                    if (hasPush && hasPull)
                        syncColor = Color.Red;
                    else if (hasPull)
                        syncColor = Color.Green;
                    else if (hasPush)
                        syncColor = Color.Red;
                    syncFont = new Font(item.Font, FontStyle.Bold);
                }
            } else {
                syncText = "...";
            }

            item.SubItems[2].Text = syncText;
            item.SubItems[2].ForeColor = syncColor;
            item.SubItems[2].Font = syncFont;
        }

        private async Task BatchSyncStatusUpdate() {
            if (lvRepos.Items.Count == 0)
                return;
            var targetItems = new List<ListViewItem>();
            foreach (ListViewItem i in lvRepos.Items)
                targetItems.Add(i);
            statusLabel.Text = "正在后台扫描同步状态...";
            await Task.Run(() => {
                var opts = new ParallelOptions {
                    MaxDegreeOfParallelism = 10
                };
                Parallel.ForEach(targetItems, opts, (item) => {
                    var repo = (GitRepo)item.Tag;
                    var changes = GitHelper.GetFileChanges(repo.Path);
                    repo.IsDirty = (changes.Count > 0);
                    var syncResult = GitHelper.GetSyncCounts(repo.Path);
                    repo.IsSyncChecked = true;
                    if (syncResult == null) {
                        repo.HasUpstream = false;
                        repo.Incoming = 0;
                        repo.Outgoing = 0;
                    } else {
                        repo.HasUpstream = true;
                        repo.Incoming = syncResult.Value.behind;
                        repo.Outgoing = syncResult.Value.ahead;
                    }

                    try {
                        BeginInvoke((Action)(() => RenderRepoItem(item)));
                    } catch {
                    }
                });
            });
            BeginInvoke((Action)(() => statusLabel.Text = "就绪"));
        }

        private async Task RefreshRepoDetails() {
            if (splitConsole.SplitterDistance < 50)
                splitConsole.SplitterDistance = (int)(splitConsole.Width * 0.4);
            if (lvRepos.SelectedItems.Count == 0) {
                grpDetails.Enabled = false;
                lblRepoInfo.Text = "请选择一个仓库...";
                lvFileChanges.Items.Clear();
                rtbDiff.Clear();
                return;
            }

            grpDetails.Enabled = true;
            var item = lvRepos.SelectedItems[0];
            var repo = (GitRepo)item.Tag;
            lblRepoInfo.Text = $"📂 {repo.Name}  /  📍 {repo.CurrentBranch}";
            await Task.Run(() => {
                var changes = GitHelper.GetFileChanges(repo.Path);
                repo.IsDirty = (changes.Count > 0);
                var syncResult = GitHelper.GetSyncCounts(repo.Path);
                repo.IsSyncChecked = true;
                if (syncResult != null) {
                    repo.HasUpstream = true;
                    repo.Incoming = syncResult.Value.behind;
                    repo.Outgoing = syncResult.Value.ahead;
                } else {
                    repo.HasUpstream = false;
                }

                BeginInvoke((Action)(() => {
                    lvFileChanges.BeginUpdate();
                    lvFileChanges.Items.Clear();
                    int stagedCount = 0;
                    foreach (var c in changes) {
                        char x = c.RawStatus[0];
                        char y = c.RawStatus[1];
                        if (x != ' ' && x != '?') {
                            var lvi = new ListViewItem(x.ToString()) {
                                Group = grpStaged, ForeColor = Color.SeaGreen, Font = new Font(DefaultFont, FontStyle.Bold)
                            };
                            lvi.SubItems.Add(c.FilePath);
                            lvFileChanges.Items.Add(lvi);
                            stagedCount++;
                        }

                        if (y != ' ' || c.RawStatus == "??") {
                            string status = (c.RawStatus == "??")? "??" : y.ToString();
                            var lvi = new ListViewItem(status) {
                                Group = grpUnstaged
                            };
                            if (status == "M")
                                lvi.ForeColor = Color.RoyalBlue;
                            else if (status == "D")
                                lvi.ForeColor = Color.Crimson;
                            else
                                lvi.ForeColor = Color.Gray;
                            lvi.SubItems.Add(c.FilePath);
                            lvFileChanges.Items.Add(lvi);
                        }
                    }

                    lvFileChanges.Columns[1].Width = -2;
                    if (lvFileChanges.Columns[1].Width < 300)
                        lvFileChanges.Columns[1].Width = 300;
                    lvFileChanges.EndUpdate();
                    btnPull.Text = repo.Incoming > 0? $"⬇ {repo.Incoming}" : "⬇ Pull";
                    btnPush.Text = repo.Outgoing > 0? $"⬆ {repo.Outgoing}" : "⬆ Push";
                    btnCommit.Enabled = stagedCount > 0;
                    btnCommit.Text = stagedCount > 0? $"Commit ({stagedCount})" : "Commit";
                    RenderRepoItem(item);
                }));
            });
        }

        private async Task ShowSelectedFileDiff() {
            if (lvFileChanges.SelectedItems.Count == 0 || lvRepos.SelectedItems.Count == 0) {
                rtbDiff.Clear();
                return;
            }

            var repo = (GitRepo)lvRepos.SelectedItems[0].Tag;
            var fileItem = lvFileChanges.SelectedItems[0];
            string filePath = fileItem.SubItems[1].Text;
            bool isStaged = (fileItem.Group == grpStaged);
            bool isUntracked = fileItem.Text.Contains("??") || fileItem.Text.Contains("A") && !isStaged;
            await Task.Run(() => {
                string diffContent = GitHelper.GetFileDiff(repo.Path, filePath, isStaged, isUntracked);
                BeginInvoke((Action)(() => ColorizeDiff(diffContent)));
            });
        }

        private void ColorizeDiff(string content) {
            rtbDiff.Text = "";
            if (string.IsNullOrEmpty(content))
                return;
            string[] lines = content.Split('\n');
            foreach (var line in lines) {
                int start = rtbDiff.TextLength;
                rtbDiff.AppendText(line + "\n");
                rtbDiff.Select(start, line.Length);
                if (line.StartsWith("+") && !line.StartsWith("+++"))
                    rtbDiff.SelectionColor = Color.LightGreen;
                else if (line.StartsWith("-") && !line.StartsWith("---"))
                    rtbDiff.SelectionColor = Color.LightSalmon;
                else if (line.StartsWith("@@"))
                    rtbDiff.SelectionColor = Color.LightSkyBlue;
                else
                    rtbDiff.SelectionColor = Color.Gainsboro;
            }

            rtbDiff.Select(0, 0);
            rtbDiff.ScrollToCaret();
        }

        private async Task ToggleStagedStatus() {
            if (lvFileChanges.SelectedItems.Count == 0 || lvRepos.SelectedItems.Count == 0)
                return;
            var repo = (GitRepo)lvRepos.SelectedItems[0].Tag;
            var item = lvFileChanges.SelectedItems[0];
            string file = item.SubItems[1].Text;
            lvFileChanges.Enabled = false;
            await Task.Run(() => {
                if (item.Group == grpUnstaged)
                    GitHelper.StageFile(repo.Path, file);
                else
                    GitHelper.UnstageFile(repo.Path, file);
            });
            await RefreshRepoDetails();
            lvFileChanges.Enabled = true;
        }

        private async Task RunDetailAction(string action) {
            if (lvRepos.SelectedItems.Count == 0)
                return;
            var repo = (GitRepo)lvRepos.SelectedItems[0].Tag;
            pnlActions.Enabled = false;
            try {
                if (action == "Commit") {
                    string msg = txtCommitMsg.Text.Trim();
                    if (string.IsNullOrEmpty(msg)) {
                        MessageBox.Show("请输入提交信息");
                        return;
                    }

                    var res = await Task.Run(() => GitHelper.Commit(repo.Path, msg));
                    if (res.ok) {
                        txtCommitMsg.Clear();
                        Log($"[{repo.Name}] Commit Success: {msg}");
                    } else
                        MessageBox.Show(res.message);
                } else if (action == "Stash") {
                    await Task.Run(() => GitHelper.RunGit(repo.Path, "stash", 10000));
                    Log($"[{repo.Name}] Stashed");
                } else if (action == "Pull") {
                    var res = await Task.Run(() => GitHelper.PullCurrentBranch(repo.Path));
                    Log($"[{repo.Name}] Pull: {res.message}");
                } else if (action == "Push") {
                    var res = await Task.Run(() => GitHelper.Push(repo.Path));
                    if (res.ok)
                        Log($"[{repo.Name}] Push OK");
                    else
                        MessageBox.Show(res.msg);
                }
            } finally {
                pnlActions.Enabled = true;
                await RefreshRepoDetails();
            }
        }

        private async Task LoadReposForCheckedParentsAsync(bool forceRescan = false) {
            _loadCts?.Cancel();
            _loadCts = new System.Threading.CancellationTokenSource();
            var token = _loadCts.Token;
            var seq = ++_loadSeq;
            lvRepos.BeginUpdate();
            lvRepos.Items.Clear();
            lvRepos.EndUpdate();
            _repos.Clear();
            _allBranches.Clear();
            cmbTargetBranch.Items.Clear();
            var parents = _checkedParents.Where(Directory.Exists).ToList();
            if (!parents.Any()) {
                statusLabel.Text = "就绪";
                SetSwitchState(SwitchState.NotStarted);
                return;
            }

            if (!forceRescan && _settings.RepositoryCache.Count > 0) {
                var finalRepos = new List<(string name, string path, string parent)>();
                bool allFound = true;
                foreach (var p in parents) {
                    var cache = _settings.RepositoryCache.FirstOrDefault(x => string.Equals(x.ParentPath, p, StringComparison.OrdinalIgnoreCase));
                    if (cache != null && cache.Children != null) {
                        foreach (var child in cache.Children)
                            if (Directory.Exists(child.FullPath))
                                finalRepos.Add((child.Name, child.FullPath, Path.GetFileName(p)));
                    } else {
                        allFound = false;
                        break;
                    }
                }

                if (allFound) {
                    lvRepos.BeginUpdate();
                    foreach (var (name, path, parentName) in finalRepos) {
                        var r = new GitRepo(name, path);
                        string display = name == "Root"? $"[{parentName}] (根)" : $"[{parentName}] {name}";
                        lvRepos.Items.Add(new ListViewItem(new[] {
                            "⏳", "—", "", display, path
                        }) {
                            Tag = r, Checked = true
                        });
                    }

                    lvRepos.EndUpdate();
                    statusLabel.Text = "加载完成 (缓存)";
                    StartReadBranches(token);
                    _ = BatchSyncStatusUpdate();
                    return;
                }
            }

            statusLabel.Text = "正在全盘扫描 Git 仓库...";
            statusProgress.Visible = true;
            var foundRepos = await Task.Run(() => {
                var dict = new Dictionary<string, List<SubRepoItem>>();
                foreach (var p in parents) {
                    if (token.IsCancellationRequested)
                        break;
                    var list = new List<SubRepoItem>();
                    foreach (var path in GitHelper.ScanForGitRepositories(p)) {
                        string name = string.Equals(path, p, StringComparison.OrdinalIgnoreCase)? "Root" : path.Substring(p.Length).TrimStart(Path.DirectorySeparatorChar);
                        list.Add(new SubRepoItem {
                            Name = name, FullPath = path
                        });
                    }

                    dict[p] = list;
                }

                return dict;
            });
            if (token.IsCancellationRequested || seq != _loadSeq) {
                statusProgress.Visible = false;
                return;
            }

            foreach (var kvp in foundRepos) {
                var exist = _settings.RepositoryCache.FirstOrDefault(x => string.Equals(x.ParentPath, kvp.Key, StringComparison.OrdinalIgnoreCase));
                if (exist != null)
                    _settings.RepositoryCache.Remove(exist);
                _settings.RepositoryCache.Add(new ParentRepoCache {
                    ParentPath = kvp.Key, Children = kvp.Value
                });
            }

            _settings.Save();
            lvRepos.BeginUpdate();
            foreach (var kvp in foundRepos)
                foreach (var item in kvp.Value) {
                    var r = new GitRepo(item.Name, item.FullPath);
                    string display = item.Name == "Root"? $"[{Path.GetFileName(kvp.Key)}] (根)" : $"[{Path.GetFileName(kvp.Key)}] {item.Name}";
                    lvRepos.Items.Add(new ListViewItem(new[] {
                        "⏳", "—", "", display, item.FullPath
                    }) {
                        Tag = r, Checked = true
                    });
                }

            lvRepos.EndUpdate();
            statusProgress.Visible = false;
            statusLabel.Text = $"扫描完成";
            StartReadBranches(token);
            _ = BatchSyncStatusUpdate();
        }

        private void StartReadBranches(System.Threading.CancellationToken token) {
            var tasks = new List<Task>();
            foreach (ListViewItem item in lvRepos.Items) {
                tasks.Add(Task.Run(() => {
                    if (token.IsCancellationRequested)
                        return;
                    ((GitRepo)item.Tag).CurrentBranch = GitHelper.GetFriendlyBranch(((GitRepo)item.Tag).Path);
                }));
            }

            _ = Task.WhenAll(tasks).ContinueWith(t => {
                if (token.IsCancellationRequested)
                    return;
                BeginInvoke((Action)(() => {
                    lvRepos.BeginUpdate();
                    foreach (ListViewItem item in lvRepos.Items)
                        RenderRepoItem(item);
                    lvRepos.EndUpdate();
                    RefreshBranchesAsync();
                    _ = AutoFetchAndRefreshAsync(token);
                }));
            });
        }

        private async Task AutoFetchAndRefreshAsync(System.Threading.CancellationToken token) {
            try {
                var allPaths = new List<string>();
                var rootPaths = new List<string>();
                foreach (ListViewItem item in lvRepos.Items) {
                    if (item.Tag is GitRepo r) {
                        allPaths.Add(r.Path);
                        if (r.Name == "Root")
                            rootPaths.Add(r.Path);
                    }
                }

                if (allPaths.Count == 0)
                    return;
                var targetPaths = rootPaths.Count > 0? rootPaths : allPaths;
                lblFetchStatus.Text = rootPaths.Count > 0? $"📡 正在同步 {targetPaths.Count} 个主仓库..." : $"📡 正在同步 {targetPaths.Count} 个仓库...";
                await Task.Run(() => {
                    var opts = new ParallelOptions {
                        MaxDegreeOfParallelism = 8
                    };
                    Parallel.ForEach(targetPaths, opts, (path) => {
                        if (token.IsCancellationRequested)
                            return;
                        GitHelper.FetchFast(path);
                    });
                });
                if (token.IsCancellationRequested)
                    return;
                BeginInvoke((Action)(() => {
                    lblFetchStatus.Text = "";
                    RefreshBranchesAsync();
                }));
                await BatchSyncStatusUpdate();
            } catch {
            }
        }

        private async Task RefreshBranchesAsync() {
            if (lvRepos == null || lvRepos.IsDisposed || lvRepos.Items.Count == 0)
                return;
            var targetPaths = new List<string>();
            foreach (ListViewItem item in lvRepos.Items) {
                if (item.Tag is GitRepo r && !string.IsNullOrEmpty(r.Path))
                    targetPaths.Add(r.Path);
            }

            var all = new HashSet<string>();
            var tasks = new List<Task<IEnumerable<string>>>();
            foreach (var path in targetPaths)
                tasks.Add(Task.Run(() => GitHelper.GetAllBranches(path)));
            try {
                var results = await Task.WhenAll(tasks);
                foreach (var list in results)
                    if (list != null)
                        foreach (var b in list)
                            all.Add(b);
            } catch (Exception ex) {
                Log($"Err: {ex.Message}");
            }

            _allBranches = all.OrderBy(x => x).ToList();
            if (_allBranches.Count > 0) {
                if (_settings.CachedBranchList == null)
                    _settings.CachedBranchList = new List<string>();
                _settings.CachedBranchList = _allBranches;
                _settings.Save();
            }

            if (cmbTargetBranch != null && !cmbTargetBranch.IsDisposed)
                UpdateBranchDropdown();
        }

        private void UpdateBranchDropdown() {
            try {
                if (cmbTargetBranch == null || cmbTargetBranch.IsDisposed)
                    return;
                string currentText = cmbTargetBranch.Text;
                cmbTargetBranch.BeginUpdate();
                cmbTargetBranch.Items.Clear();
                var src = _allBranches?.ToList() ?? new List<string>();
                var list = string.IsNullOrEmpty(currentText)? src : src.Where(b => b != null && b.IndexOf(currentText, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                foreach (var b in list.Take(500))
                    cmbTargetBranch.Items.Add(b);
                cmbTargetBranch.EndUpdate();
                cmbTargetBranch.Text = currentText;
                if (!string.IsNullOrEmpty(currentText)) {
                    cmbTargetBranch.SelectionStart = currentText.Length;
                }

                if (list.Count > 0 && cmbTargetBranch.Focused && !string.IsNullOrEmpty(currentText)) {
                    cmbTargetBranch.DroppedDown = true;
                    Cursor.Current = Cursors.Default;
                }
            } catch {
            }
        }

        private bool ShowSwitchConfirmDialog(string targetBranch) {
            using var form = new Form {
                Text = "⚠️ 高危操作确认",
                Width = 450,
                Height = 280,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.White
            };
            var lblTitle = new Label {
                Text = "您即将执行一键切线操作，目标分支：",
                AutoSize = true,
                Location = new Point(25, 25),
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.DimGray
            };
            var lblBranch = new Label {
                Text = targetBranch,
                AutoSize = true,
                Location = new Point(25, 60),
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.Crimson
            };
            var lblHint = new Label {
                Text = "此操作将影响所有选中的仓库，请确认无误。",
                AutoSize = true,
                Location = new Point(25, 110),
                Font = new Font("Segoe UI", 9, FontStyle.Italic),
                ForeColor = Color.Gray
            };
            var btnOk = new Button {
                Text = "🚀 确认切线",
                DialogResult = DialogResult.OK,
                Width = 160,
                Height = 50,
                Location = new Point(40, 160),
                BackColor = Color.ForestGreen,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnOk.FlatAppearance.BorderSize = 0;
            var btnCancel = new Button {
                Text = "❌ 取消",
                DialogResult = DialogResult.Cancel,
                Width = 160,
                Height = 50,
                Location = new Point(220, 160),
                BackColor = Color.IndianRed,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            form.Controls.AddRange(new Control[] {
                lblTitle, lblBranch, lblHint, btnOk, btnCancel
            });
            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;
            return form.ShowDialog(this) == DialogResult.OK;
        }

        private void AdjustPbSizeMode(PictureBox pb) {
            if (pb.Image == null)
                return;
            if (pb.Image.Width > pb.Width || pb.Image.Height > pb.Height) {
                pb.SizeMode = PictureBoxSizeMode.Zoom;
            } else {
                pb.SizeMode = PictureBoxSizeMode.CenterImage;
            }
        }

        // 启动旅行动画
        private void StartFrogTravel() {
            LoadRandomFrameWorkImage();
            lblStateText.Text = "🐸 呱呱去旅行了...";
            lblStateText.ForeColor = Color.ForestGreen;
        }

        // [修改] 抽卡核心逻辑：支持传入 RepoCount 调整概率，且 SSR/UR 优先未收录
        private async Task FinishFrogTravelAndDrawCard(int repoCount) {
            string baseLibPath = Path.Combine(_settings.UpdateSourcePath, "Img");

            if (!Directory.Exists(baseLibPath)) {
                try {
                    Directory.CreateDirectory(baseLibPath);
                    foreach (var r in Enum.GetNames(typeof(Rarity)))
                        Directory.CreateDirectory(Path.Combine(baseLibPath, r));
                } catch {
                }
            }

            // 1. 决定稀有度
            var rarity = RollRarity(repoCount);
            string rarityPath = Path.Combine(baseLibPath, rarity.ToString());

            // [关键修改] 调用带优先级的选图逻辑
            string imagePath = GetImageWithPriority(rarityPath, rarity);

            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath)) {
                try {
                    using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read)) {
                        pbState.Image = Image.FromStream(fs);
                    }

                    AdjustPbSizeMode(pbState);

                    string fileName = Path.GetFileName(imagePath);
                    string displayName = Path.GetFileNameWithoutExtension(fileName);

                    lblStateText.ForeColor = _rarityColors.ContainsKey(rarity)? _rarityColors[rarity] : Color.Black;
                    string rarityLabel = rarity == Rarity.UR? "🌟UR🌟" : rarity.ToString();
                    string msg = $"带回了: {displayName} [{rarityLabel}]";

                    // 2. 判断是否新卡
                    bool isNew = !_myCollection.Any(x => string.Equals(x.FileName, fileName, StringComparison.OrdinalIgnoreCase));
                    if (isNew) {
                        int score = _rarityScores.ContainsKey(rarity)? _rarityScores[rarity] : 1;
                        var newItem = new CollectedItem {
                            FileName = fileName, Rarity = rarity.ToString(), Score = score, CollectTime = DateTime.Now
                        };
                        _myCollection.Add(newItem);
                        CollectionService.Save(_settings.UpdateSourcePath, Environment.UserName, _myCollection);
                        msg += " (NEW!)";
                    }

                    // 3. 计算当前总分并上传
                    int totalScore = _myCollection.Sum(x => x.Score);

#if !BOSS_MODE && !PURE_MODE
                    if (!string.IsNullOrEmpty(_settings.LeaderboardPath)) {
                        await LeaderboardService.UploadMyScoreAsync(0, 0, _myCollection.Count, totalScore);
                    }
#endif
                    lblStateText.Text = msg;

                    // 闪光特效
                    if (rarity == Rarity.SSR || rarity == Rarity.UR) {
                        var originalColor = statePanel.BackColor;
                        statePanel.BackColor = Color.Gold;
                        flashTimer.Start();
                        await Task.Delay(500);
                        statePanel.BackColor = originalColor;
                        await Task.Delay(200);
                        statePanel.BackColor = Color.Gold;
                        await Task.Delay(500);
                        statePanel.BackColor = originalColor;
                    }
                } catch (Exception ex) {
                    lblStateText.Text = "明信片污损了...";
                    Log($"Load Image Error: {ex.Message}");
                }
            } else {
                lblStateText.Text = $"🐸 去了{rarity}区但空手而归...";
                lblStateText.ForeColor = Color.Gray;
                LoadRandomFrameWorkImage();
            }
        }

        // [核心修改] 根据 repoCount 切换概率表
        private Rarity RollRarity(int repoCount) {
            // 如果仓库数 >= 5，使用欧皇池；否则使用非酋池
            var weights = (repoCount >= 5)? _rarityWeightsHigh : _rarityWeightsLow;

            int totalWeight = weights.Values.Sum();
            int roll = new Random().Next(0, totalWeight);
            int current = 0;
            foreach (var kvp in weights) {
                current += kvp.Value;
                if (roll < current)
                    return kvp.Key;
            }

            return Rarity.N;
        } 
        
        // [新增] 智能选图逻辑：SSR 和 UR 优先获取未收集的图片
        private string GetImageWithPriority(string folderPath, Rarity rarity) {
            if (!Directory.Exists(folderPath))
                return null;

            // 获取该稀有度下的所有图片
            var files = Directory.GetFiles(folderPath, "*.*").Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)).ToList();

            if (files.Count == 0)
                return null;

            // === 核心逻辑 ===
            // 只有 SSR 和 UR 启用"防重机制"
            if (rarity == Rarity.SSR || rarity == Rarity.UR) {
                // 1. 找出当前用户已拥有的该稀有度的图片文件名
                var collectedNames = new HashSet<string>(_myCollection.Select(c => c.FileName), StringComparer.OrdinalIgnoreCase);

                // 2. 筛选出未收集的图片
                var uncollectedFiles = files.Where(f => !collectedNames.Contains(Path.GetFileName(f))).ToList();

                // 3. 如果有未收集的，优先从中随机抽取一张
                if (uncollectedFiles.Count > 0) {
                    return uncollectedFiles[new Random().Next(uncollectedFiles.Count)];
                }
                // 如果全都收集齐了，则进入下面的逻辑（随机重复卡）
            }

            // N, R, SR 或者 高稀有度已全收集：完全随机
            return files[new Random().Next(files.Count)];
        }

        private async void ShowLeaderboard() {
            if (_leaderboardForm != null && !_leaderboardForm.IsDisposed) {
                if (_leaderboardForm.WindowState == FormWindowState.Minimized)
                    _leaderboardForm.WindowState = FormWindowState.Normal;
                _leaderboardForm.BringToFront();
                _leaderboardForm.Activate();
                return;
            }

            if (string.IsNullOrEmpty(_settings.LeaderboardPath)) {
                string input = ShowInputBox("设置", "请输入共享文件路径:", _settings.LeaderboardPath);
                if (string.IsNullOrWhiteSpace(input))
                    return;
                _settings.LeaderboardPath = input;
                _settings.Save();
                LeaderboardService.SetPath(input);
            }

            _leaderboardForm = new Form {
                Text = "👑 卷王 & 摸鱼王 & 欧皇排行榜",
                Width = 1000,
                Height = 500,
                StartPosition = FormStartPosition.CenterScreen,
                Icon = this.Icon
            };
            var table = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));

            var listCount = new ListView {
                Dock = DockStyle.Fill, View = View.Details, GridLines = true, FullRowSelect = true
            };
            listCount.Columns.Add("排名", 40);
            listCount.Columns.Add("用户", 180);
            listCount.Columns.Add("次数", 60);
            var listDuration = new ListView {
                Dock = DockStyle.Fill, View = View.Details, GridLines = true, FullRowSelect = true
            };
            listDuration.Columns.Add("排名", 40);
            listDuration.Columns.Add("用户", 180);
            listDuration.Columns.Add("时长", 80);
            var listCollection = new ListView {
                Dock = DockStyle.Fill, View = View.Details, GridLines = true, FullRowSelect = true
            };
            listCollection.Columns.Add("排名", 40);
            listCollection.Columns.Add("欧皇", 180);
            listCollection.Columns.Add("欧气(张)", 80);

            table.Controls.Add(listCount, 0, 0);
            table.Controls.Add(listDuration, 1, 0);
            table.Controls.Add(listCollection, 2, 0);
            var lblMy = new Label {
                Dock = DockStyle.Bottom,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(DefaultFont, FontStyle.Bold),
                Text = "正在加载数据..."
            };
            _leaderboardForm.Controls.Add(table);
            _leaderboardForm.Controls.Add(lblMy);

            _leaderboardForm.Shown += async (_, __) => {
                var data = await LeaderboardService.GetLeaderboardAsync();

                var sortedCount = data.OrderByDescending(x => x.TotalSwitches).ToList();
                for (int i = 0; i < sortedCount.Count; i++) {
                    var u = sortedCount[i];
                    string name = u.Name;
                    if (i == 0)
                        name = $"🥇 {u.Name} (🌭切线王)";
                    listCount.Items.Add(new ListViewItem(new[] {
                        (i + 1).ToString(), name, u.TotalSwitches.ToString()
                    }));
                }

                var sortedTime = data.OrderByDescending(x => x.TotalDuration).ToList();
                for (int i = 0; i < sortedTime.Count; i++) {
                    var u = sortedTime[i];
                    string name = u.Name;
                    if (i == 0)
                        name = $"👑 {u.Name} (🐟摸鱼王)";
                    listDuration.Items.Add(new ListViewItem(new[] {
                        (i + 1).ToString(), name, FormatDuration(u.TotalDuration)
                    }));
                }

                var sortedColl = data.OrderByDescending(x => x.TotalCollectionScore).ThenByDescending(x => x.TotalCardsCollected).ToList();
                int rank = 1;
                for (int i = 0; i < sortedColl.Count; i++) {
                    var u = sortedColl[i];
                    if (u.TotalCollectionScore <= 0 && u.TotalCardsCollected <= 0)
                        continue;
                    string name = u.Name;
                    if (rank == 1)
                        name = $"🐶 {u.Name} (狗运王)";
                    listCollection.Items.Add(new ListViewItem(new[] {
                        rank.ToString(), name, $"{u.TotalCollectionScore} ({u.TotalCardsCollected})"
                    }));
                    rank++;
                }

                var me = data.FirstOrDefault(x => x.Name == Environment.UserName);
                if (me != null) {
                    lblMy.Text = $"我：切线{me.TotalSwitches}次 | 摸鱼{FormatDuration(me.TotalDuration)} | 欧气{me.TotalCollectionScore}分";
                } else {
                    lblMy.Text = "暂无数据";
                }
            };
            _leaderboardForm.Show();
        }

        private async Task SwitchAllAsync() {
            var target = cmbTargetBranch.Text.Trim();
            if (string.IsNullOrEmpty(target)) {
                MessageBox.Show("请输入分支名");
                return;
            }

            if (_settings.ConfirmOnSwitch) {
                if (!ShowSwitchConfirmDialog(target))
                    return;
            }

            var items = lvRepos.Items.Cast<ListViewItem>().Where(i => i.Checked).ToList();
            if (!items.Any())
                return;
            var targetRepos = items.Select(i => (GitRepo)i.Tag).ToList();

            btnSwitchAll.Enabled = false;
            statusProgress.Visible = true;

            StartFrogTravel();

            var progressHandler = new Progress<RepoSwitchResult>(result => {
                var item = items.FirstOrDefault(x => x.Tag == result.Repo);
                if (item != null) {
                    item.Text = (result.Success? "✅" : "❌") + $" {result.DurationSeconds:F1}s";
                    RenderRepoItem(item);
                    Log($"[{result.Repo.Name}] {result.Message?.Replace("\n", " ")}");
                }

                statusLabel.Text = $"处理中 {result.ProgressIndex}/{result.TotalCount}";
            });

            double totalSeconds = await _workflowService.SwitchReposAsync(targetRepos, target, _settings.StashOnSwitch, _settings.FastMode, progressHandler);

#if !BOSS_MODE && !PURE_MODE
            if (!string.IsNullOrEmpty(_settings.LeaderboardPath)) {
                // [修复开始] 计算当前已有的卡片数和分数，并在上传时传入
                int currentCardCount = _myCollection.Count;
                int currentScore = _myCollection.Sum(x => x.Score);

                // 原代码是: UploadMyScoreAsync(totalSeconds, 0, null, null);
                // 修改为传入 currentCardCount 和 currentScore:
                var (nc, nt, ns) = await LeaderboardService.UploadMyScoreAsync(totalSeconds, 0, currentCardCount, currentScore);
                UpdateStatsUi(nc, nt, ns);
                // [修复结束]
            }
#endif

            // [核心修改] 传入本次切线的仓库数量
            await FinishFrogTravelAndDrawCard(targetRepos.Count);

            statusProgress.Visible = false;
            btnSwitchAll.Enabled = true;
            statusLabel.Text = "完成";
            Log("🏁 全部完成");
        }

        private void TrySetRuntimeIcon() {
            try {
                var icon = ImageHelper.LoadIconFromResource("appicon");
                if (icon != null)
                    this.Icon = icon;
            } catch {
            }
        }

        private void SetSwitchState(SwitchState st) {
            // 每次状态改变，都随机换一张图
            LoadRandomFrameWorkImage();

            if (st == SwitchState.NotStarted) {
                lblStateText.Text = "Ready"; // 或者 "未开始"
                lblStateText.ForeColor = Color.Gray;
            }
            else if (st == SwitchState.Switching) {
                lblStateText.Text = "切线中...";
                lblStateText.ForeColor = Color.DodgerBlue;
            }
            else if (st == SwitchState.Done) {
                lblStateText.Text = "搞定!";
                lblStateText.ForeColor = Color.ForestGreen;
            }
        }

        // [重写] MainForm.cs -> LoadRandomFrameWorkImage 方法
        private void LoadRandomFrameWorkImage() {
            try {
                // 0. 清理旧图片 (通用操作)
                if (pbState.Image != null) {
                    var old = pbState.Image;
                    pbState.Image = null;
                    old.Dispose();
                }

                string theme = _settings.SelectedTheme;

                // === Case 1: 无主题 (默认) ===
                if (string.IsNullOrEmpty(theme) || theme == THEME_NONE) {
                    // 保持 Image 为 null 即可
                    return;
                }

                string imagePathToLoad = null;

                // === Case 2: 收藏品模式 ===
                if (theme == THEME_COLLECTION) {
                    if (_myCollection.Count == 0)
                        return; // 没东西可显示

                    CollectedItem targetItem = null;

                    if (_settings.SelectedCollectionItem == "Random" || string.IsNullOrEmpty(_settings.SelectedCollectionItem)) {
                        // 随机选一张
                        targetItem = _myCollection[new Random().Next(_myCollection.Count)];
                    } else {
                        // 找指定的图片
                        targetItem = _myCollection.FirstOrDefault(x => x.FileName == _settings.SelectedCollectionItem);
                        // 如果找不到(可能被删了)，回退到随机
                        if (targetItem == null)
                            targetItem = _myCollection[new Random().Next(_myCollection.Count)];
                    }

                    if (targetItem != null) {
                        // 拼接完整路径: UpdateSourcePath/Img/{Rarity}/{FileName}
                        imagePathToLoad = Path.Combine(_settings.UpdateSourcePath, "Img", targetItem.Rarity, targetItem.FileName);
                    }
                }
                // === Case 3: 文件夹主题模式 ===
                else {
                    string rootPath = _settings.FrameWorkImgPath;
                    string themePath = Path.Combine(rootPath, theme);

                    if (Directory.Exists(themePath)) {
                        var files = Directory.GetFiles(themePath, "*.*").Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)).ToList();

                        if (files.Count > 0) {
                            imagePathToLoad = files[new Random().Next(files.Count)];
                        }
                    }
                }

                // === 执行加载 ===
                if (!string.IsNullOrEmpty(imagePathToLoad) && File.Exists(imagePathToLoad)) {
                    byte[] fileBytes = File.ReadAllBytes(imagePathToLoad);
                    var ms = new MemoryStream(fileBytes);
                    pbState.Image = Image.FromStream(ms);
                    AdjustPbSizeMode(pbState);
                }
            } catch (Exception ex) {
                Log($"[UI] Load Image Error: {ex.Message}");
            }
        }

        private void UpdateStatsUi(int totalCount = -1, double totalSeconds = -1, long totalSpace = -1) {
            if (statusStats != null) {
                int c = totalCount >= 0? totalCount : _settings.TodaySwitchCount;
                double t = totalSeconds >= 0? totalSeconds : _settings.TodayTotalSeconds;
                statusStats.Text = $"📅 累计：切线 {c} 次 | 摸鱼 {FormatDuration(t)}";
            }
        }

        private string FormatDuration(double seconds) {
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}小时{ts.Minutes}分{ts.Seconds}秒";
            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}分{ts.Seconds}秒";
            return $"{ts.Seconds}秒";
        }

        private string ShowInputBox(string title, string prompt, string defaultVal) {
            Form promptForm = new Form() {
                Width = 500,
                Height = 150,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = title,
                StartPosition = FormStartPosition.CenterParent
            };
            Label textLabel = new Label() {
                Left = 20, Top = 20, Text = prompt, AutoSize = true
            };
            TextBox textBox = new TextBox() {
                Left = 20, Top = 50, Width = 440, Text = defaultVal
            };
            Button confirmation = new Button() {
                Text = "确定",
                Left = 360,
                Width = 100,
                Top = 80,
                DialogResult = DialogResult.OK
            };
            promptForm.Controls.Add(textLabel);
            promptForm.Controls.Add(textBox);
            promptForm.Controls.Add(confirmation);
            promptForm.AcceptButton = confirmation;
            return promptForm.ShowDialog() == DialogResult.OK? textBox.Text : "";
        }

        private string FormatSize(long bytes) {
            if (bytes == 0)
                return "0B";
            string prefix = bytes < 0? "-" : "";
            long absBytes = Math.Abs(bytes);
            if (absBytes < 1024)
                return $"{prefix}{absBytes}B";
            long gb = absBytes / (1024 * 1024 * 1024);
            long rem = absBytes % (1024 * 1024 * 1024);
            long mb = rem / (1024 * 1024);
            rem = rem % (1024 * 1024);
            long kb = rem / 1024;
            var sb = new StringBuilder();
            sb.Append(prefix);
            if (gb > 0)
                sb.Append($"{gb}GB ");
            if (mb > 0)
                sb.Append($"{mb}MB ");
            if (kb > 0)
                sb.Append($"{kb}KB");
            return sb.ToString().Trim();
        }

        private void Log(string s) => txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");

        private async void StartSuperSlimProcess() {
            if (MessageBox.Show("【一键瘦身】将执行深度 GC，非常耗时。\n建议下班挂机执行。是否继续？", "确认 (1/2)", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            if (MessageBox.Show("CPU 将会满载。\n真的要继续吗？", "确认 (2/2)", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            var selectedParents = ShowParentSelectionDialog();
            if (selectedParents.Count == 0)
                return;
            this.Enabled = false;
            long totalSavedBytes = 0;
            foreach (var parent in selectedParents) {
                var cache = _settings.RepositoryCache.FirstOrDefault(x => string.Equals(x.ParentPath, parent, StringComparison.OrdinalIgnoreCase));
                if (cache == null || cache.Children.Count == 0)
                    continue;
                Log($"=== 清理父节点: {Path.GetFileName(parent)} ===");
                foreach (var repoInfo in cache.Children) {
                    Log($" >>> [清理中] {repoInfo.Name} ...");
                    statusLabel.Text = $"正在瘦身: {repoInfo.Name}";
                    var (ok, log, sizeStr, saved) = await Task.Run(() => GitHelper.GarbageCollect(repoInfo.FullPath, false));
                    if (ok) {
                        totalSavedBytes += saved;
                        Log($"[成功] {repoInfo.Name}: 减小 {sizeStr}");
                    } else
                        Log($"[失败] {repoInfo.Name}");
                }
            }

            this.Enabled = true;
            statusLabel.Text = "清理完成";
#if !BOSS_MODE && !PURE_MODE
            if (!string.IsNullOrEmpty(_settings.LeaderboardPath)) {
                await LeaderboardService.UploadMyScoreAsync(0, totalSavedBytes, null, null);
            }
#endif
            MessageBox.Show($"🎉 清理完毕！\n节省空间: {FormatSize(totalSavedBytes)}", "完成");
        }

        private List<string> ShowParentSelectionDialog() {
            var form = new Form {
                Text = "选择要清理的目录",
                Width = 400,
                Height = 300,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false
            };
            var clb = new CheckedListBox {
                Top = 10,
                Left = 10,
                Width = 360,
                Height = 200,
                CheckOnClick = true
            };
            var btnOk = new Button {
                Text = "开始", Top = 220, Left = 150, DialogResult = DialogResult.OK
            };
            foreach (var p in _settings.ParentPaths)
                clb.Items.Add(p, true);
            form.Controls.Add(clb);
            form.Controls.Add(btnOk);
            form.AcceptButton = btnOk;
            if (form.ShowDialog() == DialogResult.OK) {
                var r = new List<string>();
                foreach (var i in clb.CheckedItems)
                    r.Add(i.ToString());
                return r;
            }

            return new List<string>();
        }

        // [重写] MainForm.cs -> ShowThemeSettingsDialog 方法
        private void ShowThemeSettingsDialog() {
            string rootPath = _settings.FrameWorkImgPath;

            // 1. 准备主题列表
            var themeList = new List<string> {
                THEME_NONE, THEME_COLLECTION
            }; // 固定选项
            if (Directory.Exists(rootPath)) {
                var dirs = Directory.GetDirectories(rootPath);
                themeList.AddRange(dirs.Select(d => Path.GetFileName(d)));
            }

            using var form = new Form {
                Text = "界面设置",
                Width = 450, // 稍微加宽以容纳长文件名
                Height = 350,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = _settings.IsDarkMode? Color.FromArgb(32, 32, 32) : Color.WhiteSmoke,
                ForeColor = _settings.IsDarkMode? Color.Gainsboro : Color.Black
            };

            // === UI 控件 ===
            var lblTheme = new Label {
                Text = "🎨 主题风格:",
                Top = 20,
                Left = 20,
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            var cmbThemes = new ComboBox {
                Top = 50,
                Left = 20,
                Width = 390,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10),
                BackColor = _settings.IsDarkMode? Color.FromArgb(45, 45, 48) : Color.White,
                ForeColor = _settings.IsDarkMode? Color.Gainsboro : Color.Black
            };
            cmbThemes.Items.AddRange(themeList.ToArray());

            // [新增] 收藏品选择区域 (默认隐藏)
            var lblColl = new Label {
                Text = "🖼️ 选择展示的收藏品:",
                Top = 90,
                Left = 20,
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Visible = false
            };

            var cmbCollection = new ComboBox {
                Top = 120,
                Left = 20,
                Width = 390,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10),
                BackColor = _settings.IsDarkMode? Color.FromArgb(45, 45, 48) : Color.White,
                ForeColor = _settings.IsDarkMode? Color.Gainsboro : Color.Black,
                Visible = false
            };

            // 填充收藏品列表
            cmbCollection.Items.Add(COLL_RANDOM);
            // 按稀有度排序：UR > SSR > SR > R > N
            var sortedCollection = _myCollection.OrderByDescending(x => GetRarityWeight(x.Rarity)).ThenByDescending(x => x.CollectTime).ToList();

            foreach (var item in sortedCollection) {
                cmbCollection.Items.Add($"[{item.Rarity}] {Path.GetFileNameWithoutExtension(item.FileName)}");
            }

            // === 联动逻辑 ===
            cmbThemes.SelectedIndexChanged += (_, __) => {
                bool isColl = cmbThemes.SelectedItem?.ToString() == THEME_COLLECTION;
                lblColl.Visible = isColl;
                cmbCollection.Visible = isColl;

                // 调整窗体布局（如果是收藏模式，把下面的控件往下推）
                int offset = isColl? 70 : 0;
                // 这里只是简单的动态布局示意，实际可以用 Panel
            };

            // === 初始化选中状态 ===
            string currentTheme = _settings.SelectedTheme;
            if (string.IsNullOrEmpty(currentTheme))
                currentTheme = THEME_NONE; // 默认无主题

            if (themeList.Contains(currentTheme))
                cmbThemes.SelectedItem = currentTheme;
            else
                cmbThemes.SelectedIndex = 0; // 默认选第一项

            // 初始化收藏品选中
            if (_settings.SelectedCollectionItem == "Random" || string.IsNullOrEmpty(_settings.SelectedCollectionItem)) {
                cmbCollection.SelectedIndex = 0;
            } else {
                // 尝试通过文件名匹配
                string target = _settings.SelectedCollectionItem;
                for (int i = 0; i < cmbCollection.Items.Count; i++) {
                    if (cmbCollection.Items[i].ToString().Contains(target)) {
                        cmbCollection.SelectedIndex = i;
                        break;
                    }
                }
            }

            // === 其他控件 ===
            var chkDarkMode = new CheckBox {
                Text = "🌙 开启深色模式 (Dark Mode)",
                Top = 200,
                Left = 20,
                Width = 390, // 位置稍微下移
                Font = new Font("Segoe UI", 10),
                Checked = _settings.IsDarkMode,
                Cursor = Cursors.Hand
            };

            var btnOk = new Button {
                Text = "💾 保存并应用",
                Top = 240,
                Left = 20,
                Width = 390,
                Height = 40,
                BackColor = Color.DodgerBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.OK
            };
            btnOk.FlatAppearance.BorderSize = 0;

            form.Controls.AddRange(new Control[] {
                lblTheme, cmbThemes, lblColl, cmbCollection, chkDarkMode, btnOk
            });
            form.AcceptButton = btnOk;

            // 触发一次联动以设置初始可见性
            // Hack: 手动调用事件处理逻辑
            bool showColl = cmbThemes.SelectedItem?.ToString() == THEME_COLLECTION;
            lblColl.Visible = showColl;
            cmbCollection.Visible = showColl;

            // === 保存逻辑 ===
            if (form.ShowDialog(this) == DialogResult.OK) {
                bool needApply = false;

                // 1. 保存主题
                string newTheme = cmbThemes.SelectedItem?.ToString();
                if (newTheme == THEME_NONE)
                    newTheme = ""; // 空字符串代表无主题

                if (newTheme != _settings.SelectedTheme) {
                    _settings.SelectedTheme = newTheme;
                    needApply = true;
                }

                // 2. 保存收藏品设置
                if (newTheme == THEME_COLLECTION) {
                    if (cmbCollection.SelectedIndex == 0) {
                        _settings.SelectedCollectionItem = "Random";
                    } else {
                        // 从显示的文本 "[SSR] Name" 中提取真实文件名
                        // 对应上面的 sortedCollection 索引 (注意索引 -1 因为第0项是Random)
                        int index = cmbCollection.SelectedIndex - 1;
                        if (index >= 0 && index < sortedCollection.Count) {
                            _settings.SelectedCollectionItem = sortedCollection[index].FileName;
                        }
                    }

                    needApply = true; // 即使主题没变，换了图片也要刷新
                }

                // 3. 保存深色模式
                if (chkDarkMode.Checked != _settings.IsDarkMode) {
                    _settings.IsDarkMode = chkDarkMode.Checked;
                    ApplyThemeColors();
                    needApply = true;
                }

                if (needApply) {
                    _settings.Save();
                    UpdateThemeLabel(); // 更新状态栏文字
                    LoadRandomFrameWorkImage(); // 立即刷新图片
                    MessageBox.Show("设置已保存！");
                }
            }
        }

        // [辅助] 稀有度权重排序
        private int GetRarityWeight(string r) {
            if (r == "UR")
                return 5;
            if (r == "SSR")
                return 4;
            if (r == "SR")
                return 3;
            if (r == "R")
                return 2;
            return 1;
        }
        
        private void ApplyThemeColors() {
            bool dark = _settings.IsDarkMode;

            // 定义调色板
            Color formBack = dark ? Color.FromArgb(32, 32, 32) : Color.WhiteSmoke;
            Color formFore = dark ? Color.Gainsboro : Color.Black;
            Color controlBack = dark ? Color.FromArgb(45, 45, 48) : Color.White;
            Color controlFore = dark ? Color.Gainsboro : Color.Black;
    
            // 1. 设置主窗体
            this.BackColor = formBack;
            this.ForeColor = formFore;

            // 2. 设置容器标题颜色 (GroupBox)
            Control[] groups = { grpTop, grpList, grpActions, grpDetails, grpLog };
            foreach (var g in groups) {
                if (g != null) g.ForeColor = formFore;
            }

            // 3. 设置列表和输入框 (List/Edit)
            Control[] lists = { lbParents, lvRepos, lvFileChanges, txtLog, txtCommitMsg, cmbTargetBranch, txtCommitMsg };
            foreach (var c in lists) {
                if (c != null) {
                    c.BackColor = controlBack;
                    c.ForeColor = controlFore;
                }
            }

            // 4. 设置标签 (Label)
            // 排除 lblStateText 因为它是动态颜色的
            Control[] labels = { lblTargetBranch, lblRepoInfo, lblFetchStatus };
            foreach (var l in labels) {
                if (l != null) l.ForeColor = formFore;
            }

            // 5. 特殊处理独立窗口
            if (consoleWindow != null) {
                consoleWindow.BackColor = formBack;
                consoleWindow.ForeColor = formFore;
            }
    
            // 6. 强制刷新列表项颜色 (因为 ListViewItem 颜色可能是之前渲染的)
            if (lvRepos != null && lvRepos.Items.Count > 0) {
                lvRepos.BeginUpdate();
                foreach (ListViewItem item in lvRepos.Items) {
                    RenderRepoItem(item);
                }
                lvRepos.EndUpdate();
            }
            if (statusTheme != null) {
                statusTheme.ForeColor = _settings.IsDarkMode ? Color.Gray : Color.DimGray;
            }
        }
    }
}