using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GitBranchSwitcher {
    public class CollectionForm : Form {
        private FlowLayoutPanel _flowPanel;
        private AppSettings _settings;

        // 稀有度颜色配置
        private readonly Dictionary<string, Color> _rarityColors = new Dictionary<string, Color> {
            {
                "N", Color.Gray
            }, {
                "R", Color.DodgerBlue
            }, {
                "SR", Color.MediumPurple
            }, {
                "SSR", Color.Gold
            }, {
                "UR", Color.Crimson
            }
        };

        // 稀有度权重
        private readonly Dictionary<string, int> _rarityScore = new Dictionary<string, int> {
            {
                "UR", 5
            }, {
                "SSR", 4
            }, {
                "SR", 3
            }, {
                "R", 2
            }, {
                "N", 1
            }
        };

        public CollectionForm() {
            _settings = AppSettings.Load();
            InitializeComponent();
            LoadCollection();
        }

        private void InitializeComponent() {
            Text = "🖼️ 我的藏品图鉴 (Collection Album)";
            Width = 950;
            Height = 650;
            MinimumSize = new Size(400, 500);
            StartPosition = FormStartPosition.CenterScreen;
            try {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            } catch {
            }

            _flowPanel = new FlowLayoutPanel {
                Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(15), BackColor = Color.FromArgb(240, 240, 240) // 浅灰背景
            };
            Controls.Add(_flowPanel);

            // 简单响应式
            this.Resize += (s, e) => {
                _flowPanel.Padding = this.Width < 500? new Padding(5) : new Padding(15);
            };
        }

        private void LoadCollection() {
            _flowPanel.Controls.Clear();
            _flowPanel.SuspendLayout();

            // 1. 获取用户已收集列表
            var myCollectionList = CollectionService.Load(_settings.UpdateSourcePath, Environment.UserName);
            var collectedSet = new HashSet<string>(myCollectionList, StringComparer.OrdinalIgnoreCase);

            // 2. 扫描图库（获取全量卡片）
            var libraryRoot = Path.Combine(_settings.UpdateSourcePath, "Img");
            var allCards = new List<(string Name, string Rarity, string Path, int Score, bool IsCollected)>();

            if (Directory.Exists(libraryRoot)) {
                foreach (var dir in Directory.GetDirectories(libraryRoot)) {
                    var rarityName = Path.GetFileName(dir);
                    if (!_rarityScore.ContainsKey(rarityName))
                        continue;

                    foreach (var file in Directory.GetFiles(dir)) {
                        var fname = Path.GetFileName(file);
                        // 判断是否收集
                        bool hasIt = collectedSet.Contains(fname);
                        allCards.Add((fname, rarityName, file, _rarityScore[rarityName], hasIt));
                    }
                }
            } else {
                // 如果连图库都连不上
                var lbl = new Label {
                    Text = "无法连接到图库服务器...", AutoSize = true, ForeColor = Color.Red
                };
                _flowPanel.Controls.Add(lbl);
                _flowPanel.ResumeLayout();
                return;
            }

            // 3. 排序：已获得优先 > 稀有度高优先 > 名字排序
            var sortedList = allCards.OrderByDescending(x => x.IsCollected) // true(1) 在前
                .ThenByDescending(x => x.Score) // UR 在前
                .ThenBy(x => x.Name).ToList();

            // 4. 生成卡片
            foreach (var item in sortedList) {
                _flowPanel.Controls.Add(CreateCardControl(item));
            }

            // 更新标题统计
            Text = $"🖼️ 藏品图鉴 - 收集进度: {collectedSet.Count}/{allCards.Count} ({(double)collectedSet.Count / allCards.Count:P1})";

            _flowPanel.ResumeLayout();
        }

        private Control CreateCardControl((string Name, string Rarity, string Path, int Score, bool IsCollected) item) {
            int w = 130, h = 180;
            var panel = new Panel {
                Width = w, Height = h, Margin = new Padding(8)
            };

            // 获取稀有度颜色
            Color rarityColor = _rarityColors.ContainsKey(item.Rarity)? _rarityColors[item.Rarity] : Color.Gray;

            // === 上半部分：图片或文字 ===
            Control contentControl;

            if (item.IsCollected) {
                // [已获得]：显示图片，背景为白色
                panel.BackColor = Color.White;

                var pb = new PictureBox {
                    Dock = DockStyle.Top,
                    Height = 135,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.White,
                    Cursor = Cursors.Hand
                };

                // 只有已获得才加载图片流
                try {
                    using (var fs = new FileStream(item.Path, FileMode.Open, FileAccess.Read)) {
                        pb.Image = Image.FromStream(fs);
                    }
                } catch {
                }

                // 点击放大
                pb.Click += (s, e) => {
                    try {
                        System.Diagnostics.Process.Start("explorer.exe", item.Path);
                    } catch {
                    }
                };
                var tt = new ToolTip();
                tt.SetToolTip(pb, item.Name);

                contentControl = pb;
            } else {
                // [未获得]：显示文字 "我是谁？"，背景置灰
                panel.BackColor = Color.FromArgb(224, 224, 224); // 浅灰色背景

                var lblUnknown = new Label {
                    Dock = DockStyle.Top,
                    Height = 135,
                    Text = "我是谁？",
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 12, FontStyle.Italic),
                    ForeColor = Color.Gray, // 文字置灰
                    BackColor = Color.Transparent // 透出Panel的灰底
                };

                contentControl = lblUnknown;
            }

            // === 下半部分：名字 ===
            var lblName = new Label {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8, item.IsCollected? FontStyle.Bold : FontStyle.Regular),
                // 已获得显示名字颜色，未获得显示灰色
                ForeColor = item.IsCollected? rarityColor : Color.DimGray,
                // 未获得时不显示真名，显示 ???
                Text = item.IsCollected? $"{Path.GetFileNameWithoutExtension(item.Name)}\n[{item.Rarity}]" : $"???\n[{item.Rarity}]"
            };

            // === 绘制边框 ===
            // 无论是拥有还是未拥有，都画出稀有度边框，让用户知道自己错过了什么等级的卡
            panel.Paint += (s, e) => {
                // 已获得：实线，粗一点；未获得：虚线或细实线，颜色稍微淡一点
                int borderW = item.IsCollected? (item.Score >= 4? 3 : 2) : 1;
                var borderColor = item.IsCollected? rarityColor : ControlPaint.Light(rarityColor, 0.5f);

                ControlPaint.DrawBorder(e.Graphics, panel.ClientRectangle, borderColor, borderW, ButtonBorderStyle.Solid, borderColor, borderW, ButtonBorderStyle.Solid, borderColor, borderW, ButtonBorderStyle.Solid, borderColor, borderW, ButtonBorderStyle.Solid);
            };

            panel.Padding = new Padding(item.IsCollected && item.Score >= 4? 3 : 1);

            panel.Controls.Add(lblName);
            panel.Controls.Add(contentControl);

            return panel;
        }
    }
}