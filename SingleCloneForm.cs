using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GitBranchSwitcher {
    public class SingleCloneForm : Form {
        private readonly string _defaultBranch;
        private readonly string _currentRepoBranch;
        private readonly ComboBox cmbParent;
        private readonly TextBox txtBranch;
        private readonly TextBox txtRepos;
        private readonly RadioButton rbExistingDir;
        private readonly RadioButton rbFreshClone;
        private readonly Label lblTarget;
        private readonly TextBox txtLog;
        private readonly Button btnStart;

        public string SelectedParentPath { get; private set; } = "";
        public List<string> TargetPaths { get; } = new List<string>();

        private class CloneRequest {
            public string RepoUrl { get; set; } = "";
            public string RelativePath { get; set; } = "";
            public string TargetPath { get; set; } = "";
        }

        public SingleCloneForm(IEnumerable<string> parentPaths, string defaultBranch, string? initialParentPath = null, string? currentRepoBranch = null) {
            _defaultBranch = string.IsNullOrWhiteSpace(defaultBranch) ? "" : defaultBranch.Trim();
            _currentRepoBranch = string.IsNullOrWhiteSpace(currentRepoBranch) ? "" : currentRepoBranch.Trim();
            Text = "\u514b\u9686\u4ed3\u5e93";
            Width = 760;
            Height = 600;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9F);

            var main = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 6
            };
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var fields = new TableLayoutPanel {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                Padding = new Padding(0, 0, 0, 8)
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            cmbParent = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
            foreach (var path in parentPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
                cmbParent.Items.Add(path);
            if (!string.IsNullOrWhiteSpace(initialParentPath)) {
                cmbParent.Text = initialParentPath;
            } else if (cmbParent.Items.Count > 0) {
                cmbParent.SelectedIndex = 0;
            }

            var btnBrowse = new Button { Text = "...", Width = 36 };
            btnBrowse.Click += (_, __) => {
                using var fbd = new FolderBrowserDialog();
                var seedPath = cmbParent.Text.Trim();
                if (!Directory.Exists(seedPath))
                    seedPath = parentPaths.FirstOrDefault(Directory.Exists) ?? "";
                if (Directory.Exists(seedPath))
                    fbd.InitialDirectory = seedPath;
                if (fbd.ShowDialog(this) == DialogResult.OK)
                    cmbParent.Text = fbd.SelectedPath;
            };

            AddRow(fields, 0, "\u7236\u8282\u70b9", cmbParent, btnBrowse);
            txtBranch = new TextBox {
                Dock = DockStyle.Fill,
                Text = IsUsableBranch(_currentRepoBranch) ? _currentRepoBranch : IsUsableBranch(_defaultBranch) ? _defaultBranch : "develop",
                PlaceholderText = "\u8fdc\u7a0b\u5206\u652f\uff0c\u4f8b\u5982 develop / main"
            };
            var btnUseCurrentRepoBranch = new Button {
                Text = "\u9009\u7528\u5f53\u524d\u4ed3\u5e93\u5206\u652f",
                AutoSize = true,
                Enabled = IsUsableBranch(_currentRepoBranch)
            };
            btnUseCurrentRepoBranch.Click += (_, __) => txtBranch.Text = _currentRepoBranch;
            AddRow(fields, 1, "\u5206\u652f", txtBranch, btnUseCurrentRepoBranch);

            var modePanel = new FlowLayoutPanel {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 4, 0, 4)
            };
            rbExistingDir = new RadioButton {
                AutoSize = true,
                Text = "\u5df2\u6709\u73b0\u6210\u76ee\u5f55",
                Checked = true,
                Margin = new Padding(0, 3, 24, 3)
            };
            rbFreshClone = new RadioButton {
                AutoSize = true,
                Text = "\u5168\u65b0 clone",
                Margin = new Padding(0, 3, 0, 3)
            };
            modePanel.Controls.Add(rbExistingDir);
            modePanel.Controls.Add(rbFreshClone);

            txtRepos = new TextBox {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = "git@git.tube:sausage-man/u3d-scenes.git",
                PlaceholderText = "\u6bcf\u884c\u4e00\u4e2a Git URL"
            };

            lblTarget = new Label {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 44,
                ForeColor = Color.DimGray
            };

            btnStart = new Button {
                Dock = DockStyle.Top,
                Height = 38,
                Text = "\u5f00\u59cb\u62c9\u7ebf",
                BackColor = Color.AliceBlue,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            btnStart.Click += async (_, __) => await StartAsync();

            txtLog = new TextBox {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 9F),
                BackColor = Color.WhiteSmoke
            };

            main.Controls.Add(fields, 0, 0);
            main.Controls.Add(txtRepos, 0, 1);
            main.Controls.Add(modePanel, 0, 2);
            main.Controls.Add(lblTarget, 0, 3);
            main.Controls.Add(btnStart, 0, 4);
            main.Controls.Add(txtLog, 0, 5);
            Controls.Add(main);

            cmbParent.TextChanged += (_, __) => UpdateTargetPreview();
            rbExistingDir.CheckedChanged += (_, __) => UpdateTargetPreview();
            rbFreshClone.CheckedChanged += (_, __) => UpdateTargetPreview();
            txtRepos.TextChanged += (_, __) => UpdateTargetPreview();
            UpdateTargetPreview();

            var cfg = AppSettings.Load();
            if (cfg.DarkMode)
                ThemeManager.Apply(this);
        }

        private static void AddRow(TableLayoutPanel panel, int row, string labelText, Control editor, Control? extra) {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label {
                Text = labelText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 6, 0, 0)
            };
            panel.Controls.Add(label, 0, row);
            panel.Controls.Add(editor, 1, row);
            panel.Controls.Add(extra ?? new Label { Width = 1 }, 2, row);
        }

        private async Task StartAsync() {
            if (!TryBuildRequests(out var parentPath, out var requests, out var error)) {
                MessageBox.Show(error, "\u514b\u9686\u4ed3\u5e93", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string branch = ResolveBranch();
            if (string.IsNullOrWhiteSpace(branch)) {
                MessageBox.Show("\u8bf7\u8f93\u5165\u8fdc\u7a0b\u5206\u652f\u3002", "\u514b\u9686\u4ed3\u5e93", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool needsAttachConfirm = requests.Any(r =>
                Directory.Exists(r.TargetPath)
                && Directory.GetFileSystemEntries(r.TargetPath).Length > 0
                && !Directory.Exists(Path.Combine(r.TargetPath, ".git")));
            bool needsGitResetConfirm = requests.Any(r =>
                Directory.Exists(Path.Combine(r.TargetPath, ".git")));
            if (needsAttachConfirm) {
                var confirm = MessageBox.Show(
                    "\u5c06\u5728\u5df2\u6709\u6587\u4ef6\u4f46\u6ca1\u6709 .git \u7684\u76ee\u5f55\u4e2d\u91cd\u5efa Git \u5143\u6570\u636e\uff0c\u5e76\u5f3a\u5236\u5bf9\u9f50\u8fdc\u7a0b\u5206\u652f\u3002\n\n\u8bf7\u5148\u786e\u8ba4\u672c\u5730\u6ca1\u6709\u9700\u8981\u4fdd\u7559\u7684\u672a\u63d0\u4ea4\u5185\u5bb9\u3002\u7ee7\u7eed\u5417\uff1f",
                    "\u786e\u8ba4\u91cd\u5efa Git \u5143\u6570\u636e",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.OK)
                    return;
            }
            if (needsGitResetConfirm) {
                var confirm = MessageBox.Show(
                    "\u76ee\u6807\u76ee\u5f55\u5df2\u7ecf\u662f Git \u4ed3\u5e93\u3002\u5de5\u5177\u5c06\u628a origin \u6539\u5230\u65b0\u4ed3\uff0c\u7136\u540e\u5f3a\u5236 reset \u5230\u8fdc\u7a0b\u5206\u652f\u3002\n\n\u8fd9\u4f1a\u8986\u76d6\u672a\u63d0\u4ea4\u5185\u5bb9\uff0c\u8bf7\u5148\u786e\u8ba4\u76ee\u6807\u76ee\u5f55\u9009\u5bf9\u4e86\u3002\u7ee7\u7eed\u5417\uff1f",
                    "\u786e\u8ba4\u91cd\u7f6e\u5df2\u6709 Git \u4ed3\u5e93",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.OK)
                    return;
            }

            btnStart.Enabled = false;
            btnStart.Text = "\u62c9\u7ebf\u4e2d...";
            lblTarget.Text = "\u72b6\u6001\uff1a\u62c9\u7ebf\u4e2d...";
            TargetPaths.Clear();
            AppendLog($"Parent: {parentPath}");
            AppendLog($"Branch: {branch}");
            AppendLog($"Mode: {(rbExistingDir.Checked ? "Existing directory" : "Fresh clone")}");
            AppendLog($"Repos: {requests.Count}");

            try {
                int success = 0;
                int failed = 0;
                foreach (var request in requests) {
                    string? targetParent = Path.GetDirectoryName(request.TargetPath);
                    if (!string.IsNullOrEmpty(targetParent))
                        Directory.CreateDirectory(targetParent);

                    AppendLog("");
                    AppendLog($"==> {request.RepoUrl}");
                    AppendLog($"Target: {request.TargetPath}");

                    var result = await Task.Run(() => GitHelper.CloneOrAttachExisting(
                        request.RepoUrl,
                        request.TargetPath,
                        branch,
                        rbExistingDir.Checked,
                        AppendLog));

                    if (result.ok) {
                        success++;
                        TargetPaths.Add(request.TargetPath);
                        AppendLog(result.message);
                    } else {
                        failed++;
                        AppendLog(result.message);
                    }
                }

                SelectedParentPath = parentPath;
                AppendLog($"Done. Success: {success}, Failed: {failed}");

                if (success > 0) {
                    DialogResult = DialogResult.OK;
                    Close();
                } else {
                    MessageBox.Show("\u6ca1\u6709\u4ed3\u5e93\u514b\u9686\u6210\u529f\uff0c\u8bf7\u67e5\u770b\u65e5\u5fd7\u3002", "\u514b\u9686\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            } finally {
                btnStart.Text = "\u5f00\u59cb\u62c9\u7ebf";
                btnStart.Enabled = true;
                if (!IsDisposed)
                    UpdateTargetPreview();
            }
        }

        private bool TryBuildRequests(out string parentPath, out List<CloneRequest> requests, out string error) {
            parentPath = cmbParent.Text.Trim();
            requests = new List<CloneRequest>();
            error = "";

            if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath)) {
                error = "\u8bf7\u9009\u62e9\u6709\u6548\u7684\u7236\u8282\u70b9\u76ee\u5f55\u3002";
                return false;
            }

            string fullParent = Path.GetFullPath(parentPath);
            string parentPrefix = fullParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var repoUrls = txtRepos.Lines
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (repoUrls.Count == 0) {
                error = "\u8bf7\u81f3\u5c11\u8f93\u5165\u4e00\u4e2a Git URL\u3002";
                return false;
            }

            if (rbExistingDir.Checked && repoUrls.Count > 1) {
                error = "\u5df2\u6709\u73b0\u6210\u76ee\u5f55\u6a21\u5f0f\u53ea\u80fd\u586b\u4e00\u4e2a Git URL\u3002";
                return false;
            }

            foreach (var repoUrl in repoUrls) {
                if (string.IsNullOrWhiteSpace(repoUrl)) {
                    error = "\u5b58\u5728\u7a7a\u7684 Git URL\u3002";
                    return false;
                }

                if (repoUrl.Contains('|')) {
                    error = "\u4ed3\u5e93\u5730\u5740\u8bf7\u53ea\u586b Git URL\uff0c\u76ee\u6807\u8def\u5f84\u8bf7\u586b\u5230\u4e0b\u65b9\u5355\u72ec\u8f93\u5165\u6846\u3002";
                    return false;
                }

                string relativePath = rbExistingDir.Checked ? "." : InferRepoFolder(repoUrl);

                string fullTarget = Path.GetFullPath(Path.Combine(fullParent, relativePath));
                bool isParentItself = fullTarget.Equals(fullParent, StringComparison.OrdinalIgnoreCase);
                if (!isParentItself && !fullTarget.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase)) {
                    error = "\u76ee\u6807\u8def\u5f84\u4e0d\u80fd\u8df3\u51fa\u7236\u8282\u70b9\u76ee\u5f55\u3002";
                    return false;
                }

                requests.Add(new CloneRequest {
                    RepoUrl = repoUrl,
                    RelativePath = relativePath,
                    TargetPath = fullTarget
                });
            }

            var duplicateTarget = requests
                .GroupBy(r => r.TargetPath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicateTarget != null) {
                error = "\u5b58\u5728\u91cd\u590d\u7684\u76ee\u6807\u8def\u5f84\uff1a" + duplicateTarget.Key;
                return false;
            }

            parentPath = fullParent;
            return true;
        }

        private string ResolveBranch() => txtBranch.Text.Trim();

        private static bool IsUsableBranch(string branch) {
            if (string.IsNullOrWhiteSpace(branch))
                return false;
            return !branch.Contains("detached", StringComparison.OrdinalIgnoreCase)
                   && !branch.Contains("unknown", StringComparison.OrdinalIgnoreCase)
                   && !branch.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
                   && !branch.Contains("鈥?");
        }

        private void UpdateTargetPreview() {
            if (TryBuildRequests(out _, out var requests, out _) && requests.Count > 0)
                lblTarget.Text = rbExistingDir.Checked
                    ? "\u76ee\u6807\uff1a\u91cd\u7f6e\u7236\u76ee\u5f55\u672c\u8eab " + requests[0].TargetPath
                    : $"\u76ee\u6807\uff1a{requests.Count} \u4e2a\u4ed3\u5e93\uff0c\u9996\u4e2a {requests[0].TargetPath}";
            else
                lblTarget.Text = "\u76ee\u6807\uff1a-";
        }

        private void AppendLog(string line) {
            if (txtLog.IsDisposed)
                return;

            void Write() => txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\r\n");
            if (txtLog.InvokeRequired)
                txtLog.BeginInvoke((Action)Write);
            else
                Write();
        }

        private static string InferRepoFolder(string repoUrl) {
            string value = repoUrl.Trim().TrimEnd('/', '\\');
            int slash = Math.Max(value.LastIndexOf('/'), value.LastIndexOf(':'));
            if (slash >= 0)
                value = value[(slash + 1)..];
            if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                value = value[..^4];
            return string.IsNullOrWhiteSpace(value) ? "Repository" : value;
        }
    }
}
