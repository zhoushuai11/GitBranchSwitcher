using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GitBranchSwitcher
{
    public class BranchFavoritesForm : Form
    {
        private ListBox _lbBranches;
        private AppSettings _settings;
        private Action<string> _onSelectCallback;

        public BranchFavoritesForm(AppSettings settings, Action<string> onSelectCallback)
        {
            _settings = settings;
            _onSelectCallback = onSelectCallback;

            if (_settings.FavoriteBranches == null)
                _settings.FavoriteBranches = new List<FavoriteItem>();

            InitializeComponent();
            RefreshList();
        }

        private void InitializeComponent()
        {
            this.Text = "⭐ 分支收藏夹 (双击填入)";
            this.Width = 500;
            this.Height = 600;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            
            // === 列表区域 (开启自绘模式) ===
            _lbBranches = new ListBox
            {
                Dock = DockStyle.Fill,
                DrawMode = DrawMode.OwnerDrawVariable, // 开启自绘，允许不同高度（虽暂未使用）
                ItemHeight = 45, // 设置单行高度，留出空间显示两行文字或分隔符
                IntegralHeight = false,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White
            };
            
            // 绑定绘制事件
            _lbBranches.DrawItem += _lbBranches_DrawItem;
            _lbBranches.MeasureItem += (s, e) => e.ItemHeight = 45; // 固定高度
            _lbBranches.DoubleClick += (s, e) => SelectAndClose();

            // === 底部按钮区域 ===
            var pnlBottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10),
                BackColor = Color.WhiteSmoke
            };

            var btnClose = CreateButton("关闭", Color.IndianRed);
            btnClose.Click += (s, e) => this.Close();

            var btnAdd = CreateButton("➕ 添加收藏", Color.DodgerBlue);
            btnAdd.Width = 100;
            btnAdd.Click += (s, e) => AddNewBranchDialog();
            
            var btnDelete = CreateButton("🗑️ 删除", Color.Gray);
            btnDelete.Click += (s, e) => DeleteSelected();

            pnlBottom.Controls.Add(btnClose);
            pnlBottom.Controls.Add(btnAdd);
            pnlBottom.Controls.Add(btnDelete);

            this.Controls.Add(_lbBranches);
            this.Controls.Add(pnlBottom);
        }

        // === 核心逻辑：自绘列表项 ===
        private void _lbBranches_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _settings.FavoriteBranches.Count) return;

            var item = _settings.FavoriteBranches[e.Index];
            
            // 1. 绘制背景
            e.DrawBackground();
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            
            // 定义画笔和字体
            Brush textBrush = isSelected ? Brushes.White : Brushes.Black;
            Brush remarkBrush = isSelected ? Brushes.LightGray : Brushes.Gray;
            
            using (var fontTitle = new Font("Segoe UI", 11, FontStyle.Bold))
            using (var fontRemark = new Font("Segoe UI", 9, FontStyle.Regular))
            {
                // 2. 绘制分支名称 (第一行)
                e.Graphics.DrawString(item.Branch, fontTitle, textBrush, e.Bounds.X + 5, e.Bounds.Y + 5);

                // 3. 绘制备注 (后面) - 如果有备注
                if (!string.IsNullOrEmpty(item.Remark))
                {
                    string remarkText = $"📝 {item.Remark}";
                    // 也可以选择绘制在第二行，这里示例绘制在名称后面或下方
                    // 方案A：绘制在名称下方 (更整齐)
                    e.Graphics.DrawString(remarkText, fontRemark, remarkBrush, e.Bounds.X + 5, e.Bounds.Y + 24);
                }
                else 
                {
                    // 如果没备注，显示提示
                    e.Graphics.DrawString("(无备注)", fontRemark, Brushes.LightGray, e.Bounds.X + 5, e.Bounds.Y + 24);
                }
            }

            // 4. 绘制明显的分割线 (底部)
            using (var penLine = new Pen(Color.LightGray, 1))
            {
                // 虚线或者实线，这里用实线
                int y = e.Bounds.Bottom - 1;
                e.Graphics.DrawLine(penLine, e.Bounds.Left, y, e.Bounds.Right, y);
            }

            // 绘制焦点框
            e.DrawFocusRectangle();
        }

        private void AddNewBranchDialog()
        {
            // 弹出自定义的添加窗口
            using (var dlg = new AddEditDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var newItem = new FavoriteItem 
                    { 
                        Branch = dlg.BranchName, 
                        Remark = dlg.Remark 
                    };
                    
                    // 简单查重 (只查分支名)
                    if (!_settings.FavoriteBranches.Any(x => x.Branch == newItem.Branch))
                    {
                        _settings.FavoriteBranches.Add(newItem);
                        _settings.Save();
                        RefreshList();
                    }
                    else
                    {
                        MessageBox.Show("该分支已存在！");
                    }
                }
            }
        }

        private void DeleteSelected()
        {
            if (_lbBranches.SelectedIndex >= 0)
            {
                var item = _settings.FavoriteBranches[_lbBranches.SelectedIndex];
                if (MessageBox.Show($"确定删除 [{item.Branch}] 吗？", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    _settings.FavoriteBranches.RemoveAt(_lbBranches.SelectedIndex);
                    _settings.Save();
                    RefreshList();
                }
            }
        }

        private void SelectAndClose()
        {
            if (_lbBranches.SelectedIndex >= 0)
            {
                var item = _settings.FavoriteBranches[_lbBranches.SelectedIndex];
                _onSelectCallback?.Invoke(item.Branch);
                this.Close();
            }
        }

        private void RefreshList()
        {
            // ListBox OwnerDraw 模式下 Items 集合仅仅用来控制数量和索引，对象本身存这里
            _lbBranches.Items.Clear();
            foreach (var item in _settings.FavoriteBranches)
            {
                _lbBranches.Items.Add(item); // 添加对象
            }
        }

        private Button CreateButton(string text, Color baseColor)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                Height = 35,
                MinimumSize = new Size(80, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = baseColor,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Margin = new Padding(5)
            };
        }

        // === 内部类：简单的添加/编辑对话框 ===
        class AddEditDialog : Form
        {
            public string BranchName => txtBranch.Text.Trim();
            public string Remark => txtRemark.Text.Trim();

            private TextBox txtBranch, txtRemark;

            public AddEditDialog()
            {
                Text = "添加新收藏";
                Width = 400; Height = 250;
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false; MinimizeBox = false;

                var lbl1 = new Label { Text = "分支名称:", Left = 20, Top = 20, AutoSize = true };
                txtBranch = new TextBox { Left = 20, Top = 45, Width = 340 };

                var lbl2 = new Label { Text = "备注信息:", Left = 20, Top = 85, AutoSize = true };
                txtRemark = new TextBox { Left = 20, Top = 110, Width = 340, PlaceholderText = "例如：开发分支 / 紧急修复" };

                var btnOk = new Button { Text = "保存", Left = 260, Top = 160, Width = 100, Height = 35, DialogResult = DialogResult.OK, BackColor = Color.DodgerBlue, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                var btnCancel = new Button { Text = "取消", Left = 150, Top = 160, Width = 100, Height = 35, DialogResult = DialogResult.Cancel };

                Controls.AddRange(new Control[] { lbl1, txtBranch, lbl2, txtRemark, btnOk, btnCancel });
                AcceptButton = btnOk;
                CancelButton = btnCancel;
            }
        }
    }
}