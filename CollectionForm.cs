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
                Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(15), BackColor = Color.FromArgb(240, 240, 240)
            };
            Controls.Add(_flowPanel);

            this.Resize += (s, e) => {
                _flowPanel.Padding = this.Width < 500? new Padding(5) : new Padding(15);
            };
        }

        private void LoadCollection() {
            _flowPanel.Controls.Clear();
            _flowPanel.SuspendLayout();

            // [修改] 适配 List<CollectedItem>
            var myItems = CollectionService.Load(_settings.UpdateSourcePath, Environment.UserName);
            var collectedSet = new HashSet<string>(myItems.Select(x => x.FileName), StringComparer.OrdinalIgnoreCase);

            var libraryRoot = Path.Combine(_settings.UpdateSourcePath, "Img");
            var allCards = new List<(string Name, string Rarity, string Path, int Score, bool IsCollected)>();

            if (Directory.Exists(libraryRoot)) {
                foreach (var dir in Directory.GetDirectories(libraryRoot)) {
                    var rarityName = Path.GetFileName(dir);
                    if (!_rarityScore.ContainsKey(rarityName))
                        continue;

                    foreach (var file in Directory.GetFiles(dir)) {
                        var fname = Path.GetFileName(file);
                        bool hasIt = collectedSet.Contains(fname);
                        allCards.Add((fname, rarityName, file, _rarityScore[rarityName], hasIt));
                    }
                }
            } else {
                var lbl = new Label {
                    Text = "无法连接到图库服务器...", AutoSize = true, ForeColor = Color.Red
                };
                _flowPanel.Controls.Add(lbl);
                _flowPanel.ResumeLayout();
                return;
            }

            var sortedList = allCards.OrderByDescending(x => x.IsCollected).ThenByDescending(x => x.Score).ThenBy(x => x.Name).ToList();

            foreach (var item in sortedList) {
                _flowPanel.Controls.Add(CreateCardControl(item));
            }

            Text = $"🖼️ 藏品图鉴 - 收集进度: {collectedSet.Count}/{allCards.Count} ({(double)collectedSet.Count / allCards.Count:P1})";
            _flowPanel.ResumeLayout();
        }

        private Control CreateCardControl((string Name, string Rarity, string Path, int Score, bool IsCollected) item) {
            int w = 130, h = 180;
            var panel = new Panel {
                Width = w, Height = h, Margin = new Padding(8)
            };
            Color rarityColor = _rarityColors.ContainsKey(item.Rarity)? _rarityColors[item.Rarity] : Color.Gray;
            Control contentControl;

            if (item.IsCollected) {
                panel.BackColor = Color.White;
                var pb = new PictureBox {
                    Dock = DockStyle.Top,
                    Height = 135,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.White,
                    Cursor = Cursors.Hand
                };
                try {
                    using (var fs = new FileStream(item.Path, FileMode.Open, FileAccess.Read)) {
                        pb.Image = Image.FromStream(fs);
                    }
                } catch {
                }

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
                panel.BackColor = Color.FromArgb(224, 224, 224);
                var lblUnknown = new Label {
                    Dock = DockStyle.Top,
                    Height = 135,
                    Text = "我是谁？",
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 12, FontStyle.Italic),
                    ForeColor = Color.Gray,
                    BackColor = Color.Transparent
                };
                contentControl = lblUnknown;
            }

            var lblName = new Label {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8, item.IsCollected? FontStyle.Bold : FontStyle.Regular),
                ForeColor = item.IsCollected? rarityColor : Color.DimGray,
                Text = item.IsCollected? $"{Path.GetFileNameWithoutExtension(item.Name)}\n[{item.Rarity}]" : $"???\n[{item.Rarity}]"
            };

            panel.Paint += (s, e) => {
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