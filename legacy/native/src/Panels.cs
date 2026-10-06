using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexMonitor
{
    public static class PanelStyle
    {
        static MonitorTheme theme = MonitorTheme.Find("mint");
        public static Color Background { get { return theme.Background; } }
        public static Color Sidebar { get { return theme.Top; } }
        public static Color Card { get { return theme.Card; } }
        public static Color Border { get { return theme.Border; } }
        public static Color Text { get { return theme.Text; } }
        public static Color Muted { get { return theme.Muted; } }
        public static Color Accent { get { return theme.Accent; } }
        public static void SetTheme(MonitorTheme value) { theme = value; }
        public static Color Blend(Color first, Color second, double weight)
        {
            return Color.FromArgb((int)(first.R * (1 - weight) + second.R * weight), (int)(first.G * (1 - weight) + second.G * weight), (int)(first.B * (1 - weight) + second.B * weight));
        }
        public static float Scale = 1;
        public static int S(float value) { return (int)Math.Round(value * Scale); }
        public static GraphicsPath Round(RectangleF rect, float radius)
        {
            var path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        public static void TextAt(Graphics g, string value, Rectangle bounds, float size, Color color, bool bold = false)
        {
            using (var font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, value, font, bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
    public class RoundedPanelForm : Form
    {
        protected readonly ScrollBody Body;
        readonly List<PanelButton> tabs = new List<PanelButton>();
        readonly string subtitle;
        protected int nextY;
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
        public RoundedPanelForm(string title, string description)
        {
            Text = title; subtitle = description;
            Icon = AppLogo.CreateIcon(32);
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            using (var graphics = CreateGraphics()) PanelStyle.Scale = graphics.DpiX / 96f;
            ClientSize = new Size(S(780), S(570));
            BackColor = PanelStyle.Background; ForeColor = PanelStyle.Text; DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9);
            Body = new ScrollBody { Bounds = Rect(216, 108, 536, 422), BackColor = BackColor, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(Body);
            var close = new PanelButton("×", "") { Bounds = Rect(718, 22, 36, 36), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            close.Click += delegate { Close(); }; Controls.Add(close);
            MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left && e.Y < S(95)) { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); } };
            KeyPreview = true; KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            Shown += delegate { UpdateRegion(); };
        }
        protected static int S(float value) { return PanelStyle.S(value); }
        protected static Rectangle Rect(int x, int y, int width, int height) { return new Rectangle(S(x), S(y), S(width), S(height)); }
        protected int BodyWidth { get { return Body.ClientSize.Width - S(24); } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }
        void UpdateRegion()
        {
            var old = Region;
            using (var path = PanelStyle.Round(new RectangleF(0, 0, Width, Height), S(18))) Region = new Region(path);
            if (old != null) old.Dispose();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (Width > 0 && Height > 0) UpdateRegion(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(PanelStyle.Sidebar)) g.FillRectangle(brush, 0, 0, S(188), Height);
            using (var pen = new Pen(PanelStyle.Border)) { g.DrawLine(pen, S(188), 0, S(188), Height); using (var path = PanelStyle.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), S(18))) g.DrawPath(pen, path); }
            AppLogo.Draw(g, Rect(26, 26, 30, 30));
            PanelStyle.TextAt(g, "CODEX", Rect(64, 24, 104, 22), 12, PanelStyle.Text, true);
            PanelStyle.TextAt(g, "QUOTA ASSISTANT", Rect(65, 46, 112, 16), 6.5f, PanelStyle.Muted);
            PanelStyle.TextAt(g, Text, new Rectangle(S(216), S(24), Width - S(284), S(30)), 17, PanelStyle.Text, true);
            PanelStyle.TextAt(g, subtitle, new Rectangle(S(216), S(62), Width - S(246), S(24)), 9, PanelStyle.Muted);
        }
        public void ApplyTheme()
        {
            BackColor = PanelStyle.Background; ForeColor = PanelStyle.Text;
            ApplyColors(this); Invalidate(true);
        }
        static void ApplyColors(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                control.BackColor = control is PanelButton && parent is RoundedPanelForm && control.Text != "×" ? PanelStyle.Sidebar : PanelStyle.Background;
                var label = control as Label;
                if (label != null) label.ForeColor = (string)label.Tag == "accent" ? PanelStyle.Accent : (string)label.Tag == "muted" ? PanelStyle.Muted : PanelStyle.Text;
                ApplyColors(control); control.Invalidate();
            }
        }
        protected void AddTab(string caption, string number, Action click)
        {
            var button = new PanelButton(caption, "") { Bounds = Rect(18, 112 + tabs.Count * 54, 152, 44), Tag = number, Selected = tabs.Count == 0, BackColor = PanelStyle.Sidebar };
            button.Click += delegate { foreach (var item in tabs) { item.Selected = item == button; item.Invalidate(); } click(); };
            tabs.Add(button); Controls.Add(button);
        }
        protected void ClearBody()
        {
            Body.SuspendLayout();
            while (Body.Controls.Count > 0) Body.Controls[0].Dispose();
            Body.ScrollPosition = Point.Empty; nextY = 0; Body.ResumeLayout();
        }
        protected void Heading(string title, string description)
        {
            AddLabel(title, 15, PanelStyle.Text, 30, true);
            AddLabel(description, 9, PanelStyle.Muted, 34, false); nextY += S(12);
        }
        protected Label AddLabel(string text, float size, Color color, int height, bool bold)
        {
            var label = new Label { Text = text, Bounds = new Rectangle(0, nextY, BodyWidth, S(height)), Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, BackColor = PanelStyle.Background, AutoEllipsis = true, Tag = color == PanelStyle.Accent ? "accent" : color == PanelStyle.Muted ? "muted" : "text" };
            Body.Controls.Add(label); nextY += S(height); return label;
        }
        protected void Note(string value) { nextY += S(12); AddLabel(value, 8.5f, PanelStyle.Muted, 62, false); }
        protected void AddCard(string title, string detail)
        {
            var card = new InfoCard(title, detail) { Location = new Point(0, nextY), Width = BodyWidth };
            card.Measure(); Body.Controls.Add(card); nextY += card.Height + S(14);
        }
        protected void AddSwitch(string title, string detail, bool enabled, Action<bool> changed)
        {
            var toggle = new PanelButton(title, detail) { Bounds = new Rectangle(0, nextY, BodyWidth, S(72)), IsSwitch = true, Selected = enabled };
            toggle.Click += delegate { toggle.Selected = !toggle.Selected; toggle.Invalidate(); changed(toggle.Selected); };
            Body.Controls.Add(toggle); nextY += S(84);
        }
    }
    public sealed class PanelButton : Button
    {
        static int S(float value) { return PanelStyle.S(value); }
        public bool Selected, IsSwitch;
        public Color? AccentOverride;
        Color Accent { get { return AccentOverride ?? PanelStyle.Accent; } }
        public string Detail;
        bool hovered;
        public PanelButton(string title, string detail)
        {
            Text = title; Detail = detail; FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AccessibleName = title;
            MouseEnter += delegate { hovered = true; Invalidate(); }; MouseLeave += delegate { hovered = false; Invalidate(); };
            GotFocus += delegate { Invalidate(); }; LostFocus += delegate { Invalidate(); };
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width < 3 || Height < 3) return;
            Region old = Region;
            using (var path = PanelStyle.Round(new RectangleF(1, 1, Width - 2, Height - 2), S(10))) Region = new Region(path);
            if (old != null) old.Dispose();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent is RoundedPanelForm && Text != "×" ? PanelStyle.Sidebar : Parent == null ? PanelStyle.Background : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = PanelStyle.Round(new RectangleF(1, 1, Width - 2, Height - 2), S(10)))
            using (var fill = new SolidBrush(Selected && !IsSwitch ? PanelStyle.Blend(PanelStyle.Card, Accent, .14) : hovered ? PanelStyle.Blend(PanelStyle.Card, PanelStyle.Text, .07) : PanelStyle.Card))
            using (var pen = new Pen((Selected && !IsSwitch) || Focused ? Accent : PanelStyle.Border)) { g.FillPath(fill, path); g.DrawPath(pen, path); }
            if (Text == "×")
            {
                using (var pen = new Pen(PanelStyle.Muted, 1.5f * PanelStyle.Scale)) { g.DrawLine(pen, S(13), S(13), Width - S(13), Height - S(13)); g.DrawLine(pen, Width - S(13), S(13), S(13), Height - S(13)); }
                return;
            }
            int reserve = S(IsSwitch ? 72 : 20);
            PanelStyle.TextAt(g, Text, new Rectangle(S(16), S(string.IsNullOrEmpty(Detail) ? 4 : 12), Width - reserve - S(16), string.IsNullOrEmpty(Detail) ? Height - S(8) : S(24)), 10, Selected && !IsSwitch ? Accent : PanelStyle.Text, true);
            if (!string.IsNullOrEmpty(Detail)) PanelStyle.TextAt(g, Detail, new Rectangle(S(16), S(40), Width - reserve - S(16), S(20)), 8, PanelStyle.Muted);
            if (IsSwitch)
            {
                using (var path = PanelStyle.Round(new RectangleF(Width - S(62), Height / 2 - S(12), S(44), S(24)), S(12)))
                using (var brush = new SolidBrush(Selected ? Accent : PanelStyle.Border)) g.FillPath(brush, path);
                using (var brush = new SolidBrush(Selected ? PanelStyle.Background : PanelStyle.Muted)) g.FillEllipse(brush, Width - S(Selected ? 39 : 59), Height / 2 - S(9), S(18), S(18));
            }
        }
    }
    public sealed class InfoCard : Control
    {
        static int S(float value) { return PanelStyle.S(value); }
        readonly string title, detail;
        public InfoCard(string heading, string body) { title = heading; detail = body; DoubleBuffered = true; }
        public void Measure()
        {
            using (var font = new Font("Microsoft YaHei UI", 9)) Height = S(56) + (string.IsNullOrWhiteSpace(detail) ? 0 : TextRenderer.MeasureText(detail, font, new Size(Width - S(36), 4000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + S(12));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(PanelStyle.Background);
            using (var path = PanelStyle.Round(new RectangleF(1, 1, Width - 2, Height - 2), S(12)))
            using (var fill = new SolidBrush(PanelStyle.Card)) using (var pen = new Pen(PanelStyle.Border)) { g.FillPath(fill, path); g.DrawPath(pen, path); }
            PanelStyle.TextAt(g, title, new Rectangle(S(18), S(14), Width - S(36), S(27)), 10.5f, PanelStyle.Accent, true);
            using (var font = new Font("Microsoft YaHei UI", 9)) TextRenderer.DrawText(g, detail, font, new Rectangle(S(18), S(48), Width - S(36), Height - S(60)), PanelStyle.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
    public sealed class AppearanceForm : RoundedPanelForm
    {
        readonly Preferences preferences;
        readonly Action<string> themeChanged;
        readonly Action<double> opacityChanged;
        readonly Action<int> sizeChanged;
        int selected;
        public AppearanceForm(Preferences options, Action<string> themeAction, Action<double> opacityAction, Action<int> sizeAction) : base("设置", "把悬浮窗调整成你喜欢的样子")
        {
            preferences = options; themeChanged = themeAction; opacityChanged = opacityAction; sizeChanged = sizeAction;
            string[] titles = { "界面主题", "透明度", "窗口大小" };
            for (int i = 0; i < titles.Length; i++) { int index = i; AddTab(titles[i], "0" + (i + 1), delegate { selected = index; RenderPage(); }); }
            RenderPage();
        }
        void RenderPage()
        {
            ClearBody();
            if (selected == 0)
            {
                Heading("界面主题", "四种风格，点击即可预览到悬浮窗。");
                foreach (var theme in MonitorTheme.All)
                {
                    string id = theme.Id;
                    var button = new PanelButton(theme.Name, theme.Id == "mint" ? "清爽薄荷 · 分区卡片" : theme.Id == "paper" ? "明亮留白 · 轻巧线条" : theme.Id == "orbit" ? "柔和紫色 · 环形仪表" : "温暖砂金 · 双栏布局") { Bounds = new Rectangle(0, nextY, BodyWidth, S(72)), Selected = preferences.Theme == id, AccentOverride = theme.Accent };
                    button.Click += delegate { themeChanged(id); RenderPage(); };
                    Body.Controls.Add(button); nextY += S(84);
                }
            }
            else if (selected == 1)
            {
                Heading("透明度", "让额度信息融入桌面，同时保持清晰。");
                var value = AddLabel(Math.Round(preferences.Opacity * 100) + "%", 34, PanelStyle.Accent, 70, true);
                var slider = new SmoothSlider { Value = Math.Max(65, Math.Min(100, (int)Math.Round(preferences.Opacity * 100))), Bounds = new Rectangle(0, nextY + S(8), BodyWidth, S(48)), BackColor = PanelStyle.Background, AccessibleName = "悬浮窗透明度" };
                slider.ValueChanged += delegate { opacityChanged(slider.Value / 100.0); value.Text = slider.Value + "%"; };
                Body.Controls.Add(slider); nextY += S(76);
                AddLabel("65% · 更轻盈                                      100% · 更清晰", 9, PanelStyle.Muted, 30, false);
                AddCard("只调整悬浮窗", "设置面板保持清晰可读。拖动滑块时立即生效，自动保存。");
            }
            else
            {
                Heading("窗口大小", "选择适合屏幕的比例，也可直接拖动悬浮窗边缘。");
                foreach (int percent in new[] { 85, 100, 125, 150, 200 })
                {
                    int choice = percent;
                    int width = Math.Max(176, WindowLayout.DefaultWidth * percent / 100), height = Math.Max(108, (WindowLayout.DefaultHeight + 30) * percent / 100);
                    var button = new PanelButton(percent == 100 ? "100%  ·  默认尺寸" : percent + "%", width + " × " + height + " 逻辑像素") { Bounds = new Rectangle(0, nextY, BodyWidth, S(66)), Selected = Math.Abs(preferences.Width - width) <= 1 && Math.Abs(preferences.Height - height) <= 1 };
                    button.Click += delegate { sizeChanged(choice); RenderPage(); }; Body.Controls.Add(button); nextY += S(78);
                }
            }
            Note("更改即时生效并自动保存，无需点击应用。");
        }
    }
    public sealed class SmoothSlider : Control
    {
        int value = 97;
        public event EventHandler ValueChanged;
        public int Value { get { return value; } set { int next = Math.Max(65, Math.Min(100, value)); if (next == this.value) return; this.value = next; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); } }
        public SmoothSlider() { DoubleBuffered = true; TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.Slider; }
        void SetFromMouse(int x) { Value = 65 + (int)Math.Round(Math.Max(0, Math.Min(1, (x - 12.0) / Math.Max(1, Width - 24))) * 35); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Capture = true; SetFromMouse(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) SetFromMouse(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Left) Value--; if (e.KeyCode == Keys.Right) Value++; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; float x = 12 + (Width - 24) * (Value - 65) / 35f;
            using (var track = new Pen(PanelStyle.Border, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(track, 12, Height / 2, Width - 12, Height / 2);
            if (x > 12) using (var track = new Pen(PanelStyle.Accent, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(track, 12, Height / 2, x, Height / 2);
            using (var fill = new SolidBrush(PanelStyle.Accent)) g.FillEllipse(fill, x - 9, Height / 2 - 9, 18, 18);
            using (var fill = new SolidBrush(PanelStyle.Background)) g.FillEllipse(fill, x - 4, Height / 2 - 4, 8, 8);
        }
    }
    public sealed class ScrollBody : Panel
    {
        readonly Dictionary<Control, int> positions = new Dictionary<Control, int>();
        int offset, dragY, dragOffset;
        bool dragging;
        public Point ScrollPosition { get { return new Point(0, -offset); } set { ScrollTo(value.Y); } }
        public ScrollBody() { DoubleBuffered = true; AutoScroll = false; }
        int ContentHeight { get { int bottom = 0; foreach (var pair in positions) bottom = Math.Max(bottom, pair.Value + pair.Key.Height); return bottom; } }
        int Maximum { get { return Math.Max(0, ContentHeight - Height); } }
        void ScrollTo(int requested)
        {
            offset = Math.Max(0, Math.Min(Maximum, requested));
            SuspendLayout(); foreach (var pair in positions) pair.Key.Top = pair.Value - offset; ResumeLayout(false); Invalidate();
        }
        protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); positions[e.Control] = e.Control.Top + offset; Invalidate(); }
        protected override void OnControlRemoved(ControlEventArgs e) { positions.Remove(e.Control); base.OnControlRemoved(e); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ScrollTo(offset - e.Delta / 3); }
        Rectangle Thumb()
        {
            int height = Math.Min(Height, Math.Max(36, Height * Height / Math.Max(1, ContentHeight)));
            return new Rectangle(Width - 10, Maximum == 0 ? 0 : offset * (Height - height) / Maximum, 4, height);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Maximum == 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = PanelStyle.Round(Thumb(), 2)) using (var brush = new SolidBrush(PanelStyle.Border)) e.Graphics.FillPath(brush, path);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left || e.X < Width - 20 || Maximum == 0) return;
            dragging = true; dragY = e.Y; dragOffset = offset; Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (dragging) ScrollTo(dragOffset + (e.Y - dragY) * Maximum / Math.Max(1, Height - Thumb().Height));
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; }
    }
}
