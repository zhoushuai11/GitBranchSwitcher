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
        // === UI 控件 ===
        private TableLayoutPanel tlTop;
        private CheckedListBox lbParents;
        private TextBox txtSearch;
        private Button btnAddParent, btnRemoveParent, btnSelectAllParents, btnClearParents;
        private Label lblHintParents;

        private SplitContainer splitMain;
        private SplitContainer splitConsole;

        private ListView lvRepos;
        private FlowLayoutPanel repoToolbar;

        private GroupBox grpDetails;
        private ListView lvFileChanges;
        private RichTextBox rtbDiff;
        private Panel pnlDetailRight, pnlActions;
        private Label lblRepoInfo;
        private TextBox txtCommitMsg;
        private Button btnCommit, btnPull, btnPush, btnStash;

        private Panel pnlRight;
        private SplitContainer splitUpper;
        private Label lblTargetBranch, lblFetchStatus;
        private ComboBox cmbTargetBranch;
        private Button btnSwitchAll, btnUseCurrentBranch;
        private CheckBox chkStashOnSwitch, chkFastMode;
        private FlowLayoutPanel statePanel;
        private PictureBox pbState, pbFlash;
        private Label lblStateText;
        private System.Windows.Forms.Timer flashTimer;
        private TextBox txtLog;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel, statusStats;
        private ToolStripProgressBar statusProgress;

        // === 数据 ===
        private readonly BindingList<GitRepo> _repos = new BindingList<GitRepo>();
        private List<string> _allBranches = new List<string>();
        private AppSettings _settings;
        private System.Threading.CancellationTokenSource? _loadCts;
        private int _loadSeq = 0;
        private HashSet<string> _checkedParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private ListViewGroup grpStaged, grpUnstaged;
        private const int TARGET_BOX = 500, FLASH_BOX = 300;

        private enum SwitchState {
            NotStarted,
            Switching,
            Done
        }

        private int done = 0;
        private System.Threading.SemaphoreSlim sem = new System.Threading.SemaphoreSlim(16);
        private List<Task> tasks = new List<Task>();

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

            if (_settings.CachedBranchList != null && _settings.CachedBranchList.Count > 0) {
                _allBranches = new List<string>(_settings.CachedBranchList);
                UpdateBranchDropdown();
            }

            _ = LoadReposForCheckedParentsAsync(false);
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
#if !PURE_MODE
            _ = UpdateService.CheckAndUpdateAsync(_settings.UpdateSourcePath, this);
#endif
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
#if PURE_MODE
            Text = $"Git 分支管理工具 (Pure) - v{vStr}";
#elif BOSS_MODE
            Text = $"Git 分支管理工具 (Enterprise) - v{vStr}";
#else
            Text = $"Unity 项目切线工具 (Slim King) - v{vStr}";
