using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GitBranchSwitcher {
    /// <summary>
    /// Centralized dark theme engine with 3-D effects and rounded corners.
    /// Call ThemeManager.Apply(form) after all controls are created.
    /// </summary>
    public static class ThemeManager {

        // ── Color Palette ─────────────────────────────────────────────────────
        public static readonly Color BgBase       = Color.FromArgb(0x1C, 0x1C, 0x1E);
        public static readonly Color BgSurface    = Color.FromArgb(0x25, 0x25, 0x28);
        public static readonly Color BgPanel      = Color.FromArgb(0x2C, 0x2C, 0x2E);
        public static readonly Color BgInput      = Color.FromArgb(0x32, 0x32, 0x34); // input fields
        public static readonly Color BgButton     = Color.FromArgb(0x42, 0x42, 0x46); // neutral button
        public static readonly Color BorderColor  = Color.FromArgb(0x52, 0x52, 0x56);
        public static readonly Color SplitterBg   = Color.FromArgb(0x30, 0x30, 0x32);

        public static readonly Color TextPrimary   = Color.FromArgb(0xE5, 0xE5, 0xEA);
        public static readonly Color TextSecondary = Color.FromArgb(0x8E, 0x8E, 0x93);
        public static readonly Color TextDisabled  = Color.FromArgb(0x56, 0x56, 0x5A);

        public static readonly Color AccentBlue   = Color.FromArgb(0x0A, 0x84, 0xFF);
        public static readonly Color AccentGreen  = Color.FromArgb(0x30, 0xD1, 0x58);
        public static readonly Color AccentRed    = Color.FromArgb(0xFF, 0x45, 0x3A);
        public static readonly Color AccentYellow = Color.FromArgb(0xFF, 0xD6, 0x0A);
        public static readonly Color AccentPurple = Color.FromArgb(0xBF, 0x5A, 0xF2);
        public static readonly Color AccentPink   = Color.FromArgb(0xFF, 0x37, 0x5F);
        public static readonly Color AccentCyan   = Color.FromArgb(0x5E, 0xC8, 0xFA);

        private static readonly Color BtnBgBlue   = Color.FromArgb(0x0C, 0x2E, 0x52);
        private static readonly Color BtnBgGreen  = Color.FromArgb(0x10, 0x34, 0x1E);
        private static readonly Color BtnBgRed    = Color.FromArgb(0x40, 0x14, 0x14);
        private static readonly Color BtnBgYellow = Color.FromArgb(0x3A, 0x30, 0x0A);
        private static readonly Color BtnBgPurple = Color.FromArgb(0x2C, 0x14, 0x40);
        private static readonly Color BtnBgPink   = Color.FromArgb(0x40, 0x14, 0x26);

        // ── Shape ─────────────────────────────────────────────────────────────
        /// <summary>Radius for all rounded-corner drawing.</summary>
        public const int CornerRadius = 7;
        /// <summary>Radius for panel/GroupBox rounded corners (larger than buttons).</summary>
        public const int PanelCornerRadius = 10;

        /// <summary>Creates a GraphicsPath of a rounded rectangle.</summary>
        public static GraphicsPath GetRoundedPath(Rectangle rect, int r) {
            int d = r * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.X,               rect.Y,                d, d, 180, 90);
            path.AddArc(rect.Right - d,        rect.Y,                d, d, 270, 90);
            path.AddArc(rect.Right - d,        rect.Bottom - d,       d, d,   0, 90);
            path.AddArc(rect.X,               rect.Bottom - d,        d, d,  90, 90);
            path.CloseFigure();
            return path;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Recursively applies the dark theme to root and all descendants.
        /// Uses a snapshot of the control list so WrapTextBox can safely modify the tree.
        /// </summary>
        public static void Apply(Control root) {
            if (root == null) return;
            ApplyOne(root);
            // Snapshot prevents "collection modified" exceptions when WrapTextBox inserts panels
            foreach (Control child in root.Controls.Cast<Control>().ToArray())
                Apply(child);
        }

        // ── Per-control Styling ───────────────────────────────────────────────

        private static void ApplyOne(Control c) {
            switch (c) {
                case Form f:
                    f.BackColor = BgBase;
                    f.ForeColor = TextPrimary;
                    EnableDoubleBuffer(f);
                    // Dark title bar via DWM (Windows 10 1903+ / Windows 11)
                    void ApplyTitleBar() => SetDarkTitleBar(f);
                    if (f.IsHandleCreated) ApplyTitleBar();
                    else f.HandleCreated += (_, __) => ApplyTitleBar();
                    return;

                case StatusStrip ss:
                    ApplyStatusStrip(ss);
                    return;

                case ContextMenuStrip ctx:
                    ApplyToolStrip(ctx);
                    return;

                case GroupBox gb:
                    ApplyGroupBox(gb);
                    return;

                case SplitContainer sc:
                    sc.BackColor = SplitterBg;
                    sc.Panel1.BackColor = BgBase;
                    sc.Panel2.BackColor = BgBase;
                    sc.Panel1.BorderStyle = BorderStyle.None;
                    sc.Panel2.BorderStyle = BorderStyle.None;
                    EnableDoubleBuffer(sc.Panel1);
                    EnableDoubleBuffer(sc.Panel2);
                    return;

                case Button btn:
                    ApplyButton(btn);
                    return;

                case CheckBox chk:
                    chk.BackColor = Color.Transparent;
                    chk.ForeColor = RemapFg(chk.ForeColor);
                    chk.FlatStyle = FlatStyle.Flat;
                    return;

                case ComboBox cmb:
                    WrapComboBox(cmb);
                    return;

                case TextBox txt:
                    // Wrap the TextBox in a rounded inset panel for the 3-D 凹槽 effect
                    WrapTextBox(txt);
                    return; // WrapTextBox applies colours directly

                case NumericUpDown num:
                    num.BackColor = BgInput;
                    num.ForeColor = TextPrimary;
                    num.BorderStyle = BorderStyle.None;
                    return;

                case RichTextBox rtb:
                    if (IsLight(rtb.BackColor)) {
                        rtb.BackColor = rtb.ReadOnly ? BgPanel : BgInput;
                        rtb.ForeColor = TextPrimary;
                    }
                    return;

                case ListView lv:
                    lv.BackColor = BgPanel;
                    lv.ForeColor = TextPrimary;
                    lv.GridLines = false;
                    lv.BorderStyle = BorderStyle.None;
                    // Owner-draw so we can render dark column headers
                    lv.OwnerDraw = true;
                    lv.DrawColumnHeader -= OnListViewDrawColumnHeader;
                    lv.DrawColumnHeader += OnListViewDrawColumnHeader;
                    lv.DrawItem        += (s, e) => e.DrawDefault = true;
                    lv.DrawSubItem     += (s, e) => e.DrawDefault = true;
                    return;

                case CheckedListBox clb:
                    clb.BackColor = BgPanel;
                    clb.ForeColor = TextPrimary;
                    clb.BorderStyle = BorderStyle.None;
                    return;

                case ListBox lb:
                    lb.BackColor = BgPanel;
                    lb.ForeColor = TextPrimary;
                    lb.BorderStyle = BorderStyle.None;
                    return;

                case Label lbl:
                    if (lbl.BackColor != Color.Transparent)
                        lbl.BackColor = Color.Transparent;
                    lbl.ForeColor = RemapFg(lbl.ForeColor);
                    return;

                case PictureBox pb:
                    pb.BackColor = BgBase;
                    return;

                case TabControl tc:
                    tc.BackColor = BgBase;
                    tc.ForeColor = TextPrimary;
                    ApplyTabControl(tc);
                    return;

                case TabPage tp:
                    tp.BackColor = BgSurface;
                    tp.ForeColor = TextPrimary;
                    return;

                case FlowLayoutPanel flp:
                    if (IsLight(flp.BackColor)) flp.BackColor = BgSurface;
                    flp.BorderStyle = BorderStyle.None;
                    return;

                case TableLayoutPanel tlp:
                    if (IsLight(tlp.BackColor)) tlp.BackColor = BgSurface;
                    tlp.BorderStyle = BorderStyle.None;
                    return;

                case Panel p:
                    if (IsLight(p.BackColor)) p.BackColor = BgSurface;
                    p.BorderStyle = BorderStyle.None;
                    return;
            }
        }

        // ── Button: 3-D raised + rounded corners ──────────────────────────────

        private static void ApplyButton(Button btn) {
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.Cursor = Cursors.Hand;

            // Determine semantic background/foreground from original colours
            Color bg = btn.BackColor, fg = btn.ForeColor;
            Color finalBg, finalFg;

            if (bg == Color.DodgerBlue) {
                finalBg = AccentBlue;          finalFg = Color.White;
            } else if (bg == Color.AliceBlue || bg == Color.Azure || fg == Color.DarkBlue) {
                finalBg = BtnBgBlue;           finalFg = AccentCyan;
            } else if (bg == Color.MintCream || bg == Color.Honeydew || fg == Color.DarkGreen || fg == Color.DarkSlateGray) {
                finalBg = BtnBgGreen;          finalFg = AccentGreen;
            } else if (bg == Color.MistyRose || fg == Color.DarkRed || bg == Color.IndianRed) {
                finalBg = BtnBgRed;            finalFg = AccentRed;
            } else if (bg == Color.Ivory || fg == Color.DarkGoldenrod || bg == Color.LightYellow) {
                finalBg = BtnBgYellow;         finalFg = AccentYellow;
            } else if (bg == Color.LavenderBlush) {
                finalBg = BtnBgPurple;         finalFg = AccentPurple;
            } else if (bg == Color.LightPink) {
                finalBg = BtnBgPink;           finalFg = AccentPink;
            } else if (bg == Color.OldLace) {
                finalBg = BgButton;            finalFg = AccentCyan;
            } else if (bg == Color.Gray) {
                finalBg = BgButton;            finalFg = TextSecondary;
            } else {
                finalBg = BgButton;            finalFg = TextPrimary;
            }

            btn.BackColor = finalBg;
            btn.ForeColor = finalFg;

            // Hover (lighten +14) and press (darken −12) states via FlatAppearance
            btn.FlatAppearance.MouseOverBackColor = Lighten(finalBg, 14);
            btn.FlatAppearance.MouseDownBackColor = Darken(finalBg, 12);

            // Rounded Region (clips the rectangular button fill to a pill shape)
            void RefreshRegion() {
                if (btn.Width > 0 && btn.Height > 0) {
                    using var path = GetRoundedPath(
                        new Rectangle(0, 0, btn.Width, btn.Height), CornerRadius);
                    btn.Region = new Region(path);
                }
            }
            btn.HandleCreated += (_, __) => RefreshRegion();
            btn.SizeChanged   += (_, __) => RefreshRegion();
            RefreshRegion();

            // 3-D raised overlay painted AFTER the flat background
            btn.Paint += OnButton3DPaint;
        }

        private static void OnButton3DPaint(object? sender, PaintEventArgs e) {
            if (sender is not Button btn) return;
            var g = e.Graphics;
            g.SmoothingMode   = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            int w = btn.Width - 1, h = btn.Height - 1;
            if (w < 4 || h < 4) return;

            // ① Inner top-left highlight → simulates a raised convex surface
            var innerRect = new Rectangle(1, 1, w - 2, h - 2);
            using var innerPath = GetRoundedPath(innerRect, Math.Max(1, CornerRadius - 1));
            using var hlPen = new Pen(Color.FromArgb(45, 255, 255, 255), 1f);
            g.DrawPath(hlPen, innerPath);

            // ② Bottom-right shadow → reinforces the raised effect
            using var shPen = new Pen(Color.FromArgb(60, 0, 0, 0), 1f);
            var shRect = new Rectangle(1, 1, w - 1, h - 1);
            using var shPath = GetRoundedPath(shRect, Math.Max(1, CornerRadius - 1));
            g.DrawPath(shPen, shPath);

            // ③ Outer crisp border
            Color bg = btn.BackColor;
            var borderColor = Color.FromArgb(
                Math.Max(0, bg.R - 32),
                Math.Max(0, bg.G - 32),
                Math.Max(0, bg.B - 32));
            using var outerPath = GetRoundedPath(new Rectangle(0, 0, w, h), CornerRadius);
            using var borderPen = new Pen(borderColor, 1f);
            g.DrawPath(borderPen, outerPath);
        }

        // ── TextBox: direct dark styling (no wrapper panel to avoid layout issues) ──

        private static void WrapTextBox(TextBox txt) {
            txt.BackColor = BgInput;
            txt.ForeColor = TextPrimary;
            txt.BorderStyle = BorderStyle.FixedSingle;
        }

        // ── ComboBox: direct dark styling + native edit hook ──────────────────

        private static void WrapComboBox(ComboBox cmb) {
            cmb.BackColor = BgInput;
            cmb.ForeColor = TextPrimary;
            cmb.FlatStyle = FlatStyle.Flat;
            // Hook WM_CTLCOLOREDIT so the native Edit child window inside the ComboBox
            // also uses our dark background (BackColor alone is ignored by the native control).
            _ = new ComboBoxEditColorHook(cmb);
        }

        // ── ListBox / CheckedListBox: rounded clip region ──────────────────────

        private static void ApplyRoundedRegion(Control c) {
            void Refresh() {
                if (c.Width > 0 && c.Height > 0) {
                    using var p = GetRoundedPath(
                        new Rectangle(0, 0, c.Width, c.Height), CornerRadius);
                    c.Region = new Region(p);
                }
            }
            c.HandleCreated += (_, __) => Refresh();
            c.SizeChanged   += (_, __) => Refresh();
            Refresh();
        }

        // ── GroupBox: borderless color-block (UserPaint via reflection) ──────────

        private static void ApplyGroupBox(GroupBox gb) {
            // BgSurface is lighter than BgBase so the GroupBox reads as a raised
            // color block against the form background — no border needed.
            gb.BackColor = BgSurface;
            gb.ForeColor = TextSecondary;

            // NOTE: Do NOT use SetStyle(UserPaint | OptimizedDoubleBuffer) on GroupBox.
            // When OptimizedDoubleBuffer is active, WinForms blits the entire back-buffer
            // bitmap to screen — bypassing WS_CLIPCHILDREN — and overwrites child HWND
            // content. This makes child controls appear unresponsive.
            //
            // Without UserPaint, the DC obtained via BeginPaint respects WS_CLIPCHILDREN.

            gb.Paint += OnGroupBoxPaint;
        }

        private static void OnGroupBoxPaint(object? sender, PaintEventArgs e) {
            if (sender is not GroupBox gb) return;
            var g = e.Graphics;
            g.SmoothingMode   = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // ① Fill entire GroupBox with solid background (same as original — preserves
            //    child-control painting and event routing behaviour unchanged).
            using var bgBrush = new SolidBrush(gb.BackColor);
            g.FillRectangle(bgBrush, 0, 0, gb.Width, gb.Height);

            int w = gb.Width - 1, h = gb.Height - 1;
            if (w < 4 || h < 4) return;
            int r = PanelCornerRadius;

            // ② Rounded outer border (slightly lighter than background)
            Color bg = gb.BackColor;
            using var borderPath = GetRoundedPath(new Rectangle(0, 0, w, h), r);
            using var borderPen  = new Pen(Color.FromArgb(
                Math.Min(255, bg.R + 28),
                Math.Min(255, bg.G + 28),
                Math.Min(255, bg.B + 28)), 1.5f);
            g.DrawPath(borderPen, borderPath);

            // ③ Inner top-left highlight → 3-D raised effect
            using var innerPath = GetRoundedPath(new Rectangle(1, 1, w - 2, h - 2), Math.Max(1, r - 1));
            using var hlPen     = new Pen(Color.FromArgb(50, 255, 255, 255), 1f);
            g.DrawPath(hlPen, innerPath);

            // ④ Bottom-right shadow → reinforces 3-D raised effect
            using var shPath = GetRoundedPath(new Rectangle(1, 1, w - 1, h - 1), Math.Max(1, r - 1));
            using var shPen  = new Pen(Color.FromArgb(70, 0, 0, 0), 1f);
            g.DrawPath(shPen, shPath);

            // ⑤ Title text + thin horizontal rule below it
            if (gb.Text.Length > 0) {
                int titleH = gb.Font.Height;
                TextRenderer.DrawText(g, gb.Text, gb.Font,
                    new Point(12, 4), gb.ForeColor,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

                using var divPen = new Pen(Color.FromArgb(35, 255, 255, 255), 1f);
                g.DrawLine(divPen, 8, titleH + 6, gb.Width - 8, titleH + 6);
            }
        }

        // ── TabControl: owner-draw dark tabs ──────────────────────────────────

        private static void ApplyTabControl(TabControl tc) {
            tc.DrawMode = TabDrawMode.OwnerDrawFixed;
            // Padding pushes the tab label text inward; ItemSize controls tab height
            tc.Padding = new Point(12, 4);

            // Guard: do not wire the event twice if Apply() is called again
            tc.DrawItem -= OnTabDrawItem;
            tc.DrawItem += OnTabDrawItem;
        }

        private static void OnTabDrawItem(object? sender, DrawItemEventArgs e) {
            if (sender is not TabControl tc) return;
            var tab  = tc.TabPages[e.Index];
            var g    = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;

            Color bg = sel ? BgPanel   : BgSurface;
            Color fg = sel ? TextPrimary : TextSecondary;

            // Fill the tab header
            using var bgBrush = new SolidBrush(bg);
            g.FillRectangle(bgBrush, e.Bounds);

            // Draw tab label
            TextRenderer.DrawText(g, tab.Text, tc.Font, e.Bounds, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        // ── StatusStrip / ToolStrip ────────────────────────────────────────────

        private static void ApplyStatusStrip(StatusStrip ss) {
            ss.BackColor = BgSurface;
            ss.ForeColor = TextSecondary;
            ss.SizingGrip = false;
            foreach (ToolStripItem item in ss.Items) {
                item.BackColor = BgSurface;
                item.ForeColor = RemapFg(item.ForeColor);
            }
        }

        private static void ApplyToolStrip(ToolStrip ts) {
            ts.BackColor = BgPanel;
            ts.ForeColor = TextPrimary;
            foreach (ToolStripItem item in ts.Items) {
                item.BackColor = BgPanel;
                item.ForeColor = TextPrimary;
            }
        }

        // ── Colour Helpers ────────────────────────────────────────────────────

        private static Color RemapFg(Color c) {
            if (c == Color.DarkSlateBlue)  return Color.FromArgb(0x82, 0xAA, 0xFF);
            if (c == Color.Goldenrod)       return AccentYellow;
            if (c == Color.DarkGoldenrod)   return AccentYellow;
            if (c == Color.DarkGreen)       return AccentGreen;
            if (c == Color.DarkRed)         return AccentRed;
            if (c == Color.DarkBlue)        return AccentCyan;
            if (c == Color.Magenta)         return Color.FromArgb(0xFF, 0x5C, 0xE5);
            if (c == Color.SteelBlue)       return AccentCyan;
            if (c == Color.DimGray)         return TextSecondary;
            if (c == Color.DarkSlateGray)   return TextSecondary;
            if (c == Color.Gray)            return TextSecondary;
            if (!IsLight(c))                return c;
            return TextPrimary;
        }

        public static Color Lighten(Color c, int amount) =>
            Color.FromArgb(
                Math.Min(255, c.R + amount),
                Math.Min(255, c.G + amount),
                Math.Min(255, c.B + amount));

        public static Color Darken(Color c, int amount) =>
            Color.FromArgb(
                Math.Max(0, c.R - amount),
                Math.Max(0, c.G - amount),
                Math.Max(0, c.B - amount));

        private static bool IsLight(Color c) {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            return (0.2126 * r + 0.7152 * g + 0.0722 * b) > 0.35;
        }

        private static void EnableDoubleBuffer(Control c) {
            try {
                typeof(Control)
                    .GetProperty("DoubleBuffered",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(c, true);
            } catch { /* best-effort */ }
        }

        // ── Dark Title Bar (DWM) ──────────────────────────────────────────────

        [DllImport("dwmapi.dll", SetLastError = false)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static void SetDarkTitleBar(Form f) {
            try {
                int v = 1;
                // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (Win11+), fallback 19 (Win10 1903-20H1)
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0)
                    DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
            } catch { /* dwmapi unavailable */ }
        }

        // ── ComboBox: fix native Edit child background via WM_CTLCOLOREDIT ──

        /// <summary>
        /// Hooks into a ComboBox's WndProc via NativeWindow so that the native Win32 Edit
        /// child window (used by DropDown-style ComboBoxes) paints with our dark background.
        /// Without this, BackColor and FlatStyle.Flat do not affect the edit portion on
        /// Windows 10/11 when visual styles are enabled.
        /// </summary>
        private sealed class ComboBoxEditColorHook : NativeWindow {
            private const int WM_CTLCOLOREDIT = 0x0133;
            private const int WM_NCDESTROY    = 0x0082;

            private readonly Color _bg;
            private readonly Color _fg;
            private IntPtr _brush = IntPtr.Zero;

            internal ComboBoxEditColorHook(ComboBox cmb) {
                _bg = cmb.BackColor;
                _fg = cmb.ForeColor;

                void Attach() {
                    if (cmb.IsHandleCreated && Handle == IntPtr.Zero)
                        AssignHandle(cmb.Handle);
                }

                if (cmb.IsHandleCreated) Attach();
                else cmb.HandleCreated += (_, __) => Attach();
            }

            protected override void WndProc(ref Message m) {
                if (m.Msg == WM_CTLCOLOREDIT) {
                    SetBkColor(m.WParam, ColorTranslator.ToWin32(_bg));
                    SetTextColor(m.WParam, ColorTranslator.ToWin32(_fg));
                    if (_brush == IntPtr.Zero)
                        _brush = CreateSolidBrush(ColorTranslator.ToWin32(_bg));
                    m.Result = _brush;
                    return; // handled — do NOT call base, we own the result
                }
                if (m.Msg == WM_NCDESTROY && _brush != IntPtr.Zero) {
                    DeleteObject(_brush);
                    _brush = IntPtr.Zero;
                }
                base.WndProc(ref m);
            }

            [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int cr);
            [DllImport("gdi32.dll")] private static extern int    SetBkColor(IntPtr hdc, int cr);
            [DllImport("gdi32.dll")] private static extern int    SetTextColor(IntPtr hdc, int cr);
            [DllImport("gdi32.dll")] private static extern bool   DeleteObject(IntPtr h);
        }

        // ── ListView: dark column headers ─────────────────────────────────────

        private static void OnListViewDrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e) {
            var g = e.Graphics;
            // Dark header background
            using var bgBrush = new SolidBrush(BgSurface);
            g.FillRectangle(bgBrush, e.Bounds);

            // Right-edge column divider
            using var divPen = new Pen(BorderColor, 1f);
            g.DrawLine(divPen, e.Bounds.Right - 1, e.Bounds.Top + 3,
                               e.Bounds.Right - 1, e.Bounds.Bottom - 3);

            // Bottom separator line
            g.DrawLine(divPen, e.Bounds.Left, e.Bounds.Bottom - 1,
                               e.Bounds.Right, e.Bounds.Bottom - 1);

            // Header label
            var textRect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y,
                                          e.Bounds.Width - 8, e.Bounds.Height);
            TextRenderer.DrawText(g, e.Header.Text, e.Font, textRect, TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}
