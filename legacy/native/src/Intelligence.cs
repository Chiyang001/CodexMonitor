using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CodexMonitor
{
    public sealed class UsagePoint
    {
        public long Time;
        public int Minutes;
        public long? Reset;
        public double Used;
    }
    public sealed class QuotaAdvice
    {
        public string Health = "数据不足";
        public bool Fast;
        public double? DailyRate, TodayUsed, Budget, Safe;
        public DateTimeOffset? Exhaustion;
        public string Explanation = "等待有效额度与历史采样";
    }
    public sealed class UsageHistory
    {
        public List<UsagePoint> Points = new List<UsagePoint>();
        public bool SaveFailed;
        string path;
        public void Open(string folder, string identity)
        {
            // Only an opaque account identifier hash is used; never save account details.
            using (var hash = System.Security.Cryptography.SHA256.Create())
                path = Path.Combine(folder, "usage-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").Substring(0, 24) + ".json");
            try { Points = Json.Serializer.Deserialize<List<UsagePoint>>(File.ReadAllText(path)) ?? new List<UsagePoint>(); }
            catch { Points = new List<UsagePoint>(); }
            Points.RemoveAll(p => p == null || p.Used < 0 || p.Used > 100 || double.IsNaN(p.Used) || double.IsInfinity(p.Used));
        }
        public void Record(QuotaSnapshot snapshot)
        {
            long now = snapshot.Updated.ToUnixSeconds();
            Points.RemoveAll(p => p.Time < now - 8 * 86400 || p.Time > now);
            foreach (var window in snapshot.Windows)
            {
                if (window.Expired(snapshot.Updated)) continue;
                var last = Points.LastOrDefault(p => p.Minutes == window.Minutes);
                if (last != null && now - last.Time < 25) continue;
                Points.Add(new UsagePoint { Time = now, Minutes = window.Minutes, Reset = window.Reset, Used = 100 - window.Remaining });
            }
            if (path == null) return;
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path + ".tmp", Json.Serializer.Serialize(Points)); if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path); SaveFailed = false; }
            catch { SaveFailed = true; }
        }
        public QuotaAdvice Analyze(QuotaWindow window, DateTimeOffset now)
        {
            var advice = new QuotaAdvice();
            if (window == null || !window.Reset.HasValue || window.Minutes <= 0 || window.Expired(now)) return advice;
            if (window.Remaining <= 0) { advice.Health = "已耗尽"; advice.Explanation = "等待接口确认额度恢复"; }
            double days = (window.Reset.Value - now.ToUnixSeconds()) / 86400.0;
            var points = Points.Where(p => p.Minutes == window.Minutes && p.Reset == window.Reset && p.Time <= now.ToUnixSeconds()).OrderBy(p => p.Time).ToList();
            var recent = points.Where(p => p.Time >= now.ToUnixSeconds() - Math.Min(86400, window.Minutes * 60)).ToList();
            double span = recent.Count < 2 ? 0 : (recent.Last().Time - recent.First().Time) / 86400.0;
            double spent = Consumption(recent);
            // A decreasing used percentage inside the same cycle is a correction:
            // do not extrapolate it or attribute its subsequent rebound to usage.
            bool correction = recent.Zip(recent.Skip(1), (a, b) => b.Used < a.Used).Any(v => v);
            if (span >= 15.0 / 1440 && !correction)
            {
                advice.DailyRate = spent / span;
                advice.Fast = advice.DailyRate.Value * days > window.Remaining;
                advice.Health = window.Remaining <= 0 ? "已耗尽" : advice.Fast ? "消耗偏快" : "充足";
                if (advice.DailyRate.Value > 0) advice.Exhaustion = now.AddDays(Math.Min(3650, window.Remaining / advice.DailyRate.Value));
                advice.Explanation = advice.Fast ? "按近期速度，可能在重置前耗尽" : "按近期速度，重置时预计剩余 " + Math.Max(0, window.Remaining - advice.DailyRate.Value * days).ToString("0.0") + "%";
            }
            else if (window.Remaining > 0) advice.Explanation = correction ? "额度发生修正，等待新采样再预测" : "预测至少需要 15 分钟同周期采样";
            var today = points.Where(p => p.Time >= new DateTimeOffset(now.Date, now.Offset).ToUnixSeconds()).ToList();
            // Budget is anchored to the first sample today, avoiding subtracting today's usage twice.
            if (today.Count >= 2 && !today.Zip(today.Skip(1), (a, b) => b.Used < a.Used).Any(v => v))
            {
                var start = today.First();
                double startDays = (window.Reset.Value - start.Time) / 86400.0;
                advice.TodayUsed = Consumption(today);
                advice.Budget = Math.Min(100 - start.Used, (100 - start.Used) / Math.Max(1, startDays));
                advice.Safe = Math.Min(window.Remaining, Math.Max(0, advice.Budget.Value - advice.TodayUsed.Value));
            }
            return advice;
        }
        static double Consumption(List<UsagePoint> points)
        {
            double sum = 0;
            for (int i = 1; i < points.Count; i++) sum += Math.Max(0, points[i].Used - points[i - 1].Used);
            return sum;
        }
    }
    public sealed class TaskActivity
    {
        public string State = "空闲";
        public string Id;
        public string Model;
        public DateTimeOffset? Started, Finished;
        public long? Tokens;
        public double? FiveChange, WeekChange;
        public string ReadError;
        string file;
        long offset;
        string partial = "";
        long? tokenStart, tokenTotal;
        long? fiveReset, weekReset;
        double? fiveStart, weekStart;
        DateTimeOffset lastEvent;
        bool finalObserved;
        string waitingCall;
        public TaskActivity Fork() { return (TaskActivity)MemberwiseClone(); }
        public void ResetQuotaBaseline()
        {
            fiveStart = weekStart = FiveChange = WeekChange = null;
            fiveReset = weekReset = null; finalObserved = false;
        }
        public void Observe(QuotaSnapshot snapshot)
        {
            if (Started == null || finalObserved) return;
            if (Finished.HasValue && !fiveStart.HasValue && !weekStart.HasValue) { finalObserved = true; return; }
            if (fiveStart == null && snapshot.FiveHour != null) { fiveStart = snapshot.FiveHour.Remaining; fiveReset = snapshot.FiveHour.Reset; }
            if (weekStart == null && snapshot.Weekly != null) { weekStart = snapshot.Weekly.Remaining; weekReset = snapshot.Weekly.Reset; }
            FiveChange = snapshot.FiveHour != null && fiveStart.HasValue && fiveReset == snapshot.FiveHour.Reset && !snapshot.FiveHour.Expired(snapshot.Updated) && snapshot.FiveHour.Remaining <= fiveStart ? fiveStart - snapshot.FiveHour.Remaining : (double?)null;
            WeekChange = snapshot.Weekly != null && weekStart.HasValue && weekReset == snapshot.Weekly.Reset && !snapshot.Weekly.Expired(snapshot.Updated) && snapshot.Weekly.Remaining <= weekStart ? weekStart - snapshot.Weekly.Remaining : (double?)null;
            if (Finished.HasValue && snapshot.Updated >= Finished.Value) finalObserved = true;
        }
        public void Scan(bool running, DateTimeOffset now)
        {
            if (!running) { State = "离线"; return; }
            try
            {
                string root = Environment.GetEnvironmentVariable("CODEX_HOME");
                if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                string sessions = Path.Combine(root, "sessions");
                // Recent folders only; avoid rescanning an entire multi-year archive.
                var candidates = new List<FileInfo>();
                for (int day = 0; day < 3; day++)
                {
                    string folder = Path.Combine(sessions, now.LocalDateTime.AddDays(-day).ToString("yyyy\\MM\\dd"));
                    if (Directory.Exists(folder)) candidates.AddRange(new DirectoryInfo(folder).GetFiles("*.jsonl"));
                }
                var latest = candidates.OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                if (latest == null) { State = "状态未知"; ReadError = "未找到近期本地会话日志"; return; }
                if (file != latest.FullName || latest.Length < offset)
                {
                    file = latest.FullName; offset = 0; partial = ""; Id = null; Started = Finished = null; Tokens = null; tokenStart = tokenTotal = null; Model = null;
                    ResetQuotaBaseline(); waitingCall = null;
                }
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    stream.Position = offset;
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string data = partial + reader.ReadToEnd(); offset = stream.Position;
                        int end = data.LastIndexOf('\n');
                        partial = end < 0 ? data : data.Substring(end + 1);
                        if (end >= 0) foreach (string line in data.Substring(0, end).Split('\n')) ParseLine(line);
                    }
                }
                ReadError = null;
                State = Started == null || Finished.HasValue ? "空闲" : waitingCall != null ? "等待输入" : (now - lastEvent).TotalMinutes >= 10 ? "疑似卡住" : "工作中";
            }
            catch { State = "状态未知"; ReadError = "本地日志暂不可读取"; }
        }
        public void ParseLine(string line)
        {
            Dictionary<string, object> entry;
            try { entry = Json.Serializer.Deserialize<Dictionary<string, object>>(line); } catch { return; }
            var payload = Json.Object(entry, "payload");
            if (payload == null) return;
            DateTimeOffset time;
            if (!DateTimeOffset.TryParse(Json.String(entry, "timestamp"), out time)) return;
            string kind = Json.String(payload, "type");
            if (Json.String(entry, "type") == "turn_context") { Model = Json.String(payload, "model"); return; }
            if (Json.String(entry, "type") == "response_item")
            {
                string name = Json.String(payload, "name") ?? "";
                if (kind == "function_call" && (name == "request_user_input" || name.EndsWith(".request_user_input"))) waitingCall = Json.String(payload, "call_id");
                if (kind == "function_call_output" && Json.String(payload, "call_id") == waitingCall) waitingCall = null;
                return;
            }
            if (Json.String(entry, "type") != "event_msg") return;
            lastEvent = time;
            if (kind == "task_started")
            {
                Started = time; Finished = null; Id = Json.String(payload, "turn_id"); State = "工作中";
                tokenStart = tokenTotal; Tokens = null; ResetQuotaBaseline(); waitingCall = null;
            }
            else if (kind == "task_complete" || kind == "task_completed" || kind == "turn_aborted") { Finished = time; State = "空闲"; }
            else if (kind == "token_count")
            {
                var usage = Json.Object(Json.Object(payload, "info"), "total_token_usage");
                long count;
                if (long.TryParse(Json.String(usage, "total_tokens"), out count))
                {
                    tokenTotal = count;
                    if (tokenStart.HasValue && count >= tokenStart) Tokens = count - tokenStart.Value;
                }
            }
        }
    }
}
