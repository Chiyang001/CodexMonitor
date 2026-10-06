using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexMonitor
{
    public sealed class DecisionForm : RoundedPanelForm
    {
        readonly string kind;
        string text = "";
        int selected;
        public DecisionForm() : this("智能额度", null, null) { }
        public DecisionForm(string mode, Preferences preferences, Action save) : base(mode,
            mode == "历史统计" ? "回看消耗，安排下一次使用" : mode == "通知与提醒" ? "在合适的时刻，给你一个提醒" : "让每一份额度都用得心中有数")
        {
            kind = mode;
            if (mode == "通知与提醒")
            {
                AddTab("提醒规则", "01", delegate { });
                Heading("通知与提醒", "选择你关心的变化，设置即时保存。");
                if (preferences != null)
                {
                    AddSwitch("重置前 30 分钟", "提前安排下一段工作", preferences.BeforeResetAlert, v => { preferences.BeforeResetAlert = v; save(); });
                    AddSwitch("额度重置已确认", "接口确认额度已恢复后提醒", preferences.ResetAlert, v => { preferences.ResetAlert = v; save(); });
                    AddSwitch("额度低于 20%", "为接下来的任务留出余量", preferences.LowAlert, v => { preferences.LowAlert = v; save(); });
                    AddSwitch("周额度消耗偏快", "预计会在周重置前耗尽时提醒", preferences.PaceAlert, v => { preferences.PaceAlert = v; save(); });
                }
                Note("同周期同类提醒仅发送一次。Windows 通知设置可能影响显示。");
            }
            else
            {
                string[] tabs = mode == "历史统计" ? new[] { "额度时间轴", "历史采样" } : new[] { "额度概览", "今日预算", "当前任务" };
                for (int i = 0; i < tabs.Length; i++) { int index = i; AddTab(tabs[i], "0" + (i + 1), delegate { selected = index; RenderContent(); }); }
                RenderContent();
            }
        }
        public void UpdateContent(string value)
        {
            if (kind == "通知与提醒" || value == text) return;
            text = value; RenderContent();
        }
        static string Slice(string value, string start, string end)
        {
            int first = start == null ? 0 : value.IndexOf(start, StringComparison.Ordinal);
            if (first < 0) return "暂时没有可显示的数据\n等待有效采样，稍后会自动更新。";
            int last = end == null ? value.Length : value.IndexOf(end, Math.Min(value.Length, first + 1), StringComparison.Ordinal);
            return value.Substring(first, (last < 0 ? value.Length : last) - first).Trim();
        }
        void RenderContent()
        {
            int scroll = -Body.ScrollPosition.Y; ClearBody(); string value;
            if (kind == "历史统计")
            {
                Heading(selected == 0 ? "未来 24 小时" : "消耗记录", selected == 0 ? "已知重置点与预测耗尽点，按时间排列。" : "最近 24 小时的本地采样摘要。");
                value = selected == 0 ? Slice(text, "未来 24 小时额度时间轴", "历史采样") : Slice(text, "历史采样", "预测基于");
            }
            else
            {
                Heading(selected == 0 ? "额度概览" : selected == 1 ? "今日安全预算" : "当前任务", selected == 0 ? "剩余多少，以及按近期速度还能用多久。" : selected == 1 ? "把今天的消耗控制在合适的节奏。" : "来自最近活动的本地 Codex 会话。");
                value = selected == 0 ? Slice(text, null, "今日安全预算") : selected == 1 ? Slice(text, "今日安全预算", "Codex ·") : Slice(text, "Codex ·", "未来 24 小时");
                if (selected == 0) value = value.Replace("Codex 额度决策助手", "").Trim();
                if (selected == 2) value = Slice(value, null, "历史采样");
            }
            if (string.IsNullOrWhiteSpace(value)) value = "正在等待数据\n连接 Codex 后，这里会自动显示最新信息。";
            foreach (string part in value.Replace("\r\n", "\n").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] lines = part.Trim().Split(new[] { '\n' }, 2); AddCard(lines[0], lines.Length > 1 ? lines[1] : "");
            }
            Note("预测仅作参考；历史不足时不推算额度。任务额度变化可能包含其他客户端的消耗。");
            Body.ScrollPosition = new Point(0, scroll);
        }
    }
}
