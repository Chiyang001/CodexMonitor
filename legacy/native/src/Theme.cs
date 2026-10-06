using System.Drawing;

namespace CodexMonitor
{
    public sealed class MonitorTheme
    {
        public readonly string Id, Name;
        public readonly int Layout;
        public readonly Color Background, Top, Text, Muted, Accent, Card, Border, Track;
        public MonitorTheme(string id, string name, int layout, uint background, uint top, uint text, uint muted, uint accent, uint card, uint border, uint track)
        {
            Id = id; Name = name; Layout = layout;
            Background = Color.FromArgb(unchecked((int)background)); Top = Color.FromArgb(unchecked((int)top));
            Text = Color.FromArgb(unchecked((int)text)); Muted = Color.FromArgb(unchecked((int)muted)); Accent = Color.FromArgb(unchecked((int)accent));
            Card = Color.FromArgb(unchecked((int)card)); Border = Color.FromArgb(unchecked((int)border)); Track = Color.FromArgb(unchecked((int)track));
        }
        public static readonly MonitorTheme[] All = {
            new MonitorTheme("mint", "深海薄荷 · 分区卡片", 0, 0xFF11171A, 0xFF1E282C, 0xFFEFF7F6, 0xFF859699, 0xFF8BE1CC, 0xFF232E32, 0xFF405256, 0xFF39494D),
            new MonitorTheme("paper", "晴空纸白 · 极简线条", 1, 0xFFF5F7FC, 0xFFFFFFFF, 0xFF263249, 0xFF738096, 0xFF507CDD, 0xFFFFFFFF, 0xFFDCE3F0, 0xFFE2E8F3),
            new MonitorTheme("orbit", "星夜紫 · 双环仪表", 2, 0xFF191526, 0xFF292139, 0xFFF4EEFF, 0xFFA498BC, 0xFFC3A1FF, 0xFF30273F, 0xFF514264, 0xFF443653),
            new MonitorTheme("amber", "暖砂金 · 双栏面板", 3, 0xFF29221C, 0xFF362D23, 0xFFFFF3DE, 0xFFB5A38A, 0xFFE8BC75, 0xFF3C3227, 0xFF65513B, 0xFF5B4934)
        };
        public static MonitorTheme Find(string id)
        {
            foreach (var theme in All) if (theme.Id == id) return theme;
            return All[0];
        }
    }
}

