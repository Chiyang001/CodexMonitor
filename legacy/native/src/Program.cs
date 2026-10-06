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
                AppLogo.ExportIcon(args[1]);
                return;
            }
            if (args.Length > 1 && args[0] == "--export-logo")
            {
                using (var logo = AppLogo.Render(512)) logo.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
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
        public int Height = WindowLayout.DefaultHeight + 30;
        public string Theme = "mint";
        public bool ResetAlert = true, BeforeResetAlert = false, LowAlert = false, PaceAlert = true;
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
        readonly UsageHistory history = new UsageHistory();
        TaskActivity activity = new TaskActivity();
        readonly HashSet<string> alerted = new HashSet<string>();
        string historyIdentity;
        DecisionForm decision;
        DecisionForm notificationPanel, historyPanel;
        AppearanceForm appearance;
        bool scanning;

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
            menu.Items.Add("设置", null, delegate { OpenAppearance(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("智能额度", null, delegate { OpenDecision(); });
            menu.Items.Add("通知与提醒", null, delegate { OpenPanel("通知与提醒"); });
            menu.Items.Add("历史统计", null, delegate { OpenPanel("历史统计"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { exiting = true; Close(); });
            menu.ShowImageMargin = false;
            menu.Padding = new Padding(7);
            menu.Font = new Font("Microsoft YaHei UI", 9.5f);
            foreach (ToolStripItem item in menu.Items)
                if (!(item is ToolStripSeparator)) { item.AutoSize = false; item.Size = new Size(188, 34); }
            menu.Opened += delegate {
                Region old = menu.Region;
                using (var path = PanelStyle.Round(new RectangleF(0, 0, menu.Width, menu.Height), 12)) menu.Region = new Region(path);
                if (old != null) old.Dispose();
            };
            SetTheme(theme.Id);
            ContextMenuStrip = menu;
            showSignal = capturePath == null ? new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\CodexQuotaMonitor.Show") : null;
            clock.Tick += async delegate
            {
                if (showSignal != null && showSignal.WaitOne(0)) { Show(); }
                Invalidate();
                if (decision != null && !decision.IsDisposed) decision.UpdateContent(BuildDecision());
                if (historyPanel != null && !historyPanel.IsDisposed) historyPanel.UpdateContent(BuildDecision());
                bool expired = snapshot != null && snapshot.HasExpiredWindow(DateTimeOffset.Now);
                if (running && expired && (DateTimeOffset.Now - lastAttempt).TotalSeconds >= 10) await RefreshAsync();
            };
            watch.Tick += async delegate { await CheckDesktopAsync(); await ScanActivityAsync(); };
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
            MouseDoubleClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OpenDecision(); };
        }
        void OpenAppearance()
        {
            if (appearance == null || appearance.IsDisposed)
                appearance = new AppearanceForm(preferences,
                    id => { SetTheme(id); SavePreferences(); },
                    value => { Opacity = preferences.Opacity = value; SavePreferences(); },
                    percent => {
                        Size = new Size((int)Math.Round(WindowLayout.DefaultWidth * scale * percent / 100f), (int)Math.Round((WindowLayout.DefaultHeight + 30) * scale * percent / 100f));
                        KeepOnScreen(); SavePreferences();
                    });
            appearance.Show(); appearance.Activate();
        }
        void OpenPanel(string kind)
        {
            DecisionForm panel = kind == "通知与提醒" ? notificationPanel : historyPanel;
            if (panel == null || panel.IsDisposed)
            {
                panel = new DecisionForm(kind, preferences, SavePreferences);
                if (kind == "通知与提醒") notificationPanel = panel; else historyPanel = panel;
            }
            panel.UpdateContent(BuildDecision()); panel.Show(); panel.Activate();
        }
        async Task ScanActivityAsync()
        {
            if (scanning || capturePath != null) return;
            scanning = true;
            try
            {
                var candidate = activity.Fork(); bool desktop = running; string identity = historyIdentity;
                await Task.Run(() => candidate.Scan(desktop, DateTimeOffset.Now));
                if (exiting || desktop != running || identity != historyIdentity) return;
                activity = candidate;
                if (snapshot != null) activity.Observe(snapshot);
            }
            finally { scanning = false; }
        }
        void OpenDecision()
        {
            if (decision == null || decision.IsDisposed) decision = new DecisionForm();
            decision.UpdateContent(BuildDecision()); decision.Show(); decision.Activate();
        }
        bool Fresh { get { return snapshot != null && (DateTimeOffset.Now - snapshot.Updated).TotalSeconds < 75; } }
        QuotaAdvice Advice(QuotaWindow window) { return Fresh ? history.Analyze(window, DateTimeOffset.Now) : new QuotaAdvice { Explanation = "额度数据未更新，等待重新连接" }; }
        QuotaAdvice OverallHealth()
        {
            if (snapshot == null || snapshot.Windows.Count == 0) return new QuotaAdvice();
            QuotaAdvice unknown = null, healthy = null;
            foreach (var window in snapshot.Windows)
            {
                var advice = Advice(window);
                if (advice.Health == "已耗尽" || advice.Fast) return advice;
                if (advice.Health == "数据不足") unknown = advice; else healthy = advice;
            }
            return unknown ?? healthy ?? new QuotaAdvice();
        }
        string BuildDecision()
        {
            var text = new System.Text.StringBuilder();
            var now = DateTimeOffset.Now;
            text.AppendLine("Codex 额度决策助手").AppendLine();
            if (snapshot == null) return text.AppendLine(status).ToString();
            if (!Fresh) text.AppendLine("额度数据已过期；以下百分比为上次读取值。").AppendLine();
            foreach (var window in snapshot.Windows)
            {
                var advice = Advice(window);
                text.AppendLine(window.Label + " · 剩余 " + (window.Expired(now) ? "未知" : Percent(window)) + " · " + QuotaSnapshot.Countdown(window, now));
                text.AppendLine("额度健康：" + advice.Health + "；" + advice.Explanation);
                text.AppendLine(advice.DailyRate.HasValue ? "近期速度：" + advice.DailyRate.Value.ToString("0.0") + "% / 天；" + (advice.Exhaustion.HasValue ? "预计耗尽：" + advice.Exhaustion.Value.ToLocalTime().ToString("MM-dd HH:mm") + (advice.Fast ? "（早于重置）" : "（本周期重置更早）") : "近期无消耗") : "耗尽预测：数据不足");
                text.AppendLine();
            }
            var budgetWindow = snapshot.Weekly ?? snapshot.Windows.Find(w => w.Minutes >= 1440);
            var budget = Advice(budgetWindow);
            {
                text.AppendLine("今日安全预算（" + (budgetWindow == null ? "无日 / 周额度" : budgetWindow.Label) + "）");
                if (budget.Safe.HasValue)
                {
                    text.AppendLine("今日建议预算 " + budget.Budget.Value.ToString("0.0") + "% · 今日已观测消耗 " + budget.TodayUsed.Value.ToString("0.0") + "%");
                    text.AppendLine("今天还可使用约 " + budget.Safe.Value.ToString("0.0") + "%");
                    text.AppendLine("节省 " + (budget.Safe.Value * 0.6).ToString("0.0") + "% / 均衡 " + budget.Safe.Value.ToString("0.0") + "% / 激进 " + Math.Min(budgetWindow.Remaining, budget.Safe.Value * 1.5).ToString("0.0") + "%");
                }
                else text.AppendLine("等待今天同周期的两次有效采样");
                text.AppendLine("预算从今日首次采样起计算；未观测时段的消耗未知，短周期额度仍可能先耗尽。").AppendLine();
            }
            {
                text.AppendLine("Codex · " + activity.State + "（本地日志推测）");
                if (activity.Started.HasValue)
                {
                    text.AppendLine("最近活动任务 " + (activity.Id ?? "未知") + " · " + Math.Max(0, ((activity.Finished ?? now) - activity.Started.Value).TotalMinutes).ToString("0") + " 分钟");
                    text.AppendLine("模型 " + (activity.Model ?? "未知") + " · Tokens " + (activity.Tokens.HasValue ? activity.Tokens.Value.ToString("N0") : "数据不足"));
                    text.AppendLine("采样期间额度变化：5h " + Change(activity.FiveChange) + " / Weekly " + Change(activity.WeekChange));
                }
                if (activity.ReadError != null) text.AppendLine(activity.ReadError);
                text.AppendLine("只跟踪最近活动会话；额度变化可能包含其他任务 / 客户端。等待输入仅识别明确的输入工具调用；10 分钟无事件仅提示疑似卡住。").AppendLine();
            }
            {
                text.AppendLine("未来 24 小时额度时间轴");
                var events = new SortedDictionary<long, List<string>>();
                foreach (var window in snapshot.Windows)
                {
                    if (!window.Reset.HasValue || window.Expired(now)) continue;
                    long reset = window.Reset.Value;
                    if (reset <= now.AddHours(24).ToUnixSeconds())
                    {
                        if (!events.ContainsKey(reset)) events[reset] = new List<string>();
                        events[reset].Add(window.Label + " 预计重置（以接口重新读取为准）");
                    }
                    var advice = Advice(window);
                    if (advice.Fast && advice.Exhaustion.HasValue && advice.Exhaustion <= now.AddHours(24))
                    {
                        long time = advice.Exhaustion.Value.ToUnixSeconds();
                        if (!events.ContainsKey(time)) events[time] = new List<string>();
                        events[time].Add(window.Label + " 按近期速度预计耗尽");
                    }
                }
                foreach (var item in events) foreach (var line in item.Value) text.AppendLine(Unix(item.Key).ToLocalTime().ToString("MM-dd HH:mm") + "  " + line);
                if (events.Count == 0) text.AppendLine("未来 24 小时没有已知重置或耗尽点");
                text.AppendLine(Recommendation()).AppendLine();
            }
            text.AppendLine("历史采样（最近 24 小时）");
            foreach (var window in snapshot.Windows)
            {
                var points = history.Points.FindAll(p => p.Minutes == window.Minutes && p.Time >= now.AddHours(-24).ToUnixSeconds());
                text.AppendLine(window.Label + "：" + points.Count + " 个采样");
                int stride = Math.Max(1, (int)Math.Ceiling(points.Count / 12.0));
                for (int i = 0; i < points.Count; i += stride) text.AppendLine("  " + Unix(points[i].Time).ToLocalTime().ToString("MM-dd HH:mm") + "  剩余 " + (100 - points[i].Used).ToString("0.0") + "%");
            }
            if (history.SaveFailed) text.AppendLine("历史保存失败，当前仅使用内存采样。");
            text.AppendLine().AppendLine("预测基于近期观测速度，不保证后续消耗相同；不推算已恢复额度。");
            return text.ToString();
        }
        static DateTimeOffset Unix(long seconds) { return new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds); }
        static string Change(double? value) { return value.HasValue ? "-" + value.Value.ToString("0.0") + "%" : "未知"; }
        string Recommendation()
        {
            if (!Fresh || snapshot.Windows.Count == 0) return "推荐窗口：等待有效额度数据";
            foreach (var w in snapshot.Windows) if (w.Expired(DateTimeOffset.Now)) return "推荐窗口：等待重置确认后再判断";
            var limiting = snapshot.Windows.Find(w => w.Remaining <= 20 || Advice(w).Fast);
            if (limiting != null) return limiting.Reset.HasValue ? "高强度任务建议等到 " + Unix(limiting.Reset.Value).ToLocalTime().ToString("MM-dd HH:mm") + " 后重新检查所有额度" : "建议节省使用，重置时间未知";
            foreach (var w in snapshot.Windows) if (!Advice(w).DailyRate.HasValue) return "额度尚有余量；历史不足，暂不推荐高强度窗口";
            return "当前额度与近期速度允许继续使用；大型任务仍需留出余量";
        }
        void NotifyOnce(string key, string message)
        {
            if (capturePath != null || !alerted.Add(key)) return;
            tray.ShowBalloonTip(6000, "Codex 额度决策助手", message, ToolTipIcon.Info);
        }
        void CheckAlerts(QuotaSnapshot previous)
        {
            var now = DateTimeOffset.Now;
            foreach (var window in snapshot.Windows)
            {
                string key = window.Minutes + ":" + window.Reset;
                if (window.Expired(now)) continue;
                var old = previous == null ? null : previous.Windows.Find(w => w.Minutes == window.Minutes);
                if (preferences.ResetAlert && old != null && old.Reset.HasValue && window.Reset.HasValue && window.Reset > old.Reset && old.Reset <= now.ToUnixSeconds()) NotifyOnce("reset:" + key, window.Label + " 重置已确认，当前剩余 " + Percent(window) + "。" + Recommendation());
                if (preferences.BeforeResetAlert && window.Reset.HasValue && window.Reset.Value - now.ToUnixSeconds() <= 1800) NotifyOnce("before:" + key, window.Label + " 将在 30 分钟内重置");
                if (preferences.LowAlert && window.Remaining <= 20) NotifyOnce("low:" + key, window.Label + " 剩余 " + Percent(window));
                if (preferences.PaceAlert && window.Minutes == 10080 && Advice(window).Fast) NotifyOnce("pace:" + key, "周额度消耗偏快，按近期速度可能在重置前耗尽");
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        public void SetTheme(string id)
        {
            theme = MonitorTheme.Find(id); preferences.Theme = theme.Id;
            PanelStyle.SetTheme(theme);
            BackColor = background; ForeColor = foreground;
            menu.Renderer = new RoundedMenuRenderer(theme);
            ApplyMenuTheme(menu, menu.Renderer);
            if (appearance != null && !appearance.IsDisposed) appearance.ApplyTheme();
            if (decision != null && !decision.IsDisposed) decision.ApplyTheme();
            if (notificationPanel != null && !notificationPanel.IsDisposed) notificationPanel.ApplyTheme();
            if (historyPanel != null && !historyPanel.IsDisposed) historyPanel.ApplyTheme();
            Invalidate();
        }
        void ApplyMenuTheme(ToolStrip strip, ToolStripRenderer renderer)
        {
            strip.BackColor = theme.Card;
            strip.ForeColor = foreground;
            strip.Renderer = renderer;
            foreach (ToolStripItem item in strip.Items)
            {
                item.BackColor = theme.Card;
                item.ForeColor = foreground;
                var parent = item as ToolStripDropDownItem;
                if (parent != null && parent.HasDropDownItems)
                    ApplyMenuTheme(parent.DropDown, renderer);
            }
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
        sealed class RoundedMenuRenderer : ToolStripProfessionalRenderer
        {
            readonly MonitorTheme theme;
            public RoundedMenuRenderer(MonitorTheme value) : base(new ThemeMenuColors(value)) { theme = value; RoundedEdges = false; }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = PanelStyle.Round(new RectangleF(1, 1, e.Item.Width - 2, e.Item.Height - 2), 7))
                using (var brush = new SolidBrush(e.Item.Selected ? theme.Track : theme.Card)) e.Graphics.FillPath(brush, path);
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = theme.Text; base.OnRenderItemText(e);
            }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = PanelStyle.Round(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 12))
                using (var pen = new Pen(theme.Border)) e.Graphics.DrawPath(pen, path);
            }
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
                string identity = Json.String(account, "id") ?? Json.String(account, "email");
                // If the server omits account identity, isolate by authentication-file stamp.
                identity = identity ?? stamp;
                if (historyIdentity != identity)
                {
                    historyIdentity = identity; history.Open(Path.GetDirectoryName(settingsPath), identity); alerted.Clear();
                    activity.ResetQuotaBaseline();
                    snapshot = null;
                }
                var previous = snapshot;
                snapshot = QuotaSnapshot.Parse(result, Json.String(account, "planType"));
                if (capturePath == null) { history.Record(snapshot); activity.Observe(snapshot); CheckAlerts(previous); }
                status = "";
                tooltip.SetToolTip(this, "双击展开额度决策面板。\n" + Recommendation() + "\n拖动移动；右键设置。");
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
            AppLogo.Draw(g, new RectangleF(13, 8, 18, 18));
            DrawText(g, "Codex", 34, 7, 78, 20, 8.2f, foreground, true);
            if (snapshot != null) DrawText(g, snapshot.PlanLabel, 83, 8, Math.Max(25, width - 150), 19, 6.5f, muted, false);
            for (int i = 1; i < 2; i++)
            {
                float x = width - 57 + 23 * i;
                if (hovered == i) using (var brush = new SolidBrush(theme.Track)) using (var path = Rounded(new RectangleF(x, 6, 22, 22), 6)) g.FillPath(brush, path);
            }
            if (pointerInside) using (var pen = new Pen(muted, 0.9f)) { g.DrawLine(pen, width - 27, 13, width - 20, 20); g.DrawLine(pen, width - 20, 13, width - 27, 20); }
            // Keep each quota and its bar grouped when the user chooses a tall aspect ratio.
            bool showHealth = height >= 144;
            float firstRow = 34 + Math.Max(0, height - WindowLayout.DefaultHeight - (showHealth ? 24 : 0)) / 2;
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
            if (showHealth)
            {
                var advice = OverallHealth();
                DrawText(g, "额度健康：" + advice.Health, 16, height - 27, width - 32, 19, 7.4f, advice.Fast ? Color.FromArgb(210, 139, 74) : theme.Accent, true);
            }
            if (!pointerInside) DrawText(g, activity.State, width - 62, 8, 48, 19, 6.5f, muted, false, true);
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
        internal static Icon MakeIcon(int pixels = 32) { return AppLogo.CreateIcon(pixels); }
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
            if (decision != null) decision.Close();
            if (notificationPanel != null) notificationPanel.Close();
            if (historyPanel != null) historyPanel.Close();
            if (appearance != null) appearance.Close();
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { clock.Dispose(); watch.Dispose(); poll.Dispose(); tray.Dispose(); menu.Dispose(); tooltip.Dispose(); if (showSignal != null) showSignal.Dispose(); if (Icon != null) Icon.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
