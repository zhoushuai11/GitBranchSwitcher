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

            // ========================================================
            // [修改] 启动时自动登录共享文件夹，并检查结果
            // ========================================================
            try {
                // 这里的路径必须是共享根目录 (不包含子文件夹)
                string shareRoot = @"\\s4.biubiubiu.io\share"; 
                string user = "sausage";
                string pass = "sausage@0592";

                int result = NetworkShare.Connect(shareRoot, user, pass);
        
                // 0 = 成功, 85 = 已经连接
                if (result != 0 && result != 85) {
                    // 如果连接失败，记录日志或者弹窗（调试阶段建议弹窗，稳定后可移除）
                    // MessageBox.Show($"连接共享服务器失败，错误代码: {result}\n部分功能可能无法使用。", "网络警告");
                }
            } catch (Exception ex) {
                // System.Diagnostics.Debug.WriteLine($"[Network] 连接异常: {ex.Message}");
            }
            // ========================================================

            _myCollection = CollectionService.Load(_settings.UpdateSourcePath, Environment.UserName);

            InitializeComponent();
            
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
            statusTheme.ForeColor = Color.DimGray;
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
    
                // [修改] 强转为 ParentFolderItem 获取 Path
                foreach (ParentFolderItem i in lbParents.SelectedItems)
                    rm.Add(i.Path);
                foreach (ParentFolderItem i in lbParents.CheckedItems)
                    rm.Add(i.Path);
        
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
                        // [修改] 获取 Path
                        _checkedParents.Add(((ParentFolderItem)item).Path);
                }

                for (int i = 0; i < lbParents.Items.Count; i++)
                    lbParents.SetItemChecked(i, targetState);
                await LoadReposForCheckedParentsAsync(targetState ? false : true);
            };
            lbParents.ItemCheck += async (_, e) => {
                // [修改] 从对象中获取 Path，而不是直接 ToString
                var item = lbParents.Items[e.Index] as ParentFolderItem;
                if (item == null) return;
                var p = item.Path;

                BeginInvoke(new Action(async () => {
                    // 注意：ItemCheck 事件触发时状态还没变，要用 GetItemChecked
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
            // 定义 Fetch 按钮，使用淡薄荷色区分
            var btnFetch = MakeBtn("⬇ Fetch", Color.MintCream);
            btnFetch.ForeColor = Color.DarkSlateGray;
            var btnFetchAll = MakeBtn("⬇⬇ Fetch All", Color.Honeydew);
            btnFetchAll.ForeColor = Color.DarkGreen;
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
            repoToolbar.Controls.Add(btnFetch);
            repoToolbar.Controls.Add(btnFetchAll);
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
            btnFetch.Click += async (_, __) => {
                var items = lvRepos.Items.Cast<ListViewItem>().Where(i => i.Checked).ToList();
                if (!items.Any()) {
                    MessageBox.Show("请先勾选需要 Fetch 的仓库");
                    return;
                }

                btnFetch.Enabled = false;
                // [修改提示语]
                statusLabel.Text = $"正在极速 Fetch {items.Count} 个仓库 (仅当前分支)...";
                statusProgress.Visible = true;

                try {
                    await Task.Run(() => {
                        // [优化] 将并发度从 8 提高到 12，因为单分支 Fetch 消耗资源更少
                        var opts = new ParallelOptions {
                            MaxDegreeOfParallelism = 12
                        };
                        Parallel.ForEach(items, opts, (item) => {
                            if (item.Tag is GitRepo repo) {
                                // [修改] 调用新方法，只 Fetch 当前分支
                                GitHelper.FetchCurrentBranch(repo.Path);
                            }
                        });
                    });

                    statusLabel.Text = "Fetch 完成，正在刷新状态...";
                    await BatchSyncStatusUpdate();
                } finally {
                    statusProgress.Visible = false;
                    statusLabel.Text = "就绪";
                    btnFetch.Enabled = true;
                }
            };
            btnFetchAll.Click += async (_, __) => {
                var items = lvRepos.Items.Cast<ListViewItem>().ToList();
                if (!items.Any()) {
                    MessageBox.Show("没有仓库可 Fetch");
                    return;
                }

                btnFetchAll.Enabled = false;
                statusLabel.Text = $"正在极速 Fetch 所有 {items.Count} 个仓库...";
                statusProgress.Visible = true;

                try {
                    await Task.Run(() => {
                        var opts = new ParallelOptions {
                            MaxDegreeOfParallelism = 12
                        };
                        Parallel.ForEach(items, opts, (item) => {
                            if (item.Tag is GitRepo repo) {
                                GitHelper.FetchCurrentBranch(repo.Path);
                            }
                        });
                    });

                    statusLabel.Text = "Fetch All 完成，正在刷新状态...";
                    await BatchSyncStatusUpdate();
                } finally {
                    statusProgress.Visible = false;
                    statusLabel.Text = "就绪";
                    btnFetchAll.Enabled = true;
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

                this.Enabled = false; 
                statusLabel.Text = "正在清理 Git 锁文件...";
                statusProgress.Visible = true;

                try {
                    var r = (GitRepo)lvRepos.SelectedItems[0].Tag;
                    
                    // [关键] 必须使用 await Task.Run 放到后台线程执行
                    // 配合 GitHelper 的修改，现在应该会瞬间完成
                    var res = await Task.Run(() => GitHelper.RepairRepo(r.Path));
                    
                    if(res.ok) {
                        // 如果有日志（删除了文件），弹窗提示；如果没有（原本就没锁），轻提示即可
                        if (!string.IsNullOrWhiteSpace(res.log) && !res.log.Contains("无需清理")) {
                            MessageBox.Show("清理报告：\n" + res.log, "修复完成");
                        } else {
                            // 如果什么都没删，直接在状态栏提示，不弹窗打扰
                            statusLabel.Text = "仓库正常，无锁文件。";
                            await Task.Delay(2000); // 停留2秒让用户看到
                        }
                    } else {
                        MessageBox.Show("修复失败: " + res.log, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                } 
                catch (Exception ex) {
                    MessageBox.Show("发生异常: " + ex.Message);
                }
                finally {
                    // 恢复界面
                    if (statusLabel.Text != "仓库正常，无锁文件。") statusLabel.Text = "就绪";
                    statusProgress.Visible = false;
                    this.Enabled = true;
                }
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
            
            // === [开始修改] ===
            
            // 1. 原有的 "填入" 按钮
            btnUseCurrentBranch = MakeBtn("👈 填入");
            btnUseCurrentBranch.Dock = DockStyle.Right;
            btnUseCurrentBranch.Width = 60;

            // 2. [新增] "收藏" 按钮
            var btnFav = MakeBtn("⭐ 收藏", Color.LightYellow); // 使用淡黄色区分
            btnFav.Dock = DockStyle.Right;
            btnFav.Width = 60;
            btnFav.Click += (_, __) => {
                var frm = new BranchFavoritesForm(_settings, (selectedBranch) => {
                    cmbTargetBranch.Text = selectedBranch;
                });
                frm.ShowDialog(this);
            };

            // 3. 将控件加入面板 (注意顺序：先加靠右的)
            pnlComboRow.Controls.Add(btnUseCurrentBranch);               // 最右边
            pnlComboRow.Controls.Add(btnFav);  // 收藏的左边
            pnlComboRow.Controls.Add(cmbTargetBranch);      // 填满剩余空间

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

            // 1. 定义新按钮
            var btnFloatMode = MakeBtn("🎈 悬浮模式 (Float)", Color.LightPink);
            btnFloatMode.Height = 32;
            btnFloatMode.Dock = DockStyle.Top;
            btnFloatMode.Click += (_, __) => EnterFloatMode(); // 绑定事件
            
            btnToggleConsole = MakeBtn("💻 打开 Git 控制台", Color.OldLace);
            btnToggleConsole.Height = 32;
            btnToggleConsole.Dock = DockStyle.Top;

            btnMyCollection = MakeBtn("🖼️ 我的藏品 (Album)", Color.LavenderBlush);
            btnMyCollection.Height = 32;
            btnMyCollection.Dock = DockStyle.Top;
            btnMyCollection.Click += (_, __) => new CollectionForm().Show();

            var pnlBtnsWrap = new Panel {
                Height = 110, // [修改] 增加高度以容纳新按钮 (原 70 -> 110)
                Dock = DockStyle.Top, 
                Padding = new Padding(0, 6, 0, 0)
            };
            pnlBtnsWrap.Controls.Add(btnFloatMode);      // 新增：悬浮按钮放在最下面
            pnlBtnsWrap.Controls.Add(btnMyCollection);   // 藏品
            pnlBtnsWrap.Controls.Add(btnToggleConsole);  // 控制台
            
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
            
            InitMarqueeAnimation();
        }

        // ... (SeedParentsToUi, RenderRepoItem, BatchSyncStatusUpdate 等逻辑) ...
        private void SeedParentsToUi() {
            if (lbParents == null) return;
            lbParents.BeginUpdate();
            lbParents.Items.Clear();
    
            foreach (var p in _settings.ParentPaths) {
                // [修改] 使用包装对象而不是直接添加字符串
                var item = new ParentFolderItem { Path = p, Branch = "⏳" }; 
                int i = lbParents.Items.Add(item);
        
                if (_checkedParents.Contains(p))
                    lbParents.SetItemChecked(i, true);
            }

            lbParents.EndUpdate();
    
            // [新增] 触发后台刷新分支名
            _ = RefreshParentBranchesAsync();
        }

        // [新增] 异步获取父目录分支名
        private async Task RefreshParentBranchesAsync() {
            // 1. 简单防抖
            await Task.Delay(200); 

            // 2. 收集需要更新的 UI 项
            var itemsToUpdate = new List<ParentFolderItem>();
            foreach(var item in lbParents.Items) {
                if (item is ParentFolderItem pi) itemsToUpdate.Add(pi);
            }

            // 3. 【关键】在主线程先获取收藏夹快照，转为字典以便快速查找
            //    这样做既避免了多线程访问 List 的冲突，也提高了查找速度
            var favoritesMap = new Dictionary<string, string>();
            if (_settings.FavoriteBranches != null) {
                foreach (var fav in _settings.FavoriteBranches) {
                    // 防止重复的分支名导致报错，取第一个即可
                    if (!favoritesMap.ContainsKey(fav.Branch)) {
                        favoritesMap.Add(fav.Branch, fav.Remark);
                    }
                }
            }

            // 4. 后台并发处理
            await Task.Run(() => {
                var opts = new ParallelOptions { MaxDegreeOfParallelism = 4 };
        
                Parallel.ForEach(itemsToUpdate, opts, (item) => {
                    // A. 判断是否为 Git 目录
                    if (Directory.Exists(System.IO.Path.Combine(item.Path, ".git"))) {
                        // B. 获取分支名
                        string br = GitHelper.GetFriendlyBranch(item.Path);
                        item.Branch = br;

                        // C. 【新增】从收藏夹快照中查找备注
                        if (favoritesMap.TryGetValue(br, out string remark)) {
                            item.Note = remark;
                        } else {
                            item.Note = ""; // 没找到则清空，防止残留
                        }
                    } else {
                        item.Branch = "—";
                        item.Note = "";
                    }
                });
            });

            // 5. 回到 UI 线程刷新显示
            if (!lbParents.IsDisposed) {
                this.BeginInvoke(new Action(() => {
                    lbParents.BeginUpdate();
                    // 触发列表重绘
                    for (int i = 0; i < lbParents.Items.Count; i++) {
                        lbParents.Items[i] = lbParents.Items[i]; 
                    }
                    lbParents.EndUpdate();
                }));
            }
        }

        private static string SummarizeSwitchMessage(string message) {
            if (string.IsNullOrWhiteSpace(message))
                return "";

            string text = message.Trim();
            if (text.StartsWith(">"))
                text = text[1..].Trim();

            if (text.Contains("[LockCleaner]", StringComparison.OrdinalIgnoreCase))
                return "\u9501\u6587\u4ef6";
            if (text.StartsWith("stash pop", StringComparison.OrdinalIgnoreCase))
                return "\u5e94\u7528\u6682\u5b58";
            if (text.StartsWith("stash push", StringComparison.OrdinalIgnoreCase))
                return "\u6682\u5b58\u672c\u5730";
            if (text.StartsWith("checkout", StringComparison.OrdinalIgnoreCase))
                return "\u5207\u6362\u5206\u652f";
            if (text.StartsWith("fetch", StringComparison.OrdinalIgnoreCase) || text.Contains("拉取"))
                return "\u62c9\u53d6\u8fdc\u7a0b";
            if (text.StartsWith("reset", StringComparison.OrdinalIgnoreCase))
                return "\u91cd\u7f6e\u4ed3\u5e93";
            if (text.StartsWith("merge", StringComparison.OrdinalIgnoreCase) || text.Contains("同步"))
                return "\u540c\u6b65\u5206\u652f";
            if (text.Equals("OK", StringComparison.OrdinalIgnoreCase))
                return "\u5b8c\u6210";

            return text.Length > 18 ? text[..18] + "..." : text;
        }

        private void RenderRepoItem(ListViewItem item) {
            if (item == null || item.Tag == null)
                return;
            var repo = (GitRepo)item.Tag;
            item.SubItems[1].Text = repo.CurrentBranch;
            item.UseItemStyleForSubItems = false;
            Color defaultTextColor = Color.Black;
            item.BackColor = lvRepos.BackColor;
            item.SubItems[0].ForeColor = defaultTextColor;
            item.SubItems[0].Font = item.Font;

            if (repo.IsDirty)
                item.SubItems[1].ForeColor = Color.ForestGreen;
            else
                item.SubItems[1].ForeColor = defaultTextColor;

            string syncText = "";
            Color syncColor = Color.Gray;
            Font syncFont = item.Font;

            if (repo.IsSwitching) {
                double elapsedSeconds = repo.SwitchStartedAt.HasValue
                    ? Math.Max(0, (DateTime.Now - repo.SwitchStartedAt.Value).TotalSeconds)
                    : 0;
                item.Text = $"\u5207\u7ebf\u4e2d {elapsedSeconds:F0}s";
                item.SubItems[0].ForeColor = Color.DarkOrange;
                item.SubItems[0].Font = new Font(item.Font, FontStyle.Bold);
                item.BackColor = Color.FromArgb(255, 248, 220);
                syncText = string.IsNullOrWhiteSpace(repo.LiveStatus) ? "\u5207\u7ebf\u4e2d" : repo.LiveStatus;
                syncColor = Color.DarkOrange;
                syncFont = new Font(item.Font, FontStyle.Bold);
            } else if (repo.IsSwitchQueued) {
                item.Text = "\u6392\u961f\u4e2d";
                item.SubItems[0].ForeColor = Color.SteelBlue;
                item.SubItems[0].Font = new Font(item.Font, FontStyle.Bold);
                item.BackColor = Color.FromArgb(240, 248, 255);
                syncText = string.IsNullOrWhiteSpace(repo.LiveStatus) ? "\u7b49\u5f85" : repo.LiveStatus;
                syncColor = Color.SteelBlue;
                syncFont = new Font(item.Font, FontStyle.Bold);
            } else if (!repo.SwitchOk && !string.IsNullOrWhiteSpace(repo.LastMessage)) {
                item.SubItems[0].ForeColor = Color.Crimson;
                item.SubItems[0].Font = new Font(item.Font, FontStyle.Bold);
                item.BackColor = Color.FromArgb(255, 240, 240);
                syncText = string.IsNullOrWhiteSpace(repo.LiveStatus) ? "\u5931\u8d25" : repo.LiveStatus;
                syncColor = Color.Crimson;
                syncFont = new Font(item.Font, FontStyle.Bold);
            } else if (repo.IsSyncChecked) {
                if (!repo.HasUpstream) {
                    syncText = "\u65e0\u8fdc\u7a0b";
                    syncColor = Color.Gray;
                } else if (repo.Incoming == 0 && repo.Outgoing == 0) {
                    syncText = "\u5c31\u7eea";
                    syncColor = defaultTextColor;
                } else {
                    var sb = new List<string>();
                    bool hasPull = repo.Incoming > 0;
                    bool hasPush = repo.Outgoing > 0;
                    if (hasPull)
                        sb.Add($"\u2193 {repo.Incoming}");
                    if (hasPush)
                        sb.Add($"\u2191 {repo.Outgoing}");
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
                Text = "\u26a0\ufe0f \u9ad8\u5371\u64cd\u4f5c\u786e\u8ba4",
                Width = 450,
                Height = 280,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.White
            };
            var lblTitle = new Label {
                Text = "\u60a8\u5373\u5c06\u6267\u884c\u4e00\u952e\u5207\u7ebf\u64cd\u4f5c\uff0c\u76ee\u6807\u5206\u652f\uff1a",
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
                Text = "\u6b64\u64cd\u4f5c\u5c06\u5f71\u54cd\u6240\u6709\u9009\u4e2d\u7684\u4ed3\u5e93\uff0c\u8bf7\u786e\u8ba4\u65e0\u8bef\u3002",
                AutoSize = true,
                Location = new Point(25, 110),
                Font = new Font("Segoe UI", 9, FontStyle.Italic),
                ForeColor = Color.Gray
            };
            var btnOk = new Button {
                Text = "\ud83d\ude80 \u786e\u8ba4\u5207\u7ebf",
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
                Text = "\u274c \u53d6\u6d88",
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

        private bool ConfirmBatchLockRecovery(List<RepoLockRecoveryRequest> requests) {
            if (InvokeRequired)
                return (bool)Invoke(new Func<bool>(() => ConfirmBatchLockRecovery(requests)));
            if (requests == null || requests.Count == 0)
                return false;

            string repoList = string.Join(Environment.NewLine, requests.Select((r, index) => $"{index + 1}. {r.Repo.Name}"));
            string errorList = string.Join(
                Environment.NewLine + Environment.NewLine,
                requests.Select(r => $"[{r.Repo.Name}]{Environment.NewLine}{(string.IsNullOrWhiteSpace(r.Error) ? "\u672a\u63d0\u4f9b\u9519\u8bef\u4fe1\u606f" : r.Error.Trim())}"));

            using var form = new Form {
                Text = "\u9501\u6587\u4ef6\u6279\u91cf\u4fee\u590d\u786e\u8ba4",
                Width = 640,
                Height = 520,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.White
            };
            var lblTitle = new Label {
                Text = $"\u672c\u6b21\u5207\u7ebf\u6709 {requests.Count} \u4e2a\u4ed3\u5e93\u56e0 .lock \u5931\u8d25\uff0c\u662f\u5426\u4e00\u8d77\u4fee\u590d\u5e76\u91cd\u8bd5\uff1f",
                AutoSize = true,
                Location = new Point(25, 25),
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.DimGray
            };
            var lblCount = new Label {
                Text = string.Join("\u3001", requests.Select(r => r.Repo.Name)),
                AutoSize = false,
                Width = 570,
                Height = 52,
                Location = new Point(25, 58),
                Font = new Font("Segoe UI", 15, FontStyle.Bold),
                ForeColor = Color.Crimson
            };
            var lblRepoList = new Label {
                Text = "\u5931\u8d25\u4ed3\u5e93",
                AutoSize = true,
                Location = new Point(25, 118),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.DimGray
            };
            var txtRepos = new TextBox {
                Location = new Point(25, 142),
                Width = 250,
                Height = 210,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.WhiteSmoke,
                Text = repoList
            };
            var lblError = new Label {
                Text = "\u5931\u8d25\u4fe1\u606f",
                AutoSize = true,
                Location = new Point(300, 118),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.DimGray
            };
            var txtError = new TextBox {
                Location = new Point(300, 142),
                Width = 295,
                Height = 210,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.WhiteSmoke,
                Text = errorList
            };
            var lblHint = new Label {
                Text = "\u786e\u8ba4\u540e\u4f1a\u4f9d\u6b21\u6e05\u7406\u4e0a\u9762\u8fd9\u4e9b\u4ed3\u5e93\u7684 .lock \u6587\u4ef6\uff0c\u7136\u540e\u91cd\u65b0\u6267\u884c\u5207\u7ebf\u3002",
                AutoSize = false,
                Width = 570,
                Height = 40,
                Location = new Point(25, 365),
                Font = new Font("Segoe UI", 9, FontStyle.Italic),
                ForeColor = Color.Gray
            };
            var btnOk = new Button {
                Text = "\ud83d\udd27 \u4e00\u8d77\u4fee\u590d\u5e76\u91cd\u8bd5",
                DialogResult = DialogResult.OK,
                Width = 220,
                Height = 50,
                Location = new Point(80, 420),
                BackColor = Color.ForestGreen,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnOk.FlatAppearance.BorderSize = 0;
            var btnCancel = new Button {
                Text = "\u274c \u8df3\u8fc7\u672c\u6b21",
                DialogResult = DialogResult.Cancel,
                Width = 220,
                Height = 50,
                Location = new Point(340, 420),
                BackColor = Color.IndianRed,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            form.Controls.AddRange(new Control[] {
                lblTitle, lblCount, lblRepoList, txtRepos, lblError, txtError, lblHint, btnOk, btnCancel
            });
            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;
            return form.ShowDialog(this) == DialogResult.OK;
        }

        private async Task HandlePostSwitchLockFailuresAsync(
            List<GitRepo> targetRepos,
            List<ListViewItem> items,
            string target,
            IProgress<RepoSwitchLogEntry> liveLogHandler) {
            var lockFailedRepos = targetRepos
                .Where(r => !r.SwitchOk && GitHelper.ContainsGitLockError(r.LastMessage))
                .ToList();

            if (lockFailedRepos.Count == 0)
                return;

            var requests = lockFailedRepos.Select(repo => new RepoLockRecoveryRequest {
                Repo = repo,
                Command = "switch",
                Error = repo.LastMessage ?? ""
            }).ToList();

            statusLabel.Text = $"\u53d1\u73b0 {lockFailedRepos.Count} \u4e2a\u9501\u6587\u4ef6\u5931\u8d25\u4ed3\u5e93\uff0c\u7b49\u5f85\u5904\u7406";
            if (!ConfirmBatchLockRecovery(requests)) {
                Log($"> [LockCleaner] \u8df3\u8fc7\u672c\u6b21 {lockFailedRepos.Count} \u4e2a\u9501\u6587\u4ef6\u5931\u8d25\u4ed3\u5e93");
                return;
            }

            Log($"> [LockCleaner] \u5f00\u59cb\u6279\u91cf\u4fee\u590d {lockFailedRepos.Count} \u4e2a\u9501\u6587\u4ef6\u5931\u8d25\u4ed3\u5e93");
            for (int index = 0; index < lockFailedRepos.Count; index++) {
                var repo = lockFailedRepos[index];
                statusLabel.Text = $"\u5904\u7406\u9501\u6587\u4ef6 {index + 1}/{lockFailedRepos.Count} | {repo.Name}";
                var item = items.FirstOrDefault(x => x.Tag == repo);
                repo.IsSwitchQueued = false;
                repo.IsSwitching = true;
                repo.SwitchStartedAt = DateTime.Now;
                repo.LiveStatus = "\u4fee\u590d\u9501\u6587\u4ef6";
                if (item != null)
                    RenderRepoItem(item);

                liveLogHandler.Report(new RepoSwitchLogEntry {
                    Repo = repo,
                    Message = "> [LockCleaner] \u5f00\u59cb\u4fee\u590d\u9501\u6587\u4ef6\u5e76\u91cd\u8bd5"
                });

                var retryResult = await Task.Run(() => {
                    var repair = GitHelper.RepairRepo(repo.Path);
                    if (!repair.ok)
                        return (ok: false, message: repair.log);

                    return GitHelper.SwitchAndPull(
                        repo.Path,
                        target,
                        _settings.StashOnSwitch,
                        _settings.FastMode,
                        _settings.EnableGitOperationTimeout,
                        _settings.GitOperationTimeoutSeconds,
                        null,
                        line => liveLogHandler.Report(new RepoSwitchLogEntry {
                            Repo = repo,
                            Message = line
                        }));
                });

                repo.SwitchOk = retryResult.ok;
                repo.LastMessage = retryResult.message;
                repo.CurrentBranch = GitHelper.GetFriendlyBranch(repo.Path);
                var changes = GitHelper.GetFileChanges(repo.Path);
                repo.IsDirty = changes.Count > 0;
                var syncResult = GitHelper.GetSyncCounts(repo.Path);
                repo.IsSyncChecked = true;
                if (syncResult != null) {
                    repo.HasUpstream = true;
                    repo.Incoming = syncResult.Value.behind;
                    repo.Outgoing = syncResult.Value.ahead;
                } else {
                    repo.HasUpstream = false;
                    repo.Incoming = 0;
                    repo.Outgoing = 0;
                }

                repo.IsSwitching = false;
                repo.SwitchStartedAt = null;
                repo.LiveStatus = "";
                if (item != null) {
                    item.Text = (retryResult.ok ? "\u6210\u529f" : "\u5931\u8d25") + " \u91cd\u8bd5";
                    RenderRepoItem(item);
                }
            }
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
            foreach (var item in items) {
                var repo = (GitRepo)item.Tag;
                repo.IsSwitchQueued = true;
                repo.IsSwitching = false;
                repo.SwitchStartedAt = null;
                repo.LiveStatus = "\u7b49\u5f85";
                item.Text = "\u6392\u961f\u4e2d";
                RenderRepoItem(item);
            }
            btnSwitchAll.Enabled = false;
            statusProgress.Visible = true;
            statusProgress.Style = ProgressBarStyle.Blocks;
            statusProgress.Minimum = 0;
            statusProgress.Maximum = targetRepos.Count;
            statusProgress.Value = 0;
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var uiRefreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };

            var progressHandler = new Progress<RepoSwitchResult>(result => {
                var item = items.FirstOrDefault(x => x.Tag == result.Repo);
                if (item != null) {
                    result.Repo.IsSwitchQueued = false;
                    result.Repo.IsSwitching = false;
                    result.Repo.SwitchStartedAt = null;
                    result.Repo.LiveStatus = "";
                    item.Text = (result.Success ? "\u6210\u529f" : "\u5931\u8d25") + $" {result.DurationSeconds:F1}s";
                    RenderRepoItem(item);
                }

                if (result.ProgressIndex <= statusProgress.Maximum)
                    statusProgress.Value = result.ProgressIndex;
            });

            var liveLogHandler = new Progress<RepoSwitchLogEntry>(entry => {
                if (!string.IsNullOrWhiteSpace(entry.Message)) {
                    entry.Repo.IsSwitchQueued = false;
                    entry.Repo.IsSwitching = true;
                    entry.Repo.SwitchStartedAt ??= DateTime.Now;
                    entry.Repo.LiveStatus = SummarizeSwitchMessage(entry.Message);
                    var item = items.FirstOrDefault(x => x.Tag == entry.Repo);
                    if (item != null)
                        RenderRepoItem(item);
                    Log($"[{entry.Repo.Name}] {entry.Message}");
                }
            });

            uiRefreshTimer.Tick += (s, e) => {
                int runningCount = targetRepos.Count(r => r.IsSwitching);
                int queuedCount = targetRepos.Count(r => r.IsSwitchQueued);
                statusLabel.Text = $"\u5207\u7ebf\u4e2d {statusProgress.Value}/{statusProgress.Maximum} | \u8fd0\u884c {runningCount} | \u6392\u961f {queuedCount} | {totalStopwatch.Elapsed.TotalSeconds:F0}s";
                foreach (var item in items.Where(x => {
                             var repo = (GitRepo)x.Tag;
                             return repo.IsSwitching || repo.IsSwitchQueued;
                         }))
                    RenderRepoItem(item);
            };
            uiRefreshTimer.Start();


            try {
                // 执行切线
                double totalSeconds = await _workflowService.SwitchReposAsync(
                    targetRepos,
                    target,
                    _settings.StashOnSwitch,
                    _settings.FastMode,
                    _settings.EnableGitOperationTimeout,
                    _settings.GitOperationTimeoutSeconds,
                    progressHandler,
                    liveLogHandler,
                    null);

                await HandlePostSwitchLockFailuresAsync(targetRepos, items, target, liveLogHandler);
                await RefreshParentBranchesAsync();
                totalSeconds = totalStopwatch.Elapsed.TotalSeconds;

#if !BOSS_MODE && !PURE_MODE
                if (!string.IsNullOrEmpty(_settings.LeaderboardPath)) {
                    int currentCardCount = _myCollection.Count;
                    int currentScore = _myCollection.Sum(x => x.Score);
                    var (nc, nt, ns) = await LeaderboardService.UploadMyScoreAsync(totalSeconds, 0, currentCardCount, currentScore);
                    UpdateStatsUi(nc, nt, ns);
                }
#endif
                await FinishFrogTravelAndDrawCard(targetRepos.Count);
                
                statusLabel.Text = $"完成 (总耗时 {totalSeconds:F1}s)";
                Log($"🏁 全部完成，总耗时 {totalSeconds:F1}s");

            } finally {
                // === [新增 3] 清理计时器 ===
                uiRefreshTimer.Stop();
                uiRefreshTimer.Dispose();
                totalStopwatch.Stop();

                statusProgress.Visible = false;
                statusProgress.Style = ProgressBarStyle.Marquee; 
                statusProgress.Value = 0;
                btnSwitchAll.Enabled = true;
            }
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
                Height = 400,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
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
                Font = new Font("Segoe UI", 10)
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
            var chkEnableTimeout = new CheckBox {
                Text = "启用 Git 操作超时限制",
                Top = 200,
                Left = 20,
                Width = 390,
                Font = new Font("Segoe UI", 10),
                Checked = _settings.EnableGitOperationTimeout,
                Cursor = Cursors.Hand
            };

            var lblTimeoutSeconds = new Label {
                Text = "默认超时时间(秒):",
                Top = 235,
                Left = 20,
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            var numTimeoutSeconds = new NumericUpDown {
                Top = 263,
                Left = 20,
                Width = 390,
                Minimum = 1,
                Maximum = 3600,
                Value = Math.Max(1, _settings.GitOperationTimeoutSeconds),
                Font = new Font("Segoe UI", 10)
            };

            void UpdateTimeoutInputState() {
                bool enabled = chkEnableTimeout.Checked;
                lblTimeoutSeconds.Enabled = enabled;
                numTimeoutSeconds.Enabled = enabled;
            }
            chkEnableTimeout.CheckedChanged += (_, __) => UpdateTimeoutInputState();

            var btnOk = new Button {
                Text = "💾 保存并应用",
                Top = 315,
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
                lblTheme, cmbThemes, lblColl, cmbCollection, chkEnableTimeout, lblTimeoutSeconds, numTimeoutSeconds, btnOk
            });
            form.AcceptButton = btnOk;

            // 触发一次联动以设置初始可见性
            // Hack: 手动调用事件处理逻辑
            bool showColl = cmbThemes.SelectedItem?.ToString() == THEME_COLLECTION;
            lblColl.Visible = showColl;
            cmbCollection.Visible = showColl;
            UpdateTimeoutInputState();

            // === 保存逻辑 ===
            if (form.ShowDialog(this) == DialogResult.OK) {
                bool needApply = false;
                bool settingsChanged = false;

                // 1. 保存主题
                string newTheme = cmbThemes.SelectedItem?.ToString();
                if (newTheme == THEME_NONE)
                    newTheme = ""; // 空字符串代表无主题

                if (newTheme != _settings.SelectedTheme) {
                    _settings.SelectedTheme = newTheme;
                    needApply = true;
                    settingsChanged = true;
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
                    settingsChanged = true;
                }

                bool newEnableTimeout = chkEnableTimeout.Checked;
                int newTimeoutSeconds = Decimal.ToInt32(numTimeoutSeconds.Value);
                if (newEnableTimeout != _settings.EnableGitOperationTimeout) {
                    _settings.EnableGitOperationTimeout = newEnableTimeout;
                    settingsChanged = true;
                }
                if (newTimeoutSeconds != _settings.GitOperationTimeoutSeconds) {
                    _settings.GitOperationTimeoutSeconds = newTimeoutSeconds;
                    settingsChanged = true;
                }

                if (settingsChanged) {
                    _settings.Save();
                }

                if (needApply) {
                    UpdateThemeLabel(); // 更新状态栏文字
                    LoadRandomFrameWorkImage(); // 立即刷新图片
                    MessageBox.Show("设置已保存！");
                } else if (settingsChanged) {
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

        private void EnterFloatMode()
        {
            // 获取当前展示的图片 (青蛙图或藏品图)
            Image currentImg = pbState.Image;

            // 如果当前没有图片，就加载 AppIcon 或者默认图
            if (currentImg == null)
            {
                try 
                { 
                    currentImg = Icon.ToBitmap(); 
                } 
                catch 
                { 
                    // 如果连图标都没有，画一个带颜色的方块
                    var bmp = new Bitmap(100, 100);
                    using(var g = Graphics.FromImage(bmp)) g.Clear(Color.DeepSkyBlue);
                    currentImg = bmp;
                }
            }

            // 创建悬浮窗，传入当前主窗体引用和图片
            var floatForm = new FloatIconForm(this, currentImg);
    
            floatForm.Show();
            this.Hide(); // 隐藏主窗体
        }
        
        // ==========================================
        // 跑马灯逻辑 (堆叠星星 -> 替换文字 修复版)
        // ==========================================
        
        // 核心变量
        private System.Windows.Forms.Timer marqueeTimer;
        private string _cleanBaseTitle;          // 保存纯净的原始标题
        private const string MARQUEE_SEPARATOR = "   |   "; // 分隔符
        
        // 动画状态
        private int _marqueeLoopCount = 0;       
        private const int MAX_MARQUEE_LOOPS = 3; 
        private string _marqueeTargetText = "";  // 最终要显示的文本
        private char[] _marqueeBuffer;           // 显示缓冲区
        private int _marqueeStepIndex = 0;       // 当前步数
        private int _marqueePhase = 0;           // 0:堆星, 1:换字, 2:保持, 3:结束
        private int _marqueeHoldTicks = 0;       // 保持计时器

        // [辅助] 获取程序纯净标题
        private string GetBaseTitle() {
            try {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string vStr = $"{version.Major}.{version.Minor}.{version.Build}";
                return $"Git 分支管理工具 - v{vStr}";
            } catch {
                return "Git 分支管理工具";
            }
        }

        private string GetDefaultMarqueeEasterEgg() {
            var me = Environment.UserName;
            string[] eggs = {
                $"{me}，今天切线零冲突",
                $"{me}，愿你的 stash 一次成功",
                $"{me}，愿所有 checkout 都丝滑"
            };
            int index = DateTime.Today.DayOfYear % eggs.Length;
            return eggs[index];
        }

        // [核心] 初始化跑马灯
        // 务必在 InitUi() 最后一行调用
        private async void InitMarqueeAnimation() {
            // 1. 销毁旧定时器
            if (marqueeTimer != null) {
                marqueeTimer.Stop();
                marqueeTimer.Dispose();
                marqueeTimer = null;
            }

            // 2. 还原标题
            _cleanBaseTitle = GetBaseTitle();
            this.Text = _cleanBaseTitle;

            // 3. 读取公告 (关键：限制长度防止显示不全)
            string textToShow = GetDefaultMarqueeEasterEgg();
            try {
                string noticePath = Path.Combine(_settings.UpdateSourcePath, "notice.txt");
                var readTask = Task.Run(() => {
                    if (File.Exists(noticePath)) {
                        return File.ReadAllText(noticePath).Trim();
                    }
                    return null;
                });

                if (await Task.WhenAny(readTask, Task.Delay(2000)) == readTask) {
                    string content = await readTask;
                    if (!string.IsNullOrEmpty(content)) {
                        string line = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                        // [关键修复] 强制截断长度为 35，防止撑爆标题栏
                        if (line.Length > 35) line = line.Substring(0, 34) + "…";
                        textToShow = line;
                    }
                }
            } catch (Exception ex) {
                Log($"[Marquee] Error: {ex.Message}");
            }

            _marqueeTargetText = textToShow;

            // 4. 初始化缓冲区 (全空格)
            _marqueeBuffer = new char[_marqueeTargetText.Length];
            for(int i = 0; i < _marqueeBuffer.Length; i++) _marqueeBuffer[i] = ' ';

            // 5. 重置状态
            _marqueeLoopCount = 0;
            ResetMarqueeState();

            // 6. 启动定时器
            marqueeTimer = new System.Windows.Forms.Timer();
            marqueeTimer.Interval = 80; // 加快一点速度，效果更流畅
            marqueeTimer.Tick += MarqueeTimer_Tick;
            marqueeTimer.Start();
        }

        private void ResetMarqueeState() {
            _marqueePhase = 0;      // 回到 Phase 0: 堆星星
            _marqueeStepIndex = 0;  
            _marqueeHoldTicks = 0;
            // 重置缓冲区为空格
            for (int i = 0; i < _marqueeBuffer.Length; i++) _marqueeBuffer[i] = ' ';
        }

        // [核心] 动画逻辑
        private void MarqueeTimer_Tick(object sender, EventArgs e) {
            if (_marqueeBuffer == null || string.IsNullOrEmpty(_marqueeTargetText)) return;
            int len = _marqueeTargetText.Length;

            // Phase 0: 从右往左堆叠星星
            // 初始: "       "
            // Tick 1: "      ★" (index = len-1)
            // Tick 2: "     ★★" (index = len-2)
            if (_marqueePhase == 0) {
                int targetIndex = len - 1 - _marqueeStepIndex; // 倒序填充

                if (targetIndex >= 0) {
                    _marqueeBuffer[targetIndex] = '★'; 
                    _marqueeStepIndex++;
                } else {
                    // 填满了，进入下一阶段
                    _marqueePhase = 1;
                    _marqueeStepIndex = 0;
                }
            }
            // Phase 1: 从左往右替换为文字
            // 初始: "★★★★★★★"
            // Tick 1: "G★★★★★★" (index = 0)
            // Tick 2: "Gi★★★★★" (index = 1)
            else if (_marqueePhase == 1) {
                int targetIndex = _marqueeStepIndex; // 正序替换

                if (targetIndex < len) {
                    _marqueeBuffer[targetIndex] = _marqueeTargetText[targetIndex];
                    _marqueeStepIndex++;
                } else {
                    // 替换完了，进入保持阶段
                    _marqueePhase = 2;
                }
            }
            // Phase 2: 保持展示
            else if (_marqueePhase == 2) {
                _marqueeHoldTicks++;
                if (_marqueeHoldTicks > 50) { // 停留约 2.5秒
                    _marqueeLoopCount++;
                    if (_marqueeLoopCount >= MAX_MARQUEE_LOOPS) {
                        _marqueePhase = 3; // 结束
                    } else {
                        ResetMarqueeState(); // 再来一次
                    }
                }
            }
            // Phase 3: 结束还原
            else if (_marqueePhase == 3) {
                marqueeTimer.Stop();
                this.Text = _cleanBaseTitle; // 还原纯净标题
                return;
            }

            // 更新标题：纯净标题 + 分隔符 + 缓冲区内容
            string animPart = new string(_marqueeBuffer);
            this.Text = $"{_cleanBaseTitle}{MARQUEE_SEPARATOR}{animPart}";
        }
        
        // [新增] 用于 lbParents 显示的包装类
        // [修改] 更新后的包装类
        private class ParentFolderItem {
            public string Path { get; set; }
            public string Branch { get; set; } = "";
            public string Note { get; set; } = ""; // [新增] 存储备注

            public override string ToString() {
                string display = Path;
        
                // 显示分支名
                if (!string.IsNullOrEmpty(Branch) && Branch != "—") {
                    display += $"   🌿[{Branch}]";
                }

                // [新增] 如果有备注，显示备注
                if (!string.IsNullOrEmpty(Note)) {
                    display += $"   ★({Note})"; 
                }

                return display;
            }
        }
    }
    
}
