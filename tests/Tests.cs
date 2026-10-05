using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using CodexMonitor;

class Tests
{
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wparam, IntPtr lparam);
    static IntPtr PackedPoint(System.Drawing.Point point) { return new IntPtr(unchecked((int)(((uint)(ushort)point.Y << 16) | (ushort)point.X))); }
    static int checks;
    static void Check(bool condition, string description) { if (!condition) throw new Exception(description); checks++; }
    static Dictionary<string, object> Parse(string json) { return Json.Serializer.Deserialize<Dictionary<string, object>>(json); }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "app-server") { MockServer(); return 0; }
        try
        {
            var quota = QuotaSnapshot.Parse(Parse(@"{""rateLimitsByLimitId"":{""other"":{""primary"":{""usedPercent"":1,""windowDurationMins"":300}},""codex"":{""primary"":{""usedPercent"":48,""windowDurationMins"":300,""resetsAt"":100},""secondary"":{""usedPercent"":58,""windowDurationMins"":10080}}},""rateLimits"":{""primary"":{""usedPercent"":99,""windowDurationMins"":300}}}"));
            Check(quota.FiveHour.Remaining == 52 && quota.Weekly.Remaining == 42, "Select the Codex bucket and subtract usage");
            Check(quota.Weekly.Reset == null, "Unknown reset remains unknown");
            quota = QuotaSnapshot.Parse(Parse(@"{""rateLimits"":{""secondary"":{""usedPercent"":120,""windowDurationMins"":300},""primary"":{""usedPercent"":-1,""windowDurationMins"":10080}}}"));
            Check(quota.FiveHour.Remaining == 0 && quota.Weekly.Remaining == 100, "Classify by duration, clamp percentages");
            quota = QuotaSnapshot.Parse(Parse(@"{""rateLimits"":{""primary"":{""usedPercent"":null,""windowDurationMins"":300},""secondary"":{""usedPercent"":5,""windowDurationMins"":15}}}"));
            Check(quota.FiveHour == null && quota.Weekly == null, "Do not fabricate absent or different quota windows");
            Check(quota.Windows.Count == 1 && quota.Windows[0].Label == "15 分钟", "Other quota periods remain visible with their actual duration");
            var pro = QuotaSnapshot.Parse(Parse(@"{""rateLimits"":{""planType"":""pro"",""primary"":null,""secondary"":{""usedPercent"":26,""windowDurationMins"":10080}}}"));
            Check(pro.Windows.Count == 1 && pro.Windows[0].Label == "每周" && pro.PlanLabel == "Pro" && !pro.TrayText.Contains("5 小时"), "Pro weekly-only data does not fabricate a five-hour card or tray label");
            var free = QuotaSnapshot.Parse(Parse(@"{""rateLimits"":{""primary"":{""usedPercent"":40,""windowDurationMins"":1440}}}"), "free");
            Check(free.Windows.Count == 1 && free.Windows[0].Label == "每日" && free.PlanLabel == "Free", "Free period and account plan fallback are respected");
            var plus = QuotaSnapshot.Parse(Parse(@"{""rateLimits"":{""planType"":""plus"",""primary"":{""usedPercent"":32,""windowDurationMins"":300},""secondary"":{""usedPercent"":18,""windowDurationMins"":10080}}}"));
            Check(plus.Windows.Count == 2 && plus.Windows[0].Label == "5 小时" && plus.Windows[1].Label == "每周", "Plus displays both returned windows in period order");
            var empty = QuotaSnapshot.Parse(Parse(@"{""rateLimits"":{""planType"":""free"",""primary"":null,""secondary"":null}}"));
            Check(empty.Windows.Count == 0 && empty.TrayText.Contains("暂无额度窗口") && !empty.TrayText.Contains("100%"), "Missing windows are unknown rather than unlimited or full quota");
            var unknown = QuotaSnapshot.Parse(Parse(@"{""rateLimits"":{""primary"":{""usedPercent"":20,""windowDurationMins"":null}}}"));
            Check(unknown.Windows.Count == 1 && unknown.Windows[0].Label == "当前额度", "Unknown period keeps known percentage without fabricating a period");
            var expiredOther = QuotaSnapshot.Parse(Parse(@"{""rateLimits"":{""primary"":{""usedPercent"":5,""windowDurationMins"":15,""resetsAt"":100}}}"));
            Check(expiredOther.HasExpiredWindow(DateTimeOffset.Now) && expiredOther.TrayText.Contains("—"), "Expiry and tray handling cover non-five-hour windows");
            bool rejected = false;
            try { QuotaSnapshot.Parse(Parse(@"{""rateLimitsByLimitId"":{""other"":{}},""rateLimits"":{}}")); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Do not mislabel another bucket as Codex");
            var now = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
            var window = new QuotaWindow { Used = 40, Reset = now.ToUnixSeconds() + 61 };
            Check(QuotaSnapshot.Countdown(window, now) == "2 分后重置", "Round countdown upwards");
            window.Reset = now.ToUnixSeconds() - 1;
            Check(window.Expired(now) && window.Remaining == 60 && QuotaSnapshot.Countdown(window, now) == "等待额度重置", "Reset expiry never fabricates refreshed quota");
            window.Reset = null;
            Check(QuotaSnapshot.Countdown(window, now) == "重置时间未知", "Missing timestamp handling");
            Check(!CodexDiscovery.IsDesktopPath(@"C:\Users\me\AppData\Local\OpenAI\Codex\bin\abc\codex.exe"), "Ignore monitor's own CLI server");
            Check(CodexDiscovery.IsDesktopPath(@"C:\Program Files\WindowsApps\OpenAI.Codex_1_x64\app\ChatGPT.exe"), "Recognize current Windows desktop name");
            var size = new System.Drawing.Size(312, 183);
            Check(Math.Abs(WindowLayout.ContentScale(size) - 1.5f) < 0.01, "Layout follows display scale");
            Check(WindowLayout.ResizeHit(new System.Drawing.Point(311, 182), size, 6) == 17, "Corner starts diagonal resizing");
            Check(WindowLayout.ResizeHit(new System.Drawing.Point(1, 80), size, 6) == 10, "Side changes width independently");
            Check(WindowLayout.ResizeHit(new System.Drawing.Point(100, 2), size, 6) == 12, "Top changes height independently");
            Check(WindowLayout.ResizeHit(new System.Drawing.Point(80, 80), size, 6) == 1, "Interior is not a resize border");
            var saved = new Preferences { Width = 300, Height = 170 };
            var restored = Json.Serializer.Deserialize<Preferences>(Json.Serializer.Serialize(saved));
            Check(restored.Width == 300 && restored.Height == 170, "Custom aspect ratio survives preferences round-trip");
            restored = Json.Serializer.Deserialize<Preferences>(@"{""X"":10,""Y"":10}");
            Check(restored.Width == 208 && restored.Height == 122, "Previous settings receive default dimensions");
            Check(restored.Theme == "mint" && MonitorTheme.Find("unknown").Id == "mint", "Old or unknown themes fall back safely");
            saved.Theme = "orbit";
            Check(Json.Serializer.Deserialize<Preferences>(Json.Serializer.Serialize(saved)).Theme == "orbit", "Selected theme survives preferences round-trip");
            using (var form = new MonitorForm(false, "unused-capture"))
            {
                var themeMenu = (System.Windows.Forms.ToolStripMenuItem)form.ContextMenuStrip.Items[2];
                foreach (var theme in MonitorTheme.All)
                {
                    form.SetTheme(theme.Id);
                    int selected = 0;
                    foreach (System.Windows.Forms.ToolStripMenuItem item in themeMenu.DropDownItems)
                        if (item.Checked) { selected++; Check((string)item.Tag == theme.Id, "Theme selection matches menu checkmark"); }
                    Check(selected == 1 && form.BackColor == theme.Background, "Exactly one theme selected and background applied");
                    foreach (var dimensions in new[] { new System.Drawing.Size(176, 108), new System.Drawing.Size(208, 122), new System.Drawing.Size(500, 250), new System.Drawing.Size(208, 300) })
                    {
                        form.Size = dimensions;
                        foreach (var example in new[] { plus, pro, free, empty, unknown, expiredOther })
                        {
                            typeof(MonitorForm).GetField("snapshot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(form, example);
                            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) form.DrawToBitmap(bitmap, form.ClientRectangle);
                        }
                    }
                    if (args.Length > 0 && args[0] == "--render-previews")
                    {
                        var examples = new[] { plus, pro, free, empty };
                        var names = new[] { "plus", "pro", "free", "empty" };
                        form.Size = new System.Drawing.Size(416, 244);
                        for (int i = 0; i < examples.Length; i++)
                        {
                            typeof(MonitorForm).GetField("snapshot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(form, examples[i]);
                            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                            {
                                form.DrawToBitmap(bitmap, form.ClientRectangle);
                                bitmap.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "preview-" + theme.Id + "-" + names[i] + ".png"));
                            }
                        }
                    }
                }
                form.Size = new System.Drawing.Size(500, 250);
                Check(form.Width == 500 && form.Height == 250, "Window supports independent width and height");
                var point = form.PointToScreen(new System.Drawing.Point(form.Width - 1, form.Height - 1));
                Check(SendMessage(form.Handle, 0x84, IntPtr.Zero, PackedPoint(point)).ToInt32() == 17, "Native corner hit-test exposes resizing to Windows");
                point = form.PointToScreen(new System.Drawing.Point(40, 60));
                Check(SendMessage(form.Handle, 0x84, IntPtr.Zero, PackedPoint(point)).ToInt32() == 1, "Native interior hit-test leaves content interactive");
                form.Size = new System.Drawing.Size(1, 1);
                Check(form.Width >= form.MinimumSize.Width && form.Height >= form.MinimumSize.Height, "Minimum size prevents unreadable layout");
            }
            // Form construction installs a UI context; these console RPC checks
            // run without a UI message loop.
            System.Threading.SynchronizationContext.SetSynchronizationContext(null);
            RpcTests().GetAwaiter().GetResult();
            Console.WriteLine("PASS: " + checks + " checks"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static async Task RpcTests()
    {
        using (var client = new RpcClient())
        {
            await client.ConnectAsync(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var a = client.RequestAsync("test/a", null);
            var b = client.RequestAsync("test/b", null);
            var results = await Task.WhenAll(a, b);
            Check(Json.String(results[0], "name") == "a" && Json.String(results[1], "name") == "b", "Correlate out-of-order RPC replies");
            bool failed = false;
            try { await client.RequestAsync("test/error", null); } catch (InvalidOperationException) { failed = true; }
            Check(failed, "RPC errors fail the request");
            failed = false;
            try { await client.RequestAsync("test/exit", null); } catch (System.IO.IOException) { failed = true; }
            Check(failed, "EOF releases pending requests");
        }
    }
    static void MockServer()
    {
        string line; object held = null; bool initialized = false;
        while ((line = Console.ReadLine()) != null)
        {
            var message = Parse(line); string method = Json.String(message, "method");
            object id; message.TryGetValue("id", out id);
            if (method == "initialize") Console.WriteLine(Json.Serializer.Serialize(new { id = id, result = new { } }));
            else if (method == "initialized") initialized = true;
            else if (!initialized) throw new Exception("Handshake required");
            else if (method == "test/a") held = id;
            else if (method == "test/b")
            {
                Console.WriteLine("non-json diagnostic");
                Console.WriteLine(Json.Serializer.Serialize(new { method = "account/rateLimits/updated", @params = new { } }));
                Console.WriteLine(Json.Serializer.Serialize(new { id = id, result = new { name = "b" } }));
                Console.WriteLine(Json.Serializer.Serialize(new { id = held, result = new { name = "a" } }));
            }
            else if (method == "test/error") Console.WriteLine(Json.Serializer.Serialize(new { id = id, error = new { code = -1, message = "mock error" } }));
            else if (method == "test/exit") return;
        }
    }
}