#endif
            Width = 1400;
            Height = 950;
            StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        }

        private Button MakeBtn(string text, Color? backColor = null) {
            var b = new Button {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
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
            // === 1. 顶部父目录区 ===
            tlTop = new TableLayoutPanel {
                Dock = DockStyle.Top,
                Height = 130,
                ColumnCount = 6,
                Padding = new Padding(10),
                BackColor = Color.WhiteSmoke
            };
            tlTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++)
                tlTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tlTop.RowCount = 2;
            tlTop.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tlTop.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            lbParents = new CheckedListBox {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                BorderStyle = BorderStyle.None,
                BackColor = Color.WhiteSmoke
            };
            btnAddParent = MakeBtn("📂 添加父目录…", Color.AliceBlue);
            btnRemoveParent = MakeBtn("🗑️ 移除选中");
            txtSearch = new TextBox {
                Width = 200, Anchor = AnchorStyles.Left, BorderStyle = BorderStyle.FixedSingle
            };
            var lblSearch = new Label {
                Text = "🔍 过滤：", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.Gray
            };
            var parentOps = new FlowLayoutPanel {
                FlowDirection = FlowDirection.TopDown, AutoSize = true
            };
            btnSelectAllParents = MakeBtn("全选父目录");
            btnClearParents = MakeBtn("全不选");
            btnSelectAllParents.Font = new Font(DefaultFont.FontFamily, 8f);
            btnClearParents.Font = new Font(DefaultFont.FontFamily, 8f);
            parentOps.Controls.Add(btnSelectAllParents);
            parentOps.Controls.Add(btnClearParents);
            lblHintParents = new Label {
                Text = "提示：勾选要使用的父目录；右键可操作。", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 5, 0, 0)
            };

            tlTop.Controls.Add(lbParents, 0, 0);
            tlTop.Controls.Add(btnAddParent, 1, 0);
            tlTop.Controls.Add(btnRemoveParent, 2, 0);
            tlTop.Controls.Add(lblSearch, 3, 0);
            tlTop.Controls.Add(txtSearch, 4, 0);
            tlTop.Controls.Add(parentOps, 5, 0);
            tlTop.Controls.Add(lblHintParents, 0, 1);
            tlTop.SetColumnSpan(lblHintParents, 6);

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

                    RefilterParentsList();
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
                RefilterParentsList();
                await LoadReposForCheckedParentsAsync(true);
            };
            txtSearch.TextChanged += (_, __) => RefilterParentsList();
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
            btnSelectAllParents.Click += async (_, __) => {
                _checkedParents = new HashSet<string>(_settings.ParentPaths);
                for (int i = 0; i < lbParents.Items.Count; i++)
                    lbParents.SetItemChecked(i, true);
                await LoadReposForCheckedParentsAsync(false);
            };
            btnClearParents.Click += async (_, __) => {
                _checkedParents.Clear();
                for (int i = 0; i < lbParents.Items.Count; i++)
                    lbParents.SetItemChecked(i, false);
                await LoadReposForCheckedParentsAsync(true);
            };
            lbParents.KeyDown += async (_, e) => {
                if (e.KeyCode == Keys.Delete)
                    btnRemoveParent.PerformClick();
            };

            // === 2. 主分割区域 ===
            splitMain = new SplitContainer {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal
            };
            splitUpper = new SplitContainer {
                Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2
            };

            Shown += (_, __) => {
                splitMain.SplitterDistance = (int)(ClientSize.Height * 0.6);
                splitUpper.SplitterDistance = (int)(ClientSize.Width * 0.6);
            };

            // === 3. 左上：仓库列表 (修改列结构) ===
            lvRepos = new ListView {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                CheckBoxes = true,
                BorderStyle = BorderStyle.None,
                HideSelection = false
            };
            lvRepos.Columns.Add("状态", 60);
            lvRepos.Columns.Add("当前分支", 220);
            // [新增] 专门的同步状态列 (Index 2)
            lvRepos.Columns.Add("同步", 100);
            lvRepos.Columns.Add("仓库名", 200);
            lvRepos.Columns.Add("路径", 400);
            try {
                var prop = typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                prop?.SetValue(lvRepos, true, null);
            } catch {
            }

            lvRepos.SelectedIndexChanged += async (_, __) => await RefreshRepoDetails();

            repoToolbar = new FlowLayoutPanel {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(5, 8, 5, 8),
                BackColor = Color.White
            };
            var btnToggleSelect = MakeBtn("✅ 全选/反选", Color.White);
            btnToggleSelect.Click += (_, __) => {
                bool hasUn = lvRepos.Items.Cast<ListViewItem>().Any(i => !i.Checked);
                lvRepos.BeginUpdate();
                foreach (ListViewItem i in lvRepos.Items)
                    i.Checked = hasUn;
                lvRepos.EndUpdate();
            };
            var btnRescan = MakeBtn("🔄 刷新列表", Color.White);
            btnRescan.Click += async (_, __) => await LoadReposForCheckedParentsAsync(true);
            var btnPullAll = MakeBtn("⬇️ 批量拉取", Color.MintCream);
            btnPullAll.ForeColor = Color.DarkGreen;
            btnPullAll.Font = new Font(DefaultFont, FontStyle.Bold);
            btnPullAll.Click += async (_, __) => await PullAllAsync();
            var btnNewClone = MakeBtn("➕ 新建拉线", Color.Azure);
            btnNewClone.ForeColor = Color.DarkBlue;
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
                        RefilterParentsList();
                        _ = LoadReposForCheckedParentsAsync(true);
                    }
                }
            };

            repoToolbar.Controls.Add(btnToggleSelect);
            repoToolbar.Controls.Add(btnRescan);
            repoToolbar.Controls.Add(new Label {
                Width = 20
            });
            repoToolbar.Controls.Add(btnPullAll);
            repoToolbar.Controls.Add(btnNewClone);
