using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GitBranchSwitcher
{
    public class FloatIconForm : Form
    {
        private readonly MainForm _owner;
        private PictureBox _pbIcon;
        private ContextMenuStrip _contextMenu;
        
        // 拖拽相关变量
        private Point _dragCursorPoint;
        private Point _dragFormPoint;
        private bool _isDragging = false;
        private bool _hasMoved = false;

        // 键盘钩子与动画控制
        private GlobalKeyboardHook _keyboardHook;
        private bool _isAnimEnabled = true;
        private bool _isAnimating = false;

        // [核心配置] 尺寸定义
        // 窗口本身大一点(100)，给图标(80)留出"变大"的空间，否则变大时会被窗口边缘切掉
        private const int FORM_SIZE = 100;   
        private const int ICON_NORMAL = 80;  // 正常大小
        private const int ICON_SMALL = 70;   // 按压变小
        private const int ICON_LARGE = 95;   // 回弹变大 (过冲)

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        public FloatIconForm(MainForm owner, Image displayImage)
        {
            _owner = owner;
            InitializeComponent();
            SetImage(displayImage);
            
            _keyboardHook = new GlobalKeyboardHook();
            _keyboardHook.OnKeyPressed += KeyboardHook_OnKeyPressed;
            
            if (_isAnimEnabled) _keyboardHook.Hook();
        }

        private void InitializeComponent()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true; 
            
            // 1. 设置窗口大小为最大容器尺寸
            this.Width = FORM_SIZE;
            this.Height = FORM_SIZE;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta;

            // 2. 初始化 PictureBox (不再 Dock.Fill)
            _pbIcon = new PictureBox
            {
                // 去掉 Dock，我们手动控制大小和位置
                Dock = DockStyle.None, 
                SizeMode = PictureBoxSizeMode.StretchImage, // 强制拉伸填满控件
                BackColor = Color.Transparent,
                Cursor = Cursors.SizeAll,
                Margin = new Padding(0)
            };
            
            // 初始设置为正常大小并居中
            UpdateIconSize(ICON_NORMAL);

            // 绑定事件
            _pbIcon.MouseDown += OnMouseDown;
            _pbIcon.MouseUp += OnMouseUp;
            _pbIcon.MouseMove += OnMouseMove;
            _pbIcon.MouseClick += OnMouseClick;
            _pbIcon.DoubleClick += OnDoubleClick;

            this.Controls.Add(_pbIcon);

            // 初始化右键菜单
            _contextMenu = new ContextMenuStrip();
            var itemTop = new ToolStripMenuItem("📌 置于顶层", null, (s, e) => {
                this.TopMost = true;
                this.BringToFront();
            }) { Checked = true, CheckOnClick = true };

            var itemBottom = new ToolStripMenuItem("📉 置于底层", null, (s, e) => {
                this.TopMost = false;
                this.SendToBack();
                itemTop.Checked = false;
            });

            var itemAnim = new ToolStripMenuItem("🎹 键盘Q弹动效", null, (s, e) => {
                _isAnimEnabled = !_isAnimEnabled;
                if (_isAnimEnabled) _keyboardHook.Hook();
                else _keyboardHook.Unhook();
            }) { Checked = true, CheckOnClick = true };

            var itemRestore = new ToolStripMenuItem("🔙 还原主窗口", null, (s, e) => RestoreMainWindow());
            var itemExit = new ToolStripMenuItem("❌ 退出程序", null, (s, e) => {
                _keyboardHook?.Unhook();
                Application.Exit(); 
            });
            
            itemTop.CheckedChanged += (s, e) => this.TopMost = itemTop.Checked;

            _contextMenu.Items.AddRange(new ToolStripItem[] { 
                itemTop, itemBottom, new ToolStripSeparator(), 
                itemAnim, new ToolStripSeparator(), 
                itemRestore, itemExit 
            });
        }

        /// <summary>
        /// 核心辅助方法：设置图标大小并自动居中
        /// </summary>
        private void UpdateIconSize(int size)
        {
            // 1. 设置尺寸
            _pbIcon.Size = new Size(size, size);
            
            // 2. 计算居中位置
            int x = (FORM_SIZE - size) / 2;
            int y = (FORM_SIZE - size) / 2;
            _pbIcon.Location = new Point(x, y);
            
            // 3. 强制重绘
            _pbIcon.Refresh();
        }

        private void KeyboardHook_OnKeyPressed(object sender, Keys e)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
                this.BeginInvoke(new Action(() => PerformJumpAnimation()));
            else
                PerformJumpAnimation();
        }

        // [终极动画逻辑] 直接改变控件尺寸
        private async void PerformJumpAnimation()
        {
            // 如果正在播放动画，直接返回，防止鬼畜抖动
            if (_isAnimating || _pbIcon.IsDisposed) return;

            _isAnimating = true;
            try 
            {
                // === 阶段 1: 按压 (变小) ===
                UpdateIconSize(ICON_SMALL); 
                await Task.Delay(50); // 稍微停顿，产生力量感
                
                if (_pbIcon.IsDisposed) return;

                // === 阶段 2: 回弹 (变大/过冲) ===
                UpdateIconSize(ICON_LARGE);
                await Task.Delay(80); // 展示膨胀效果

                if (_pbIcon.IsDisposed) return;

                // === 阶段 3: 恢复 (正常) ===
                UpdateIconSize(ICON_NORMAL);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
            finally
            {
                _isAnimating = false;
            }
        }

        public void SetImage(Image img)
        {
            if (img == null) return;

            // 创建一个高质量的圆形图片，尽量填满画布
            // 我们生成一个大一点的位图，保证缩放清晰度
            int rawSize = 200; 
            Bitmap bmp = new Bitmap(rawSize, rawSize);
            
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                
                // 将原图缩放并填充到这个圆形里
                // 这里使用 DrawImage 而不是 TextureBrush，更容易控制填满
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(0, 0, rawSize, rawSize);
                    g.SetClip(path);
                    g.DrawImage(img, new Rectangle(0, 0, rawSize, rawSize));
                }

                // 描边 (白色圆环)
                g.ResetClip();
                using (Pen p = new Pen(Color.White, 6)) // 线条粗一点，缩放后才明显
                {
                    // 稍微往里缩一点画线，防止被切掉
                    g.DrawEllipse(p, 3, 3, rawSize - 6, rawSize - 6);
                }
            }
            _pbIcon.Image = bmp;
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            _hasMoved = false;
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _hasMoved = false;
                _dragCursorPoint = Cursor.Position;
                _dragFormPoint = this.Location;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point dif = Point.Subtract(Cursor.Position, new Size(_dragCursorPoint));
                if (Math.Abs(dif.X) > 3 || Math.Abs(dif.Y) > 3)
                {
                    this.Location = Point.Add(_dragFormPoint, new Size(dif));
                    _hasMoved = true;
                }
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            _isDragging = false;
        }

        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            if (_hasMoved) return;
            if (e.Button == MouseButtons.Right)
            {
                _contextMenu.Show(Cursor.Position);
            }
        }

        private void OnDoubleClick(object sender, EventArgs e)
        {
            RestoreMainWindow();
        }

        private void RestoreMainWindow()
        {
            _keyboardHook.Unhook();
            _owner.Show();
            _owner.WindowState = FormWindowState.Normal;
            _owner.Activate();
            this.Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _keyboardHook?.Unhook();
            base.OnFormClosing(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var screen = Screen.PrimaryScreen.WorkingArea;
            // 初始位置
            this.Location = new Point(screen.Width - this.Width - 50, screen.Height - this.Height - 100);
        }
    }
}