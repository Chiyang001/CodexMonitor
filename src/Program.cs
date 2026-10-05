using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexMonitor
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 1 && args[0] == "--export-icon")
            {
                using (var icon = MonitorForm.MakeIcon(64))
                using (var output = File.Create(args[1])) icon.Save(output);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool capture = args.Length > 1 && args[0] == "--capture";
            bool created;
            using (var mutex = new Mutex(true, @"Local\CodexQuotaMonitor", out created))
            {
                if (!created && !capture)
                {
                    try { using (var signal = EventWaitHandle.OpenExisting(@"Local\CodexQuotaMonitor.Show")) signal.Set(); } catch { }
                    return;
                }
                using (var form = new MonitorForm(args.Length > 0 && args[0] == "--watch", capture ? args[1] : null))
                {
                    int width, height;
                    if (capture && args.Length >= 4 && int.TryParse(args[2], out width) && int.TryParse(args[3], out height))
                        form.Size = new Size(width, height);
                    if (capture && args.Length >= 5) form.SetTheme(args[4]);
                    Application.Run(form);
                }
            }
        }
    }
    public sealed class Preferences
    {
        public int X = -1;
        public int Y = -1;
        public bool Pinned = true;
        public double Opacity = 0.97;
        public int Width = WindowLayout.DefaultWidth;
        public int Height = WindowLayout.DefaultHeight;
        public string Theme = "mint";
    }
    public sealed class MonitorForm : Form
    {
        const int WM_NCLBUTTONDOWN = 0xA1;
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        MonitorTheme theme;
        Color background { get { return theme.Background; } }
        Color foreground { get { return theme.Text; } }
        Color muted { get { return theme.Muted; } }
        readonly List<ToolStripMenuItem> themeItems = new List<ToolStripMenuItem>();
        readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly System.Windows.Forms.Timer watch = new System.Windows.Forms.Timer { Interval = 4000 };
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 30000 };
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly ToolTip tooltip = new ToolTip();
        readonly string settingsPath;
        readonly string capturePath;
        readonly EventWaitHandle showSignal;
        readonly Preferences preferences;
        RpcClient client;
        QuotaSnapshot snapshot;
        string authStamp;
        string status = "正在连接 Codex…";
        bool running;
        bool busy;
        bool exiting;
        DateTimeOffset lastAttempt = DateTimeOffset.MinValue;
        float scale = 1;
        int hovered = -1;
        bool pointerInside;
        bool layoutReady;

        public MonitorForm(bool watchOnly, string captureOutput)
        {
            capturePath = captureOutput;
            settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMonitor", "settings.json");
            try { preferences = Json.Serializer.Deserialize<Preferences>(File.ReadAllText(settingsPath)); } catch { preferences = new Preferences(); }
            if (preferences == null) preferences = new Preferences();
            theme = MonitorTheme.Find(preferences.Theme);
            preferences.Theme = theme.Id;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = background;
            ForeColor = foreground;
            Text = "Codex 额度悬浮窗";
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
            MinimumSize = new Size((int)(176 * scale), (int)(108 * scale));
            MaximumSize = new Size((int)(520 * scale), (int)(400 * scale));
            ClientSize = new Size((int)(Math.Max(176, Math.Min(520, preferences.Width)) * scale), (int)(Math.Max(108, Math.Min(400, preferences.Height)) * scale));
            layoutReady = true;
            TopMost = preferences.Pinned;
            Opacity = Math.Max(0.65, Math.Min(1, preferences.Opacity));
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = preferences.X >= 0 ? new Point(preferences.X, preferences.Y) : new Point(area.Right - Width - 24, area.Top + 80);
            KeepOnScreen();
            UpdateShape();
            Icon = MakeIcon();
            tray = new NotifyIcon { Icon = Icon, Text = "Codex 额度悬浮窗", Visible = capturePath == null, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { Show(); };
            menu.Items.Add("显示悬浮窗", null, delegate { Show(); });
            menu.Items.Add("立即刷新", null, async delegate { await RefreshAsync(); });
            var themes = new ToolStripMenuItem("界面主题");
            foreach (var option in MonitorTheme.All)
            {
                string id = option.Id;
                var item = new ToolStripMenuItem(option.Name) { Tag = id, Checked = theme.Id == id };
                item.Click += delegate { SetTheme(id); SavePreferences(); };
                themeItems.Add(item); themes.DropDownItems.Add(item);
            }
            menu.Items.Add(themes);
            SetTheme(theme.Id);
            var pin = new ToolStripMenuItem("始终置顶") { Checked = TopMost, CheckOnClick = true };
            pin.CheckedChanged += delegate { TopMost = preferences.Pinned = pin.Checked; SavePreferences(); Invalidate(); };
            menu.Items.Add(pin);
            var opacity = new ToolStripMenuItem("透明度");
            foreach (int percent in new[] { 100, 90, 80, 70 })
            {
                int value = percent;
                opacity.DropDownItems.Add(value + "%", null, delegate { Opacity = preferences.Opacity = value / 100.0; SavePreferences(); });
            }
            menu.Items.Add(opacity);
            var sizes = new ToolStripMenuItem("窗口大小");
            foreach (int percent in new[] { 85, 100, 125, 150, 200 })
            {
                int value = percent;
                sizes.DropDownItems.Add(value == 100 ? "100% · 默认" : value + "%", null, delegate {
                    Size = new Size((int)(WindowLayout.DefaultWidth * scale * value / 100f), (int)(WindowLayout.DefaultHeight * scale * value / 100f));
                    KeepOnScreen(); SavePreferences();
                });
            }
            menu.Items.Add(sizes);
            menu.Items.Add("重置悬浮窗位置", null, delegate { var bounds = Screen.PrimaryScreen.WorkingArea; Location = new Point(bounds.Right - Width - 24, bounds.Top + 80); SavePreferences(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("打开说明", null, delegate { string readme = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md"); if (File.Exists(readme)) Process.Start(new ProcessStartInfo(readme) { UseShellExecute = true }); });
            menu.Items.Add("退出", null, delegate { exiting = true; Close(); });
            ContextMenuStrip = menu;
            showSignal = capturePath == null ? new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\CodexQuotaMonitor.Show") : null;
            clock.Tick += async delegate
            {
                if (showSignal != null && showSignal.WaitOne(0)) { Show(); }
                Invalidate();
                bool expired = snapshot != null && snapshot.HasExpiredWindow(DateTimeOffset.Now);
                if (running && expired && (DateTimeOffset.Now - lastAttempt).TotalSeconds >= 10) await RefreshAsync();
            };
            watch.Tick += async delegate { await CheckDesktopAsync(); };
            poll.Tick += async delegate { if (running) await RefreshAsync(); };
            Shown += async delegate
            {
                clock.Start(); watch.Start(); poll.Start();
                if (capturePath != null) { running = true; await RefreshAsync(); }
                else { if (watchOnly) Hide(); await CheckDesktopAsync(); }
            };
            MouseMove += delegate(object sender, MouseEventArgs e)
            {
                bool entered = !pointerInside;
                pointerInside = true;
                int hit = HitButton(e.Location);
                if (hit != hovered || entered) { hovered = hit; Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            };
            MouseLeave += delegate { hovered = -1; pointerInside = false; Invalidate(); };
            MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                int hit = HitButton(e.Location);
                if (hit == 1) { Hide(); }
                else { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(2), IntPtr.Zero); KeepOnScreen(); SavePreferences(); }
            };
            ResizeEnd += delegate { KeepOnScreen(); SavePreferences(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        public void SetTheme(string id)
        {
            theme = MonitorTheme.Find(id); preferences.Theme = theme.Id;
            BackColor = background; ForeColor = foreground;
            menu.Renderer = new ToolStripProfessionalRenderer(new ThemeMenuColors(theme));
            menu.BackColor = theme.Card; menu.ForeColor = foreground;
            foreach (var item in themeItems) item.Checked = (string)item.Tag == theme.Id;
            Invalidate();
        }
        sealed class ThemeMenuColors : ProfessionalColorTable
        {
            readonly MonitorTheme theme;
            public ThemeMenuColors(MonitorTheme value) { theme = value; UseSystemColors = false; }
            public override Color ToolStripDropDownBackground { get { return theme.Card; } }
            public override Color ImageMarginGradientBegin { get { return theme.Card; } }
            public override Color ImageMarginGradientMiddle { get { return theme.Card; } }
            public override Color ImageMarginGradientEnd { get { return theme.Card; } }
            public override Color MenuBorder { get { return theme.Border; } }
            public override Color MenuItemSelected { get { return theme.Track; } }
            public override Color MenuItemBorder { get { return theme.Border; } }
            public override Color CheckBackground { get { return theme.Track; } }
        }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000080; return cp; } }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x84 && layoutReady)
            {
                long packed = message.LParam.ToInt64();
                var point = PointToClient(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
                message.Result = new IntPtr(WindowLayout.ResizeHit(point, ClientSize, Math.Max(4, (int)(4 * scale))));
            }
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (layoutReady) { UpdateShape(); Invalidate(); }
        }
        void UpdateShape()
        {
            Region previous = Region;
            using (var shape = Rounded(new RectangleF(0, 0, Width, Height), 12 * WindowLayout.ContentScale(ClientSize))) Region = new Region(shape);
            if (previous != null) previous.Dispose();
        }
        int HitButton(Point point)
        {
            float contentScale = WindowLayout.ContentScale(ClientSize);
            float width = ClientSize.Width / contentScale;
            float x = point.X / contentScale, y = point.Y / contentScale;
            if (y < 6 || y > 28) return -1;
            if (pointerInside && x >= width - 34 && x < width - 12) return 1;
            return -1;
        }
        async Task CheckDesktopAsync()
        {
            if (exiting) return;
            bool found = CodexDiscovery.DesktopRunning();
            if (found && !running)
            {
                running = true;
                snapshot = null; status = "正在连接 Codex…";
                Show(); await RefreshAsync();
            }
            else if (!found && running)
            {
                running = false; snapshot = null;
                StopClient(); Hide(); status = "等待 Codex 启动";
            }
            else if (found && authStamp != CodexDiscovery.AuthStamp())
            {
                snapshot = null; status = "账号已切换，正在更新…"; Invalidate();
                if (!busy) { StopClient(); await RefreshAsync(); }
            }
            else if (!found) { status = "等待 Codex 启动"; Invalidate(); }
        }
        async Task RefreshAsync()
        {
            if (busy || exiting || !running) return;
            busy = true; lastAttempt = DateTimeOffset.Now;
            try
            {
                string stamp = CodexDiscovery.AuthStamp();
                if (client == null || stamp != authStamp)
                {
                    StopClient(); snapshot = null;
                    authStamp = stamp;
                    client = new RpcClient();
                    await client.ConnectAsync(CodexDiscovery.Executable());
                }
                var accountResult = await client.RequestAsync("account/read", new { refreshToken = false });
                var account = Json.Object(accountResult, "account");
                string accountType = Json.String(account, "type");
                if (accountType != "chatgpt" && accountType != "chatgptAuthTokens")
                {
                    snapshot = null;
                    throw new InvalidOperationException(accountType == "apiKey" ? "API Key 登录不提供套餐额度" : "请先在 Codex 中登录 ChatGPT 账号");
                }
                var result = await client.RequestAsync("account/rateLimits/read", null);
                if (!running || exiting) return;
                if (stamp != CodexDiscovery.AuthStamp()) { snapshot = null; StopClient(); status = "账号已切换，正在更新…"; return; }
                snapshot = QuotaSnapshot.Parse(result, Json.String(account, "planType"));
                status = "";
                tooltip.SetToolTip(this, "拖动空白区域移动；拖动边缘或右下角调整大小。\n右键选择大小、置顶和透明度。");
                tray.Text = snapshot.TrayText;
                if (capturePath != null)
                {
                    busy = false;
                    Invalidate(); Update();
                    using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, ClientRectangle); bitmap.Save(capturePath, System.Drawing.Imaging.ImageFormat.Png); }
                    exiting = true; Close();
                }
            }
            catch (Exception error)
            {
                if (exiting || !running) return;
                status = error is TimeoutException || error is InvalidOperationException || error is FileNotFoundException ? error.Message : "连接中断，稍后自动重试";
                tooltip.SetToolTip(this, status + "\n每 30 秒自动重试，也可点击刷新。");
                StopClient();
                if (capturePath != null) { File.WriteAllText(capturePath + ".error.txt", status); exiting = true; Close(); }
            }
            finally { busy = false; if (!exiting) Invalidate(); }
        }
        void StopClient() { if (client != null) { client.Dispose(); client = null; } }
        static string Percent(QuotaWindow window) { return window == null ? "—" : window.Remaining.ToString("0") + "%"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float contentScale = WindowLayout.ContentScale(ClientSize);
            float width = ClientSize.Width / contentScale;
            float height = ClientSize.Height / contentScale;
            g.ScaleTransform(contentScale, contentScale);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var fill = new LinearGradientBrush(new RectangleF(0, 0, width, height), theme.Top, background, LinearGradientMode.Vertical))
            using (var outline = Rounded(new RectangleF(0.5f, 0.5f, width - 1, height - 1), 11.5f))
            using (var pen = new Pen(theme.Border, 1 / contentScale))
            { g.FillPath(fill, outline); g.DrawPath(pen, outline); }
            DrawLogo(g, new RectangleF(14, 10, 13, 13), theme.Accent);
            DrawText(g, "Codex", 34, 7, 78, 20, 8.2f, foreground, true);
            if (snapshot != null) DrawText(g, snapshot.PlanLabel, 83, 8, width - 122, 19, 6.5f, muted, false);
            for (int i = 1; i < 2; i++)
            {
                float x = width - 57 + 23 * i;
                if (hovered == i) using (var brush = new SolidBrush(theme.Track)) using (var path = Rounded(new RectangleF(x, 6, 22, 22), 6)) g.FillPath(brush, path);
            }
            if (pointerInside) using (var pen = new Pen(muted, 0.9f)) { g.DrawLine(pen, width - 27, 13, width - 20, 20); g.DrawLine(pen, width - 20, 13, width - 27, 20); }
            // Keep each quota and its bar grouped when the user chooses a tall aspect ratio.
            float firstRow = 34 + Math.Max(0, height - WindowLayout.DefaultHeight) / 2;
            const float rowHeight = 40;
            var windows = snapshot == null ? new List<QuotaWindow>() : snapshot.Windows;
            if (windows.Count == 0)
            {
                DrawText(g, snapshot == null ? "正在读取额度" : "暂无额度窗口", 16, firstRow + 12, width - 32, 24, 11, foreground, true, false, "Microsoft YaHei UI", true);
                DrawText(g, snapshot == null ? "连接后自动显示" : "账号未提供限额数据", 16, firstRow + 38, width - 32, 20, 7, muted, false, false, "Microsoft YaHei UI", true);
            }
            else if (theme.Layout >= 2)
            {
                float panelWidth = windows.Count == 1 ? width - 16 : (width - 24) / 2;
                for (int i = 0; i < windows.Count; i++)
                    DrawPanel(g, windows[i].Label, windows[i], 8 + (panelWidth + 8) * i, firstRow - 2, panelWidth);
            }
            else
            {
                for (int i = 0; i < windows.Count; i++)
                    DrawWindow(g, windows[i].Label, windows[i], firstRow + (windows.Count == 1 ? 18 : i * rowHeight), width, rowHeight);
            }
            if (pointerInside) using (var pen = new Pen(Color.FromArgb(90, 90, 90), 0.8f))
            { g.DrawLine(pen, width - 12, height - 6, width - 6, height - 12); g.DrawLine(pen, width - 8, height - 6, width - 6, height - 8); }
        }
        void DrawWindow(Graphics g, string label, QuotaWindow window, float y, float width, float rowHeight)
        {
            if (theme.Layout == 0)
                using (var card = Rounded(new RectangleF(8, y - 2, width - 16, rowHeight - 3), 7))
                using (var brush = new SolidBrush(theme.Card)) g.FillPath(brush, card);
            DrawText(g, label, 16, y, width - 100, 18, 7.6f, foreground, false);
            bool expired = window != null && window.Expired(DateTimeOffset.Now);
            bool stale = snapshot != null && (DateTimeOffset.Now - snapshot.Updated).TotalSeconds >= 75;
            Color color = window == null || expired || stale ? muted : foreground;
            DrawText(g, window == null || expired ? "—" : Percent(window), width - 89, y - 3, 75, 24, 13, color, true, true, "Segoe UI");
            DrawText(g, QuotaSnapshot.Countdown(window, DateTimeOffset.Now), 16, y + 16, width - 32, 15, 6.7f, muted, false);
            float barY = y + rowHeight - 6;
            using (var path = Rounded(new RectangleF(16, barY, width - 32, 2.5f), 1.25f))
            using (var brush = new SolidBrush(theme.Track)) g.FillPath(brush, path);
            if (window != null && !expired && window.Remaining > 0)
                using (var path = Rounded(new RectangleF(16, barY, Math.Max(1, (float)((width - 32) * window.Remaining / 100)), 2.5f), 1.25f))
                using (var brush = new SolidBrush(stale ? muted : window.Remaining <= 15 ? Color.FromArgb(210, 139, 74) : theme.Accent)) g.FillPath(brush, path);
        }
        void DrawPanel(Graphics g, string label, QuotaWindow window, float x, float y, float width)
        {
            bool expired = window != null && window.Expired(DateTimeOffset.Now);
            bool stale = snapshot != null && (DateTimeOffset.Now - snapshot.Updated).TotalSeconds >= 75;
            Color accent = window == null || expired || stale ? muted : window.Remaining <= 15 ? Color.FromArgb(210, 139, 74) : theme.Accent;
            string percent = window == null || expired ? "—" : Percent(window);
            using (var path = Rounded(new RectangleF(x, y, width, 78), 8))
            using (var brush = new SolidBrush(theme.Card)) g.FillPath(brush, path);
            DrawText(g, label, x + 8, y + 2, width - 16, 16, 7.2f, muted, false);
            if (theme.Layout == 2)
            {
                float center = x + width / 2;
                var ring = new RectangleF(center - 21, y + 20, 42, 42);
                using (var pen = new Pen(theme.Track, 3)) g.DrawEllipse(pen, ring);
                if (window != null && !expired && window.Remaining > 0)
                    using (var pen = new Pen(accent, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(pen, ring, -90, (float)(window.Remaining * 3.6));
                DrawText(g, percent, x + 6, y + 28, width - 12, 25, 10, foreground, true, false, "Segoe UI", true);
            }
            else
            {
                using (var pen = new Pen(accent, 2)) g.DrawLine(pen, x + 9, y + 24, x + 9, y + 48);
                DrawText(g, percent, x + 17, y + 20, width - 23, 32, 18, foreground, true, false, "Segoe UI");
                using (var pen = new Pen(theme.Track, 3)) g.DrawLine(pen, x + 9, y + 57, x + width - 9, y + 57);
                if (window != null && !expired && window.Remaining > 0)
                    using (var pen = new Pen(accent, 3)) g.DrawLine(pen, x + 9, y + 57, x + 9 + (width - 18) * (float)window.Remaining / 100, y + 57);
            }
            DrawText(g, QuotaSnapshot.Countdown(window, DateTimeOffset.Now), x + 5, y + 63, width - 10, 13, 5.6f, muted, false);
        }
        static void DrawText(Graphics g, string text, float x, float y, float width, float height, float size, Color color, bool bold, bool right = false, string family = "Microsoft YaHei UI", bool center = false)
        {
            // Pixel units keep font sizing independent of the display DPI; the graphics
            // transform already applies the Windows scale factor to the entire layout.
            using (var font = new Font(family, size * 96f / 72f, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = center ? StringAlignment.Center : right ? StringAlignment.Far : StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text, font, brush, new RectangleF(x, y, width, height), format);
        }
        static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
            return path;
        }
        internal static Icon MakeIcon(int pixels = 32)
        {
            using (var bitmap = new Bitmap(pixels, pixels))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.ScaleTransform(pixels / 32f, pixels / 32f);
                using (var fill = new SolidBrush(Color.FromArgb(18, 18, 18)))
                using (var path = Rounded(new RectangleF(0, 0, 32, 32), 7)) g.FillPath(fill, path);
                DrawLogo(g, new RectangleF(5, 5, 22, 22), Color.White);
                IntPtr handle = bitmap.GetHicon();
                try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        static void DrawLogo(Graphics g, RectangleF bounds, Color color)
        {
            using (var pen = new Pen(color, bounds.Width * 4 / 22f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pen, bounds, -90, 280);
                g.DrawLine(pen, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, bounds.X + bounds.Width * 20 / 22f, bounds.Y + bounds.Height / 2);
            }
        }
        void KeepOnScreen()
        {
            var bounds = Screen.FromRectangle(Bounds).WorkingArea;
            Location = new Point(Math.Max(bounds.Left, Math.Min(Left, bounds.Right - Width)), Math.Max(bounds.Top, Math.Min(Top, bounds.Bottom - Height)));
        }
        void SavePreferences()
        {
            if (capturePath != null) return;
            preferences.X = Left; preferences.Y = Top;
            preferences.Width = (int)Math.Round(Width / scale);
            preferences.Height = (int)Math.Round(Height / scale);
            try { Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)); File.WriteAllText(settingsPath, Json.Serializer.Serialize(preferences)); } catch { }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            exiting = true; clock.Stop(); watch.Stop(); poll.Stop();
            SavePreferences(); StopClient(); tray.Visible = false;
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { clock.Dispose(); watch.Dispose(); poll.Dispose(); tray.Dispose(); menu.Dispose(); tooltip.Dispose(); if (showSignal != null) showSignal.Dispose(); if (Icon != null) Icon.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