#if !BOSS_MODE && !PURE_MODE
            var btnRank = MakeBtn("🏆 排行榜", Color.Ivory);
            btnRank.ForeColor = Color.DarkGoldenrod;
            btnRank.Click += (_, __) => ShowLeaderboard();
            repoToolbar.Controls.Add(btnRank);
#endif
            var btnSuperSlim = MakeBtn("🔥 一键瘦身", Color.MistyRose);
            btnSuperSlim.ForeColor = Color.DarkRed;
            btnSuperSlim.Click += (_, __) => StartSuperSlimProcess();
            repoToolbar.Controls.Add(btnSuperSlim);

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

            var pnlListContainer = new Panel {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle
            };
            pnlListContainer.Controls.Add(lvRepos);
            pnlListContainer.Controls.Add(repoToolbar);

            // === 4. 下半部分：Fork 控制台 ===
            grpDetails = new GroupBox {
                Text = "控制台", Dock = DockStyle.Fill, Padding = new Padding(3), BackColor = Color.White
            };
            splitConsole = new SplitContainer {
                Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 5
            };

            // [左] 文件列表
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
            lvFileChanges.Columns.Add("文件路径", 600);

            lvFileChanges.SelectedIndexChanged += async (_, __) => await ShowSelectedFileDiff();
            lvFileChanges.DoubleClick += async (_, __) => await ToggleStagedStatus();

            var fileMenu = new ContextMenuStrip();
            fileMenu.Items.Add("➕ 加入/移出 暂存区", null, async (_, __) => await ToggleStagedStatus());
            fileMenu.Items.Add("📂 打开目录", null, (_, __) => {
                if (lvFileChanges.SelectedItems.Count > 0 && lvRepos.SelectedItems.Count > 0)
                    Process.Start("explorer.exe", "/select,\"" + Path.Combine(((GitRepo)lvRepos.SelectedItems[0].Tag).Path, lvFileChanges.SelectedItems[0].SubItems[1].Text) + "\"");
            });
            var itemDiscard = fileMenu.Items.Add("🧨 还原 (Discard)");
            itemDiscard.ForeColor = Color.Red;
            itemDiscard.Click += async (_, __) => {
                if (lvFileChanges.SelectedItems.Count == 0 || lvRepos.SelectedItems.Count == 0)
                    return;
                var r = (GitRepo)lvRepos.SelectedItems[0].Tag;
                var file = lvFileChanges.SelectedItems[0].SubItems[1].Text;
                if (lvFileChanges.SelectedItems[0].Group == grpStaged) {
                    MessageBox.Show("请先 Unstage 再还原。");
                    return;
                }

                if (MessageBox.Show($"丢弃 '{file}' 的修改？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                    await Task.Run(() => {
                        GitHelper.RunGit(r.Path, $"checkout -- \"{file}\"", 5000);
                        if (file.Contains("??"))
                            GitHelper.RunGit(r.Path, $"clean -f \"{file}\"", 5000);
                    });
                    await RefreshRepoDetails();
                }
            };
            lvFileChanges.ContextMenuStrip = fileMenu;
            splitConsole.Panel1.Controls.Add(lvFileChanges);

            // [右] 详情+操作
            pnlDetailRight = new Panel {
                Dock = DockStyle.Fill
            };
            lblRepoInfo = new Label {
                Dock = DockStyle.Top,
                Height = 28,
                Text = "请选择仓库...",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.DarkSlateGray,
                BackColor = Color.WhiteSmoke,
                Padding = new Padding(5, 0, 0, 0)
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
            btnCommit.Click += async (_, __) => await RunDetailAction("Commit");

            btnPush = MakeBtn("⬆ Push", Color.AliceBlue);
            btnPush.Height = 28;
            btnPush.Width = 80;
            btnPush.Click += async (_, __) => await RunDetailAction("Push");
            btnPull = MakeBtn("⬇ Pull", Color.AliceBlue);
            btnPull.Height = 28;
            btnPull.Width = 80;
            btnPull.Click += async (_, __) => await RunDetailAction("Pull");
            btnStash = MakeBtn("📦 Stash");
            btnStash.Height = 28;
            btnStash.Width = 70;
            btnStash.Click += async (_, __) => await RunDetailAction("Stash");

            pnlBtns.Controls.Add(btnCommit);
            pnlBtns.Controls.Add(new Label {
                Width = 8
            });
            pnlBtns.Controls.Add(btnPush);
            pnlBtns.Controls.Add(btnPull);
            pnlBtns.Controls.Add(new Label {
                Width = 8
            });
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
            splitConsole.Panel2.Controls.Add(pnlDetailRight);
            grpDetails.Controls.Add(splitConsole);

            // === 5. 右上区 ===
            pnlRight = new Panel {
                Dock = DockStyle.Fill, Padding = new Padding(15), BackColor = Color.White
            };
            var rightLayout = new TableLayoutPanel {
                Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true
            };
            rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            lblFetchStatus = new Label {
                Text = "", AutoSize = true, ForeColor = Color.Magenta, Font = new Font("Segoe UI", 9, FontStyle.Italic)
            };
            rightLayout.Controls.Add(lblFetchStatus, 0, 0);
            rightLayout.SetColumnSpan(lblFetchStatus, 3);
            lblTargetBranch = new Label {
                Text = "🎯 目标分支：", AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font(DefaultFont, FontStyle.Bold)
            };
            cmbTargetBranch = new ComboBox {
                Width = 400, DropDownStyle = ComboBoxStyle.DropDown, Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = new Font("Consolas", 10)
            };
            btnUseCurrentBranch = MakeBtn("👈 填入选中项");
            btnUseCurrentBranch.Click += (_, __) => {
                var item = lvRepos.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Checked);
                if (item == null) {
                    MessageBox.Show("请先勾选");
                    return;
                }

                var repo = (GitRepo)item.Tag;
                var branch = repo.CurrentBranch;
                if (!string.IsNullOrEmpty(branch) && branch != "—") {
                    cmbTargetBranch.SelectedIndex = -1;
                    cmbTargetBranch.Text = branch;
                } else {
                    MessageBox.Show("无效分支");
                }
            };
            cmbTargetBranch.TextUpdate += (_, __) => {
                try {
                    UpdateBranchDropdown();
                } catch {
                }
            };
            chkStashOnSwitch = new CheckBox {
                Text = "🔒 尝试 Stash 本地修改 [推荐]",
                AutoSize = true,
                Checked = _settings.StashOnSwitch,
                ForeColor = Color.DarkSlateBlue,
                Cursor = Cursors.Hand
            };
            chkStashOnSwitch.CheckedChanged += (_, __) => {
                _settings.StashOnSwitch = chkStashOnSwitch.Checked;
                _settings.Save();
            };
            chkFastMode = new CheckBox {
                Text = "⚡ 极速本地切换 (跳过 Fetch/Pull)",
                AutoSize = true,
                Checked = _settings.FastMode,
                ForeColor = Color.DarkGreen,
                Font = new Font(DefaultFont, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            chkFastMode.CheckedChanged += (_, __) => {
                _settings.FastMode = chkFastMode.Checked;
                _settings.Save();
            };
            btnSwitchAll = new Button {
                Text = "🚀 一键切线 (Switch)",
                Height = 50,
                Dock = DockStyle.Top,
                BackColor = Color.DodgerBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnSwitchAll.FlatAppearance.BorderSize = 0;
            btnSwitchAll.Click += async (_, __) => await SwitchAllAsync();
            statePanel = new FlowLayoutPanel {
                Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 20, 0, 0)
            };
            pbState = new PictureBox {
                Width = TARGET_BOX, Height = TARGET_BOX, SizeMode = PictureBoxSizeMode.CenterImage
            };
            lblStateText = new Label {
                Text = "Ready", Font = new Font("Segoe UI", 14, FontStyle.Bold), AutoSize = true, ForeColor = Color.Gray
            };
            pbFlash = new PictureBox {
                Width = FLASH_BOX, Height = FLASH_BOX, Visible = false, SizeMode = PictureBoxSizeMode.CenterImage
            };
            flashTimer = new System.Windows.Forms.Timer {
                Interval = 800
            };
            flashTimer.Tick += (_, __) => {
                pbFlash.Visible = false;
                flashTimer.Stop();
            };
            statePanel.Controls.Add(pbState);
            statePanel.Controls.Add(lblStateText);
            statePanel.Controls.Add(pbFlash);
            rightLayout.Controls.Add(lblTargetBranch, 0, 1);
            rightLayout.Controls.Add(cmbTargetBranch, 1, 1);
            rightLayout.Controls.Add(btnUseCurrentBranch, 2, 1);
            rightLayout.Controls.Add(new Label {
                Height = 20
            }, 0, 2);
            rightLayout.Controls.Add(btnSwitchAll, 0, 3);
            rightLayout.SetColumnSpan(btnSwitchAll, 3);
            rightLayout.Controls.Add(new Label {
                Height = 10
            }, 0, 4);
            rightLayout.Controls.Add(chkStashOnSwitch, 0, 5);
            rightLayout.SetColumnSpan(chkStashOnSwitch, 3);
            rightLayout.Controls.Add(chkFastMode, 0, 6);
            rightLayout.SetColumnSpan(chkFastMode, 3);
            rightLayout.Controls.Add(statePanel, 0, 7);
            rightLayout.SetColumnSpan(statePanel, 3);
            pnlRight.Controls.Add(rightLayout);

            // 6. 组装全局
            splitUpper.Panel1.Controls.Add(pnlListContainer);
            splitUpper.Panel2.Controls.Add(pnlRight);
            splitMain.Panel1.Controls.Add(splitUpper);
            splitMain.Panel2.Controls.Add(grpDetails);

            txtLog = new TextBox {
                Dock = DockStyle.Fill,
                Height = 100,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                Font = new Font("Consolas", 9),
                BackColor = Color.WhiteSmoke,
                BorderStyle = BorderStyle.FixedSingle
            };

            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("就绪") {
                Margin = new Padding(10, 0, 0, 0)
            };
            statusStrip.Items.Add(statusLabel);
            statusStrip.Items.Add(new ToolStripStatusLabel {
                Spring = true
            });
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

            Controls.Add(splitMain);
            Controls.Add(tlTop);
            Controls.Add(statusStrip);
        }

        // === 核心逻辑 ===

        // 在 MainForm.cs 中替换 BatchSyncStatusUpdate 方法

        private async Task BatchSyncStatusUpdate() {
            if (lvRepos.Items.Count == 0)
                return;

            var targetItems = new List<ListViewItem>();
            foreach (ListViewItem i in lvRepos.Items)
                targetItems.Add(i);

            // 更新底部状态栏提示
            BeginInvoke((Action)(() => statusLabel.Text = "正在分析同步状态..."));

            await Task.Run(() => {
                var opts = new ParallelOptions {
                    MaxDegreeOfParallelism = 10
                };

                Parallel.ForEach(targetItems, opts, (item) => {
                    var repo = (GitRepo)item.Tag;

                    // 调用新版 GetSyncCounts (返回可空类型)
                    var syncResult = GitHelper.GetSyncCounts(repo.Path);

                    try {
                        BeginInvoke((Action)(() => {
                            if (syncResult == null) {
                                // 没找到远程分支 (通常是新分支)
                                item.SubItems[2].Text = "⚠️ 无远程";
                                item.SubItems[2].ForeColor = Color.Gray;
                                repo.Incoming = 0;
                                repo.Outgoing = 0;
                            } else {
                                var (behind, ahead) = syncResult.Value;
                                repo.Incoming = behind;
                                repo.Outgoing = ahead;

                                if (behind == 0 && ahead == 0) {
                                    // [优化] 完全同步时显示对勾
                                    item.SubItems[2].Text = "✔ 最新";
                                    item.SubItems[2].ForeColor = Color.Green;
                                } else {
                                    // 有差异
                                    string syncText = "";
                                    if (ahead > 0)
                                        syncText += $"{ahead}↑ ";
                                    if (behind > 0)
                                        syncText += $"{behind}↓";

                                    item.SubItems[2].Text = syncText.Trim();

                                    // 颜色逻辑：落后显示红(需拉取)，领先显示蓝(需推送)，都有显示紫
                                    if (behind > 0 && ahead > 0)
                                        item.SubItems[2].ForeColor = Color.Purple;
                                    else if (behind > 0)
                                        item.SubItems[2].ForeColor = Color.Red;
                                    else
                                        item.SubItems[2].ForeColor = Color.Blue;
                                }
                            }
                        }));
                    } catch {
                    }
                });
            });

            BeginInvoke((Action)(() => statusLabel.Text = "就绪"));
        }
        private async Task RefreshRepoDetails() {
            // 初始化比例
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
                var data = GitHelper.GetSyncCounts(repo.Path);
                var behind = data.Value.behind;
                var ahead = data.Value.ahead;
                repo.Incoming = behind;
                repo.Outgoing = ahead;

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

                    btnPull.Text = behind > 0? $"⬇ {behind}" : "⬇ Pull";
                    btnPull.Enabled = behind > 0;
                    btnPush.Text = ahead > 0? $"⬆ {ahead}" : "⬆ Push";
                    btnPush.Enabled = ahead > 0;
                    btnCommit.Enabled = stagedCount > 0;
                    btnCommit.Text = stagedCount > 0? $"Commit ({stagedCount})" : "Commit";

                    // 顺便更新一下列表里的同步状态
                    string syncText = "";
                    if (ahead > 0)
                        syncText += $"{ahead}↑ ";
                    if (behind > 0)
                        syncText += $"{behind}↓";
                    if (string.IsNullOrEmpty(syncText))
                        syncText = "—";
                    item.SubItems[2].Text = syncText;
                    item.SubItems[2].ForeColor = behind > 0? Color.Red : (ahead > 0? Color.Blue : Color.Gray);
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

        // === 关键逻辑 ===
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
                        // [Fix] Add 5 items
                        lvRepos.Items.Add(new ListViewItem(new[] {
                            "⏳", "—", "...", display, path
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
                    // [Fix] Add 5 items
                    lvRepos.Items.Add(new ListViewItem(new[] {
                        "⏳", "—", "...", display, item.FullPath
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
                        item.SubItems[1].Text = ((GitRepo)item.Tag).CurrentBranch;
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

        // ... (其余方法保持不变) ...
        private void TrySetRuntimeIcon() {
            try {
                var icon = ImageHelper.LoadIconFromResource("appicon");
                if (icon != null)
                    this.Icon = icon;
            } catch {
            }
        }

        private void ApplyImageTo(PictureBox pb, string key, int s) {
#if BOSS_MODE
            pb.Image = null;
#else
            if (pb.Image != null) {
                var o = pb.Image;
                pb.Image = null;
                o.Dispose();
            }

            var img = ImageHelper.LoadRandomImageFromResource(key);
            if (img != null) {
                pb.SizeMode = (img.Width > s || img.Height > s)? PictureBoxSizeMode.Zoom : PictureBoxSizeMode.CenterImage;
                pb.Image = img;
            }
#endif
        }

        private void LoadStateImagesRandom() {
            ApplyImageTo(pbState, "state_notstarted", TARGET_BOX);
            ApplyImageTo(pbFlash, "flash_success", FLASH_BOX);
        }

        private void SetSwitchState(SwitchState st) {
            if (st == SwitchState.NotStarted) {
                ApplyImageTo(pbState, "state_notstarted", TARGET_BOX);
                lblStateText.Text = "未开始";
            }

            if (st == SwitchState.Switching) {
                ApplyImageTo(pbState, "state_switching", TARGET_BOX);
                lblStateText.Text = "切线中...";
            }

            if (st == SwitchState.Done) {
                ApplyImageTo(pbState, "state_done", TARGET_BOX);
                lblStateText.Text = "搞定!";
            }
        }

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

        private void RefilterParentsList() {
            lbParents.BeginUpdate();
            lbParents.Items.Clear();
            var kw = txtSearch.Text.Trim();
            foreach (var p in _settings.ParentPaths) {
                if (string.IsNullOrEmpty(kw) || p.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) {
                    int i = lbParents.Items.Add(p);
                    if (_checkedParents.Contains(p))
                        lbParents.SetItemChecked(i, true);
                }
            }

            lbParents.EndUpdate();
        }

        private void UpdateStatsUi(int totalCount = -1, double totalSeconds = -1, long totalSpace = -1) {
            if (statusStats != null) {
                int c = totalCount >= 0? totalCount : _settings.TodaySwitchCount;
                double t = totalSeconds >= 0? totalSeconds : _settings.TodayTotalSeconds;
                long s = totalSpace >= 0? totalSpace : 0;
                statusStats.Text = $"📅 累计：切线 {c} 次 | 摸鱼 {FormatDuration(t)} | 瘦身 {FormatSize(s)}";
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

        private async void ShowLeaderboard() {
            if (string.IsNullOrEmpty(_settings.LeaderboardPath)) {
                string input = ShowInputBox("设置", "请输入共享文件路径:", _settings.LeaderboardPath);
                if (string.IsNullOrWhiteSpace(input))
                    return;
                _settings.LeaderboardPath = input;
                _settings.Save();
                LeaderboardService.SetPath(input);
            }

            var form = new Form {
                Text = "👑 卷王 & 摸鱼王 & 瘦身王 排行榜", Width = 1000, Height = 500, StartPosition = FormStartPosition.CenterParent
            };
            var table = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
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
            var listSpace = new ListView {
                Dock = DockStyle.Fill, View = View.Details, GridLines = true, FullRowSelect = true
            };
            listSpace.Columns.Add("排名", 40);
            listSpace.Columns.Add("用户", 180);
            listSpace.Columns.Add("瘦身", 100);
            table.Controls.Add(listCount, 0, 0);
            table.Controls.Add(listDuration, 1, 0);
            table.Controls.Add(listSpace, 2, 0);
            var lblMy = new Label {
                Dock = DockStyle.Bottom,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(DefaultFont, FontStyle.Bold),
                Text = "正在加载数据..."
            };
            form.Controls.Add(table);
            form.Controls.Add(lblMy);
            form.Shown += async (_, __) => {
                var data = await LeaderboardService.GetLeaderboardAsync();
                var sortedCount = data.OrderByDescending(x => x.TotalSwitches).ToList();
                for (int i = 0; i < sortedCount.Count; i++) {
                    var u = sortedCount[i];
                    string name = u.Name;
                    if (i == 0)
                        name = $"🥇 {u.Name} (🌭切线王)";
                    else if (i == 1)
                        name = $"🥈 {u.Name}";
                    else if (i == 2)
                        name = $"🥉 {u.Name}";
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
                    else if (i == 1)
                        name = $"🥈 {u.Name}";
                    else if (i == 2)
                        name = $"🥉 {u.Name}";
                    listDuration.Items.Add(new ListViewItem(new[] {
                        (i + 1).ToString(), name, FormatDuration(u.TotalDuration)
                    }));
                }

                var sortedSpace = data.OrderByDescending(x => x.TotalSpaceCleaned).ToList();
                int rankSpace = 1;
                foreach (var u in sortedSpace) {
                    if (u.TotalSpaceCleaned <= 0)
                        continue;
                    string name = u.Name;
                    if (rankSpace == 1)
                        name = $"💪 {u.Name} (🥦瘦身王)";
                    else if (rankSpace == 2)
                        name = $"🥈 {u.Name}";
                    else if (rankSpace == 3)
                        name = $"🥉 {u.Name}";
                    listSpace.Items.Add(new ListViewItem(new[] {
                        rankSpace.ToString(), name, FormatSize(u.TotalSpaceCleaned)
                    }));
                    rankSpace++;
                }

                var me = data.FirstOrDefault(x => x.Name == Environment.UserName);
                if (me != null) {
                    lblMy.Text = $"我：切线{me.TotalSwitches}次 | 摸鱼{FormatDuration(me.TotalDuration)} | 瘦身{FormatSize(me.TotalSpaceCleaned)}";
                } else {
                    lblMy.Text = "暂无数据";
                }
            };
            form.ShowDialog(this);
        }

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
            int totalRepos = 0;
            foreach (var parent in selectedParents) {
                var cache = _settings.RepositoryCache.FirstOrDefault(x => string.Equals(x.ParentPath, parent, StringComparison.OrdinalIgnoreCase));
                if (cache == null || cache.Children.Count == 0)
                    continue;
                Log($"=== 清理父节点: {Path.GetFileName(parent)} ===");
                foreach (var repoInfo in cache.Children) {
                    totalRepos++;
                    Log($" >>> [清理中] {repoInfo.Name} ...");
                    statusLabel.Text = $"正在瘦身: {repoInfo.Name}";
                    var (ok, log, sizeStr, saved) = await Task.Run(() => GitHelper.GarbageCollect(repoInfo.FullPath, true));
                    if (ok) {
                        totalSavedBytes += saved;
                        Log($"[成功] {repoInfo.Name}: 减小 {sizeStr}");
                    } else {
                        string errorSummary = "未知错误";
                        if (!string.IsNullOrWhiteSpace(log)) {
                            var lines = log.Split(new[] {
                                '\r', '\n'
                            }, StringSplitOptions.RemoveEmptyEntries);
                            var errorLine = lines.LastOrDefault(l => l.Contains("❌") || l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.Contains("fatal", StringComparison.OrdinalIgnoreCase));
                            errorSummary = errorLine ?? lines.LastOrDefault() ?? "无日志返回";
                        }

                        Log($"[失败] {repoInfo.Name}: {errorSummary}");
                    }
                }
            }

            this.Enabled = true;
            statusLabel.Text = "清理完成";
#if !BOSS_MODE && !PURE_MODE
            if (!string.IsNullOrEmpty(_settings.LeaderboardPath)) {
                var stats = await LeaderboardService.UploadMyScoreAsync(0, totalSavedBytes);
                UpdateStatsUi(stats.totalCount, stats.totalTime, stats.totalSpace);
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

        private void Log(string s) => txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");

        private async Task PullAllAsync() {
            var items = lvRepos.Items.Cast<ListViewItem>().Where(i => i.Checked).ToList();
            if (!items.Any()) {
                MessageBox.Show("请先勾选需要拉取的仓库！");
                return;
            }

            repoToolbar.Enabled = false;
            btnSwitchAll.Enabled = false;
            statusProgress.Visible = true;
            statusLabel.Text = "正在批量拉取...";
            Log("=== 开始批量 Pull ===");
            int finishCount = 0;
            var pullTasks = new List<Task>();
            var batchSw = Stopwatch.StartNew();
            foreach (var item in items) {
                pullTasks.Add(Task.Run(async () => {
                    await sem.WaitAsync();
                    var r = (GitRepo)item.Tag;
                    try {
                        BeginInvoke((Action)(() => {
                            item.Text = "⏳";
                        }));
                        var res = GitHelper.PullCurrentBranch(r.Path);
                        BeginInvoke((Action)(() => {
                            item.Text = res.ok? "✅" : "❌";
                            Log($"[{r.Name}] {r.CurrentBranch}: {res.message}");
                            statusLabel.Text = $"拉取中 {++finishCount}/{items.Count}";
                        }));
                    } finally {
                        sem.Release();
                    }
                }));
            }

            await Task.WhenAll(pullTasks);
            batchSw.Stop();
            statusProgress.Visible = false;
            repoToolbar.Enabled = true;
            btnSwitchAll.Enabled = true;
            statusLabel.Text = $"拉取完成，耗时 {batchSw.Elapsed.TotalSeconds:F1}s";
            Log($"🏁 批量拉取结束");
        }

        private async Task SwitchAllAsync() {
            var target = cmbTargetBranch.Text.Trim();
            if (string.IsNullOrEmpty(target)) {
                MessageBox.Show("请输入分支名");
                return;
            }

            var items = lvRepos.Items.Cast<ListViewItem>().Where(i => i.Checked).ToList();
            if (!items.Any())
                return;
            btnSwitchAll.Enabled = false;
            statusProgress.Visible = true;
            SetSwitchState(SwitchState.Switching);
            foreach (var i in items) {
                i.Text = "⏳";
                i.SubItems[1].Text = "...";
            }

            var batchSw = Stopwatch.StartNew();
            foreach (var item in items) {
                tasks.Add(Task.Run(async () => {
                    await sem.WaitAsync();
                    var r = (GitRepo)item.Tag;
                    var sw = Stopwatch.StartNew();
                    try {
                        var res = GitHelper.SwitchAndPull(r.Path, target, _settings.StashOnSwitch, _settings.FastMode);
                        r.SwitchOk = res.ok;
                        r.LastMessage = res.message;
                        r.CurrentBranch = GitHelper.GetFriendlyBranch(r.Path);
                    } finally {
                        sw.Stop();
                        sem.Release();
                    }

                    BeginInvoke((Action)(() => {
                        item.Text = (r.SwitchOk? "✅" : "❌") + $" {sw.Elapsed.TotalSeconds:F1}s";
                        item.SubItems[1].Text = r.CurrentBranch;
                        Log($"[{r.Name}] {r.LastMessage?.Replace("\n", " ")}");
                        if (r.SwitchOk) {
                            ApplyImageTo(pbFlash, "flash_success", FLASH_BOX);
                            pbFlash.Visible = true;
                            flashTimer.Start();
                        }

                        statusLabel.Text = $"处理中 {++done}/{items.Count}";
                    }));
                }));
            }

            await Task.WhenAll(tasks);
            batchSw.Stop();
#if !BOSS_MODE && !PURE_MODE
            if (!string.IsNullOrEmpty(_settings.LeaderboardPath)) {
                var (nc, nt, ns) = await LeaderboardService.UploadMyScoreAsync(batchSw.Elapsed.TotalSeconds, 0);
                UpdateStatsUi(nc, nt, ns);
            }
#endif
            SetSwitchState(SwitchState.Done);
            statusProgress.Visible = false;
            btnSwitchAll.Enabled = true;
            statusLabel.Text = "完成";
            Log("🏁 全部完成");
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
    }
}