using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GitBranchSwitcher
{
    public class CollectionForm : Form
    {
        private FlowLayoutPanel _flowPanel;
        private AppSettings _settings;
        
        private readonly Dictionary<string, Color> _rarityColors = new Dictionary<string, Color> {
            { "N",   Color.Gray },
            { "R",   Color.DodgerBlue },
            { "SR",  Color.MediumPurple },
            { "SSR", Color.Gold },
            { "UR",  Color.Crimson }
        };

        private readonly Dictionary<string, int> _rarityScore = new Dictionary<string, int> {
            { "UR",  5 }, { "SSR", 4 }, { "SR",  3 }, { "R",   2 }, { "N",   1 }
        };

        public CollectionForm()
        {
            _settings = AppSettings.Load();
            InitializeComponent();
            LoadCollection();
        }

        private void InitializeComponent()
        {
            Text = "🖼️ 我的藏品 (My Album)";
            Width = 900; Height = 600;
            // [新增] 设置最小尺寸，防止缩太小
            MinimumSize = new Size(400, 500);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch {}
            
            _flowPanel = new FlowLayoutPanel { 
                Dock = DockStyle.Fill, 
                AutoScroll = true, 
                Padding = new Padding(10), // 减小内边距
                BackColor = Color.WhiteSmoke 
            };
            Controls.Add(_flowPanel);
            
            // 绑定 Resize 事件，如果在极小窗口下，自动调整 Padding 避免滚动条遮挡
            this.Resize += (s, e) => {
                if (this.Width < 500) _flowPanel.Padding = new Padding(5);
                else _flowPanel.Padding = new Padding(10);
            };
        }

        private void LoadCollection()
        {
            _flowPanel.Controls.Clear();
            _flowPanel.SuspendLayout(); // 挂起布局，提高性能

            var myList = CollectionService.Load(Environment.UserName);
            var collected = new HashSet<string>(myList, StringComparer.OrdinalIgnoreCase);
            
            if (collected.Count == 0) {
                var lbl = new Label { 
                    Text = "暂无藏品...\r\n快去切线摸鱼，让青蛙带回明信片吧！", 
                    AutoSize = false, 
                    Width = 300, Height = 100,
                    Font = new Font("Segoe UI", 12), 
                    ForeColor = Color.Gray, 
                    Margin = new Padding(20), 
                    TextAlign = ContentAlignment.MiddleCenter 
                };
                _flowPanel.Controls.Add(lbl);
                _flowPanel.ResumeLayout();
                return;
            }

            var libraryRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _settings.PostcardLibraryRoot);
            var fileMap = new List<(string Name, string Rarity, string Path, int Score)>();

            if (Directory.Exists(libraryRoot)) {
                foreach (var dir in Directory.GetDirectories(libraryRoot)) {
                    var rarityName = Path.GetFileName(dir);
                    if (!_rarityScore.ContainsKey(rarityName)) continue;
                    foreach (var file in Directory.GetFiles(dir)) {
                        var fname = Path.GetFileName(file);
                        if (collected.Contains(fname)) {
                            fileMap.Add((fname, rarityName, file, _rarityScore[rarityName]));
                        }
                    }
                }
            }

            var sortedList = fileMap.OrderByDescending(x => x.Score).ThenBy(x => x.Name).ToList();

            foreach (var item in sortedList) { 
                _flowPanel.Controls.Add(CreateCardControl(item)); 
            }
            
            _flowPanel.ResumeLayout();
            Text = $"🖼️ 我的藏品 (收集进度: {sortedList.Count}/{collected.Count})";
        }

        private Control CreateCardControl((string Name, string Rarity, string Path, int Score) item)
        {
            // [修改] 缩小卡片尺寸，适应小屏幕
            int w = 130, h = 180; 
            
            var panel = new Panel { Width = w, Height = h, Margin = new Padding(8), BackColor = Color.White };
            
            var pb = new PictureBox { 
                Dock = DockStyle.Top, 
                Height = 135, // 调整图片高度
                SizeMode = PictureBoxSizeMode.Zoom, 
                BackColor = Color.FromArgb(245, 245, 245), 
                Cursor = Cursors.Hand 
            };
            
            // 异步加载图片防止卡顿 (虽然这里还是同步，但用了 using stream)
            try { using (var fs = new FileStream(item.Path, FileMode.Open, FileAccess.Read)) { pb.Image = Image.FromStream(fs); } } catch { }
            
            pb.Click += (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", item.Path); } catch {} };
            
            var lbl = new Label { 
                Dock = DockStyle.Fill, 
                Text = $"{Path.GetFileNameWithoutExtension(item.Name)}\n[{item.Rarity}]", 
                TextAlign = ContentAlignment.MiddleCenter, 
                Font = new Font("Segoe UI", 8, FontStyle.Regular), // 字体调小
                ForeColor = _rarityColors.ContainsKey(item.Rarity) ? _rarityColors[item.Rarity] : Color.Black 
            };
            
            panel.Paint += (s, e) => {
                var color = _rarityColors.ContainsKey(item.Rarity) ? _rarityColors[item.Rarity] : Color.Black;
                int borderW = item.Score >= 4 ? 3 : 1; 
                ControlPaint.DrawBorder(e.Graphics, panel.ClientRectangle, color, borderW, ButtonBorderStyle.Solid, color, borderW, ButtonBorderStyle.Solid, color, borderW, ButtonBorderStyle.Solid, color, borderW, ButtonBorderStyle.Solid);
            };
            panel.Padding = new Padding(item.Score >= 4 ? 3 : 1); 
            panel.Controls.Add(lbl); 
            panel.Controls.Add(pb);
            
            var tt = new ToolTip(); tt.SetToolTip(pb, item.Name);
            return panel;
        }
    }
}