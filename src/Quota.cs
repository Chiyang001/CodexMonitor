using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace CodexMonitor
{
    public sealed class QuotaWindow
    {
        public double Used;
        public long? Reset;
        public int Minutes;
        public string Label
        {
            get
            {
                if (Minutes == 10080) return "每周";
                if (Minutes == 1440) return "每日";
                if (Minutes <= 0) return "当前额度";
                if (Minutes % 1440 == 0) return Minutes / 1440 + " 天";
                if (Minutes % 60 == 0) return Minutes / 60 + " 小时";
                return Minutes + " 分钟";
            }
        }
        public double Remaining { get { return Math.Max(0, Math.Min(100, 100 - Used)); } }
        public bool Expired(DateTimeOffset now) { return Reset.HasValue && Reset.Value <= now.ToUnixSeconds(); }
    }
    public static class TimeExtensions
    {
        public static long ToUnixSeconds(this DateTimeOffset time) { return (long)(time.ToUniversalTime() - new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalSeconds; }
    }
    public sealed class QuotaSnapshot
    {
        public QuotaWindow FiveHour;
        public QuotaWindow Weekly;
        public string Plan;
        public DateTimeOffset Updated;
        public readonly List<QuotaWindow> Windows = new List<QuotaWindow>();
        public string PlanLabel
        {
            get
            {
                switch ((Plan ?? "").ToLowerInvariant())
                {
                    case "free": return "Free";
                    case "go": return "Go";
                    case "plus": return "Plus";
                    case "pro": return "Pro";
                    case "business": case "team": return "Business";
                    case "enterprise": return "Enterprise";
                    case "edu": return "Edu";
                    default: return "";
                }
            }
        }
        public bool HasExpiredWindow(DateTimeOffset now) { return Windows.Exists(window => window.Expired(now)); }
        public string TrayText
        {
            get
            {
                string text = "Codex" + (PlanLabel == "" ? "" : " · " + PlanLabel);
                foreach (var window in Windows) text += " · " + window.Label + " " + (window.Expired(DateTimeOffset.Now) ? "—" : window.Remaining.ToString("0") + "%");
                if (Windows.Count == 0) text += " · 暂无额度窗口";
                return text.Length > 63 ? text.Substring(0, 63) : text;
            }
        }
        public static QuotaSnapshot Parse(Dictionary<string, object> result, string accountPlan = null)
        {
            var map = Json.Object(result, "rateLimitsByLimitId");
            var bucket = map != null ? Json.Object(map, "codex") : Json.Object(result, "rateLimits");
            if (bucket == null) throw new InvalidOperationException("此账号没有返回 Codex 套餐额度");
            var snapshot = new QuotaSnapshot { Plan = Json.String(bucket, "planType") ?? accountPlan, Updated = DateTimeOffset.Now };
            foreach (string name in new[] { "primary", "secondary" })
            {
                var item = Json.Object(bucket, name);
                if (item == null) continue;
                object used, duration, reset;
                double percent;
                int minutes = 0;
                if (!item.TryGetValue("usedPercent", out used) || used == null ||
                    !double.TryParse(Convert.ToString(used, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out percent) || double.IsNaN(percent) || double.IsInfinity(percent)) continue;
                if (item.TryGetValue("windowDurationMins", out duration) && duration != null)
                    int.TryParse(Convert.ToString(duration, CultureInfo.InvariantCulture), out minutes);
                long timestamp;
                var window = new QuotaWindow { Used = percent, Minutes = minutes };
                if (item.TryGetValue("resetsAt", out reset) && reset != null && long.TryParse(Convert.ToString(reset), out timestamp) && timestamp > 0) window.Reset = timestamp;
                if (minutes == 300) snapshot.FiveHour = window;
                if (minutes == 10080) snapshot.Weekly = window;
                snapshot.Windows.Add(window);
            }
            snapshot.Windows.Sort((a, b) => a.Minutes.CompareTo(b.Minutes));
            return snapshot;
        }
        public static string Countdown(QuotaWindow window, DateTimeOffset now)
        {
            if (window == null) return "暂无额度数据";
            if (!window.Reset.HasValue) return "重置时间未知";
            long seconds = window.Reset.Value - now.ToUnixSeconds();
            if (seconds <= 0) return "等待额度重置";
            long minutes = (seconds + 59) / 60;
            if (minutes >= 1440) return string.Format("{0} 天 {1} 小时后重置", minutes / 1440, minutes % 1440 / 60);
            if (minutes >= 60) return string.Format("{0} 小时 {1} 分后重置", minutes / 60, minutes % 60);
            return string.Format("{0} 分后重置", minutes);
        }
    }
    public static class Json
    {
        public static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        public static Dictionary<string, object> Object(Dictionary<string, object> value, string key)
        { object item; return value != null && value.TryGetValue(key, out item) ? item as Dictionary<string, object> : null; }
        public static string String(Dictionary<string, object> value, string key)
        { object item; return value != null && value.TryGetValue(key, out item) && item != null ? Convert.ToString(item) : null; }
    }
}
