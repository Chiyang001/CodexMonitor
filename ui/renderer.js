const api = window.monitor;
const view = new URLSearchParams(location.search).get('view') || 'widget';
document.body.dataset.view = view;
const root = document.getElementById('app');
const paths = {
  close: '<path d="m6 6 12 12M18 6 6 18"/>',
  refresh: '<path d="M20 11a8 8 0 1 0-2 6M20 4v7h-7"/>',
  settings: '<circle cx="12" cy="12" r="3"/><path d="m9 3-.8 2.4-2.4.9L3.4 6l-2 3.5L3 11.4v2.8l-1.6 1.9 2 3.5 2.4-.3 2.4.9L9 22h6l.8-2.8 2.4-.9 2.4.3 2-3.5-1.6-1.9v-2.8l1.6-1.9-2-3.5-2.4.3-2.4-.9L15 3Z"/>',
  palette: '<circle cx="12" cy="12" r="9"/><path d="M12 3a9 9 0 0 1 9 9c0 2-1 3-3 3h-3a2 2 0 0 0-2 2v1a3 3 0 0 1-3 3"/><path d="M7 10h.01M10 7h.01M15 7h.01M17 11h.01"/>',
  size: '<rect x="3" y="5" width="18" height="14" rx="3"/><path d="m8 9-2 2 2 2m8-4 2 2-2 2"/>',
  overview: '<rect x="3" y="3" width="7" height="7" rx="2"/><rect x="14" y="3" width="7" height="7" rx="2"/><rect x="3" y="14" width="7" height="7" rx="2"/><rect x="14" y="14" width="7" height="7" rx="2"/>',
  budget: '<circle cx="12" cy="12" r="9"/><path d="M12 7v10m-3-8h4a2 2 0 0 1 0 4h-2a2 2 0 0 0 0 4h4"/>',
  task: '<path d="m5 7 5 5-5 5m8 0h6"/>',
  bell: '<path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4"/>',
  timeline: '<path d="M6 4v16m4-14h10m-10 6h7m-7 6h10"/><circle cx="6" cy="6" r="1.5"/><circle cx="6" cy="12" r="1.5"/><circle cx="6" cy="18" r="1.5"/>',
  chart: '<path d="M4 4v16h16M7 15l4-5 4 3 5-7"/>',
  check: '<path d="m5 12 4 4 10-10"/>',
  shield: '<path d="m12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6Z"/><path d="m8 12 3 3 5-6"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v6m0-10h.01"/>',
  arrow: '<path d="M5 12h14m-5-5 5 5-5 5"/>',
  spark: '<path d="m12 3 2.3 6.7L21 12l-6.7 2.3L12 21l-2.3-6.7L3 12l6.7-2.3Z"/>',
  clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  show: '<rect x="3" y="4" width="18" height="15" rx="3"/><path d="M8 22h8m-4-3v3"/>',
  quit: '<path d="M12 3v9m-5-7a9 9 0 1 0 10 0"/>'
};
const icon = name => `<svg viewBox="0 0 24 24" aria-hidden="true">${paths[name] || paths.info}</svg>`;
const esc = value => String(value ?? '').replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
const logo = '<img src="../assets/Logo-rounded.png" alt="Codex Monitor">';
const fmt = value => value == null || !Number.isFinite(value) ? '—' : value.toFixed(1);
const dateTime = time => new Date(time).toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false });
function countdown(reset) {
  if (!reset) return '重置时间未知'; const minutes = Math.ceil((reset - Date.now()) / 60000);
  if (minutes <= 0) return '等待额度重置';
  if (minutes >= 1440) return `${Math.floor(minutes / 1440)}天 ${Math.floor(minutes % 1440 / 60)}小时后重置`;
  if (minutes >= 60) return `${Math.floor(minutes / 60)}小时 ${minutes % 60}分后重置`;
  return `${minutes}分后重置`;
}
const tabs = { settings: [['theme', 'palette', '界面主题'], ['effects', 'spark', '界面效果'], ['dashboard', 'overview', '仪表盘样式'], ['size', 'size', '窗口大小'], ['display', 'show', '显示位置'], ['about', 'info', '关于']], intelligence: [['overview', 'overview', '额度概览'], ['budget', 'budget', '今日预算'], ['task', 'task', '当前任务']], notifications: [['reminders', 'bell', '提醒规则']], history: [['timeline', 'timeline', '额度时间轴'], ['samples', 'chart', '历史采样']] };
const titles = { settings: ['设置', '把悬浮窗调整成你喜欢的样子'], intelligence: ['智能额度', '知道还剩多少，也知道够不够用'], notifications: ['通知与提醒', '在合适的时刻，给你一个提醒'], history: ['历史统计', '回看消耗，安排下一次使用'] };
let state, activeTab = tabs[view]?.[0][0], mounted = false, lastPrefs = '', lastWidget = '';
const head = (eyebrow, title, detail) => `<header class="page-head"><div class="eyebrow">${eyebrow}</div><h1>${title}</h1><p>${detail}</p></header>`;
const note = text => `<div class="note">${icon('info')}<span>${text}</span></div>`;
const empty = (title, detail) => `<div class="card empty">${icon('clock')}<h3>${title}</h3><p>${detail}</p></div>`;
function mount() {
  if (view === 'widget') { root.innerHTML = '<main class="surface widget" id="widget"></main>'; return; }
  if (view === 'menu') {
    root.innerHTML = `<main class="surface menu"><div class="menu-brand">${logo}Codex Monitor</div>${[['show', 'show', '显示悬浮窗'], ['refresh', 'refresh', '立即刷新'], ['settings', 'settings', '设置'], [null], ['intelligence', 'spark', '智能额度'], ['notifications', 'bell', '通知与提醒'], ['history', 'chart', '历史统计'], [null], ['quit', 'quit', '退出']].map(([action, glyph, caption]) => action ? `<button data-action="${action}" class="${action === 'quit' ? 'danger' : ''}">${icon(glyph)}${caption}</button>` : '<div class="separator"></div>').join('')}</main>`; return;
  }
  const [title, subtitle] = titles[view];
  root.innerHTML = `<main class="surface shell"><aside class="sidebar"><div class="brand">${logo}<div><strong>Codex</strong><small>额度决策助手</small></div></div><div><div class="nav-label">${view === 'settings' ? '个性化' : view === 'notifications' ? '提醒偏好' : '工作空间'}</div><nav class="nav">${tabs[view].map(([id, glyph, caption]) => `<button class="tab ${id === activeTab ? 'active' : ''}" data-tab="${id}">${icon(glyph)}${caption}</button>`).join('')}</nav></div></aside><section class="main"><header class="window-header"><div><h2>${title}</h2><p>${subtitle}</p></div><div class="header-actions">${view !== 'settings' && view !== 'notifications' ? `<button class="icon-button" data-action="refresh" title="立即刷新" aria-label="立即刷新">${icon('refresh')}</button>` : ''}<button class="icon-button close" data-action="close" title="关闭" aria-label="关闭">${icon('close')}</button></div></header><div class="content" id="content"></div></section></main>`;
}
function render(next, force = false) {
  state = next; document.documentElement.dataset.theme = state.preferences.theme;
  document.documentElement.dataset.translucent = String(state.preferences.translucent === true);
  if (!mounted) { mount(); mounted = true; force = true; }
  if (view === 'widget') return renderWidget();
  if (view === 'menu') return;
  const prefs = JSON.stringify(state.preferences);
  if (view === 'settings' && !force && !state.taskbarError && (prefs === lastPrefs || document.activeElement?.type === 'range')) return;
  lastPrefs = prefs;
  const content = document.getElementById('content'), scroll = content.scrollTop;
  const banner = view !== 'settings' && view !== 'notifications' && (!state.decision.fresh || state.status) ? `<div class="status-banner">${esc(state.status || '额度数据已过期，预测暂时不可用')}</div>` : '';
  content.innerHTML = `<div class="page">${banner}${view === 'settings' ? renderSettings() : view === 'notifications' ? renderReminders() : view === 'history' ? renderHistory() : renderIntelligence()}</div>`;
  if (!force) { content.querySelector('.page').style.animation = 'none'; content.scrollTop = scroll; }
  document.querySelectorAll('[data-tab]').forEach(button => button.classList.toggle('active', button.dataset.tab === activeTab));
  document.querySelector('[data-action="refresh"]')?.classList.toggle('spin', state.busy);
  if (state.saveError) content.insertAdjacentHTML('beforeend', note('设置或历史保存失败，当前显示使用内存中的数据。'));
}
function renderSettings() {
  const prefs = state.preferences;
  if (activeTab === 'about') {
    return head('ABOUT', '关于 Codex Monitor', '版本 1.1.0 · 额度决策助手，让额度信息更清晰。') + `<article class="card about-developer"><img class="developer-logo" src="../assets/developer.png" alt="开发者炽阳001的 Logo"><div><div class="eyebrow">开发者</div><h2>炽阳001</h2><p>Codex Monitor 开发者</p></div></article><div class="stack about-links"><button class="card setting-row" data-action="developer-bilibili"><div><h3>B 站主页</h3><small>space.bilibili.com/404891612</small></div>${icon('arrow')}</button><button class="card setting-row" data-action="developer-github"><div><h3>GitHub 主页</h3><small>github.com/Chiyang001</small></div>${icon('arrow')}</button><button class="card setting-row" data-action="check-update"><div><h3>检查更新</h3><small>打开 GitHub Releases 页面，查看最新版本和更新说明。</small></div>${icon('arrow')}</button></div>` + note('点击链接会在默认浏览器中打开。');
  }
  if (activeTab === 'display') {
    return head('WINDOW DISPLAY', '在任务栏查看额度', '把 5 小时与每周剩余额度直接显示为任务栏文字。') + `<div class="stack"><button class="card setting-row" data-toggle="showInTaskbar" role="switch" aria-checked="${prefs.showInTaskbar}"><div><h3>在任务栏显示额度文字</h3><small>显示 5h 和每周剩余百分比，右键打开菜单，双击打开设置。</small></div><span class="switch" aria-hidden="true"></span></button><div class="card"><h3>任务栏文字位置</h3><p>距离任务栏左边缘的距离，调整以避开 Traffic Monitor、天气和应用图标。</p><label class="taskbar-offset">左侧留白 <input id="taskbar-offset" type="number" min="0" max="2000" step="10" value="${prefs.taskbarOffset}" aria-label="任务栏左侧留白"> 像素</label></div><div class="card"><h3>任务栏文字布局</h3><p>选择在一行中并排显示，或分为上下两行。</p><div class="taskbar-layout">${[['one','一行显示'],['two','两行显示']].map(([id,label])=>`<button class="size-choice ${prefs.taskbarLayout===id ? 'selected' : ''}" data-taskbar-layout="${id}" aria-pressed="${prefs.taskbarLayout===id}">${label}</button>`).join('')}</div></div><div class="card"><h3>任务栏文字大小</h3><p>文字和显示区域随字号一起调整。</p><label class="taskbar-offset">字号 <input id="taskbar-font-size" type="number" min="10" max="24" step="1" value="${prefs.taskbarFontSize}" aria-label="任务栏文字大小"> 像素</label></div><button class="card setting-row" data-toggle="taskbarDark" role="switch" aria-checked="${prefs.taskbarDark}"><div><h3>深色任务栏</h3><small>深色任务栏使用浅色文字，关闭后使用深色文字。</small></div><span class="switch" aria-hidden="true"></span></button></div>` + (state.taskbarError ? note(esc(state.taskbarError)) : '') + note('更改即时生效并自动保存。支持主屏幕水平任务栏，任务栏文字独立于悬浮窗。');
  }
  if (activeTab === 'effects') {
    return head('APPEARANCE EFFECTS', '选择窗口的质感', '统一控制悬浮窗、右键菜单和所有面板的背景效果。') + `<button class="card setting-row" data-toggle="translucent" role="switch" aria-checked="${prefs.translucent}"><div><h3>半透明效果</h3><small>开启时使用磨砂毛玻璃，关闭后窗口背景完全不透明。</small></div><span class="switch" aria-hidden="true"></span></button>` + note('默认关闭。更改即时生效并自动保存。');
  }
  if (activeTab === 'theme') {
    const choices = [ ['mint', '深海薄荷', '清爽、安静的深色卡片', '#15262d', '#edf6f5', '#2b414a', '#8ddfcb'], ['paper', '晴空纸白', '明亮留白，轻盈清晰', '#f7f9fd', '#26324a', '#e4e9f2', '#597ce5'], ['orbit', '星夜紫', '柔和紫色，专注工作', '#231e33', '#f4efff', '#443853', '#bfa4f3'], ['amber', '暖砂金', '温暖沉稳，柔和护眼', '#342c24', '#fff3df', '#594a3b', '#e5bf7d'], ["ocean","极夜冰蓝","冷静深蓝，清透冰色","#17263b","#edf5ff","#354c68","#89caff"], ["rose","暮色玫瑰","柔和深玫瑰，温暖细腻","#352530","#fff0f6","#644654","#f0a6c5"], ["graphite","石墨银灰","低调中性，银灰点缀","#262a30","#f2f4f7","#4d5662","#c5d2e3"], ["sage","晨雾鼠尾草","浅绿底色，自然舒缓","#f7fbf7","#243c2d","#cbdcd0","#35724e"], ["peach","奶油蜜桃","暖白与桃色，柔和明亮","#fffaf5","#4c352b","#e5cfc0","#ac593d"], ["lavender","轻柔薰衣草","浅紫留白，轻盈淡雅","#fcfaff","#3e3157","#d8cde8","#7f5cb3"] ];
    return head('APPEARANCE', '找到适合你的风格', '悬浮窗和所有面板会一起切换。') + `<div class="card-grid">${choices.map(([id, name, detail, bg, text, line, accent]) => `<button class="theme-card ${prefs.theme === id ? 'selected' : ''}" data-theme-choice="${id}" aria-pressed="${prefs.theme === id}"><div class="theme-preview" style="--preview-bg:${bg};--preview-text:${text};--preview-line:${line};--preview-accent:${accent}"><div class="preview-top"><span>Codex</span><span>Plus</span></div><div class="preview-row"><span>5 小时</span><b>68%</b><div class="preview-bar"></div></div><div class="preview-row"><span>每周</span><b>86%</b><div class="preview-bar"></div></div></div><div class="theme-caption"><div><strong>${name}</strong><small>${detail}</small></div><span class="check-circle">${icon('check')}</span></div></button>`).join('')}</div>` + note('更改即时生效并自动保存。预览中的数字仅用于展示主题。');
  }
  if (activeTab === 'dashboard') {
    return head('DASHBOARD STYLE', '选择额度的展示方式', '独立于界面主题，同时应用到悬浮窗和额度概览。') + '<div class="card-grid">' + [['bars','进度条','横向进度，快速比较剩余额度'],['rings','环形百分比','环形刻度，突出剩余百分比'],['gauge','半圆仪表','弧形刻度，直观查看剩余额度'],['segments','分段刻度','十格刻度，清楚感知额度区间'],['numbers','数字卡片','突出百分比，简洁清晰']].map(([id,title,detail]) => `<button class="theme-card ${prefs.dashboardStyle === id ? 'selected' : ''}" data-dashboard-choice="${id}" aria-pressed="${prefs.dashboardStyle === id}"><div class="dashboard-preview ${id}">${[ ['5 小时',68],['每周',86] ].map(([label,value]) => `<div class="dashboard-sample"><span>${label}</span>${quotaVisual(value,false,id)}</div>`).join('')}</div><div class="theme-caption"><div><strong>${title}</strong><small>${detail}</small></div><span class="check-circle">${icon('check')}</span></div></button>`).join('') + '</div>' + note('显示剩余额度，重置时间和额度数据保持同步。更改即时生效并自动保存。');
  }
  return head('WINDOW SIZE', '适合你的屏幕与节奏', '选择预设比例，也可以拖动悬浮窗右下角调整大小。') + `<div class="size-grid">${[85,100,125,150,200].map(size => { const width = Math.max(248, Math.round(290 * size / 100)), height = Math.max(188, Math.round(218 * size / 100)); const chosen = prefs.width === width && prefs.height === height; return `<button class="size-choice ${chosen ? 'selected' : ''}" data-size="${size}" aria-pressed="${chosen}"><strong>${size}%</strong><small>${width} × ${height}</small><small>${size === 100 ? '默认尺寸' : size < 100 ? '轻巧紧凑' : '更多呼吸空间'}</small></button>`; }).join('')}</div>` + note(`当前尺寸 ${Math.round(prefs.width)} × ${Math.round(prefs.height)} 逻辑像素。系统缩放会自动适配。`);
}
function renderReminders() {
  const items = [['beforeResetAlert','重置前 30 分钟','提前安排下一段工作，减少等待。'],['resetAlert','额度重置已确认','官方接口确认额度恢复后通知你。'],['lowAlert','额度低于 20%','为接下来的任务留出一点余量。'],['paceAlert','周额度消耗偏快','预计在周重置前耗尽时给出提醒。']];
  return head('NOTIFICATIONS', '只在你关心的时候提醒', '选择需要的通知，让额度变化不打断你的专注。') + `<div class="stack">${items.map(([key,title,detail]) => `<button class="card setting-row" data-toggle="${key}" role="switch" aria-checked="${state.preferences[key]}"><div><h3>${title}</h3><small>${detail}</small></div><span class="switch" aria-hidden="true"></span></button>`).join('')}</div>` + note('本次运行期间，同周期同类提醒只发送一次。Windows 的通知设置可能影响显示。');
}
function quotaVisual(remaining, expired, style = state.preferences.dashboardStyle) {
  const value = expired || !Number.isFinite(remaining) ? 0 : Math.max(0, Math.min(100, remaining));
  const percent = expired || !Number.isFinite(remaining) ? '—' : Math.round(value);
  const radial = ['rings','gauge'].includes(style);
  const shape = style === 'gauge' ? '<path class="ring-track" d="M8 72 A42 42 0 0 1 92 72"/><path class="ring-fill" d="M8 72 A42 42 0 0 1 92 72" pathLength="100" stroke-dasharray="' + value + ' 100"/>' : '<circle class="ring-track" cx="50" cy="50" r="42"/><circle class="ring-fill" cx="50" cy="50" r="42" pathLength="100" stroke-dasharray="' + value + ' 100"/>';
  return `<div data-visual-style="${style}" class="quota-visual ${radial ? 'ring-visual' : 'bar-visual'} ${style}-visual" role="img" aria-label="${percent === '—' ? '额度未知' : '剩余 ' + percent + '%'}"><svg class="quota-ring" viewBox="0 0 100 100" aria-hidden="true">${shape}</svg><div class="metric percent">${percent}<small>%</small></div><div class="progress"><span style="--value:${value}%"></span></div>${style === 'segments' ? `<div class="segment-track">${Array.from({length:10},(_,i)=>`<i style="--fill:${Math.max(0,Math.min(1,value/10-i))*100}%"></i>`).join('')}</div>` : ''}</div>`;
}
function renderIntelligence() {
  const insights = state.decision;
  if (activeTab === 'overview') return head('QUOTA INSIGHTS', '额度，现在是什么状态', '结合重置时间与近期消耗，给你一个更有用的答案。') + (insights.windows.length ? `<div class="card-grid">${insights.windows.map(w => `<article class="card quota-card"><div class="topline"><h3>${esc(w.label)}</h3><span class="chip ${w.advice.fast ? 'error' : ''}">${esc(w.advice.health)}</span></div>${quotaVisual(w.remaining,w.expired)}<div class="countdown" data-reset="${w.reset || ''}">${countdown(w.reset)}</div><p class="detail">${esc(w.advice.explanation)}</p><p class="detail">${w.advice.rate == null ? '耗尽预测：数据不足' : w.advice.exhaustion ? `预计耗尽 ${dateTime(w.advice.exhaustion)}${w.advice.fast ? ' · 早于重置' : ' · 本周期重置更早'}` : '近期未观测到消耗'}</p></article>`).join('')}</div>` : empty('等待额度数据', esc(state.status || '连接 Codex 后会自动更新。'))) + `<div class="card recommendation">${icon('spark')}<div><h3>使用建议</h3><p>${esc(insights.recommendation)}</p></div></div>` + note('预测基于近期观测速率，后续任务可能消耗不同。数据过期或发生额度修正时暂停预测。');
  if (activeTab === 'budget') {
    const b = insights.budget;
    return head('DAILY BUDGET', '今天还能放心用多少', '以今日首次采样为起点，把额度分配到剩余周期。') + (b?.safe != null ? `<div class="card budget-summary"><div><h3>今天可使用约</h3><div class="metric">${fmt(b.safe)}<small>%</small></div><p>${esc(b.label)}剩余 ${fmt(b.remaining)}%</p></div><span class="chip dot">均衡预算</span></div><div class="card budget-details"><div><small>今日建议预算</small><strong>${fmt(b.budget)}%</strong></div><div><small>今日已观测消耗</small><strong>${fmt(b.today)}%</strong></div></div><div class="modes">${[['节省',b.safe*.6,'留出更多余量'],['均衡',b.safe,'跟随当前预算'],['激进',Math.min(b.remaining,b.safe*1.5),'可能提前用尽']].map(([name,value,detail]) => `<div class="card mode"><h3>${name}</h3><strong>${fmt(value)}%</strong><small>${detail}</small></div>`).join('')}</div>` : empty('正在积累今日采样', '至少需要今天同一周期的两次有效采样。<br>没有日 / 周额度时，不推算每日预算。')) + note('未观测时段的消耗未知，短周期额度仍可能先耗尽。建议预算不是任务完成保证。');
  }
  const task = state.activity, minutes = task.started ? Math.max(0, Math.floor(((task.finished || Date.now()) - task.started) / 60000)) : null;
  return head('TASK ACTIVITY', '看见当前任务的影响', '只读最近活动的本地会话，不保存你的对话内容。') + `<div class="card"><div class="task-head"><h3>Codex 状态</h3><span class="chip dot">${esc(task.state)}</span></div><div class="task-grid"><div><small>运行时长</small><strong>${minutes == null ? '—' : minutes + ' 分钟'}</strong></div><div><small>Token 增量</small><strong>${task.tokens == null ? '—' : Intl.NumberFormat('zh-CN', { notation:'compact', maximumFractionDigits:1 }).format(task.tokens)}</strong></div><div><small>5h 额度变化</small><strong>${task.changes[300] == null ? '—' : '-' + fmt(task.changes[300]) + '%'}</strong></div><div><small>Weekly 额度变化</small><strong>${task.changes[10080] == null ? '—' : '-' + fmt(task.changes[10080]) + '%'}</strong></div></div><div class="task-id">模型 ${esc(task.model || '未知')} ${task.id ? ' · ' + esc(task.id) : ''}</div></div>` + (task.error ? note(esc(task.error)) : '') + note('仅跟踪最近活动会话，额度变化可能包含其他任务或客户端。10 分钟无事件仅表示疑似卡住。');
}
function renderHistory() {
  if (activeTab === 'timeline') {
    const timeline = state.decision.timeline;
    return head('NEXT 24 HOURS', '把下一段工作，安排得刚刚好', '已知重置点与预测耗尽点，按时间展开。') + (timeline.length ? `<div class="card timeline">${timeline.map(event => `<div class="timeline-item"><div class="timeline-time">${new Date(event.time).toLocaleDateString('zh-CN',{month:'2-digit',day:'2-digit'})}<strong>${new Date(event.time).toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit',hour12:false})}</strong></div><div class="timeline-dot"></div><div><h3 class="${event.type === 'risk' ? 'error' : ''}">${esc(event.label)}</h3><p>${esc(event.detail)}</p></div></div>`).join('')}</div>` : empty('未来 24 小时暂无已知事件', '有有效重置时间或耗尽预测后，时间轴会自动更新。')) + `<div class="card recommendation">${icon('spark')}<div><h3>下一步建议</h3><p>${esc(state.decision.recommendation)}</p></div></div>` + note('不会将倒计时结束当成额度已经恢复，也不推算后续滚动窗口的重置时刻。');
  }
  const groups = state.decision.windows;
  return head('USAGE HISTORY', '消耗趋势，一眼看懂', '最近 24 小时的本地额度采样。周期切换处会断开曲线。') + `<div class="stack">${groups.map(w => {
    const points = state.history.filter(p => p.minutes === w.minutes).sort((a,b) => a.time-b.time);
    if (points.length < 2) return empty(`${esc(w.label)} · 等待采样`, `${points.length} 个采样，积累两次采样后显示曲线。`);
    const min = points[0].time, span = Math.max(1, points.at(-1).time-min); let segments = [], current = [];
    points.forEach((p,i) => { if (i && p.reset !== points[i-1].reset) { segments.push(current); current=[]; } current.push([8+(p.time-min)/span*484,148-(100-p.used)/100*136]); }); segments.push(current);
    return `<article class="card chart-card"><div class="chart-head"><h3>${esc(w.label)}</h3><span class="chip">${points.length} 个采样</span></div><svg class="chart" viewBox="0 0 500 156" preserveAspectRatio="none" aria-label="${esc(w.label)}剩余百分比历史曲线" role="img"><path class="grid" d="M8 12H492M8 80H492M8 148H492"/>${segments.map(segment => `<path class="area" d="M${segment[0][0]},148 ${segment.map(([x,y])=>`L${x},${y}`).join(' ')} L${segment.at(-1)[0]},148Z"/><path class="line" d="${segment.map(([x,y],i)=>`${i?'L':'M'}${x},${y}`).join(' ')}"/>`).join('')}</svg><div class="chart-labels"><span>${dateTime(min)}</span><span>剩余 0%–100%</span><span>${dateTime(points.at(-1).time)}</span></div></article>`;
  }).join('')}</div>` + (!groups.length ? empty('等待历史数据','连接 Codex 后会自动开始采样。') : '') + note('在本机按账号隔离保存最多八天数值采样，不保存账号明文或对话内容。');
}
function renderWidget() {
  const widget = document.getElementById('widget'), windows = state.decision.windows;
  const signature = state.preferences.dashboardStyle + ':' + windows.map(w => w.minutes).join(',');
  if (signature !== lastWidget || !widget.children.length) {
    lastWidget = signature;
    widget.innerHTML = `<header class="widget-header">${logo}<strong>Codex</strong><small id="widget-plan"></small><div class="widget-trailing"><span class="activity-label" id="widget-activity"></span><span class="status-dot"></span><button class="icon-button close widget-close" data-action="close" aria-label="隐藏悬浮窗">${icon('close')}</button></div></header><section class="widget-quotas ${['rings','gauge','numbers'].includes(state.preferences.dashboardStyle) ? 'ring-layout' : ''}">${windows.length ? windows.map(w => `<div class="widget-quota" data-window="${w.minutes}"><div class="row"><span>${esc(w.label)}</span></div>${quotaVisual(w.remaining,w.expired)}<div class="countdown" data-reset="${w.reset || ''}"></div></div>`).join('') : `<div class="empty">${icon('clock')}<h3>${state.snapshot ? '暂无额度窗口' : '正在等待 Codex'}</h3><p id="widget-status"></p></div>`}</section><footer class="widget-footer"><button class="widget-health" data-action="intelligence">${icon('shield')}<span id="widget-health"></span></button><div class="tools"><button class="icon-button" data-action="refresh" title="立即刷新" aria-label="立即刷新">${icon('refresh')}</button><button class="icon-button" data-action="settings" title="设置" aria-label="设置">${icon('settings')}</button></div></footer><div class="widget-error" id="widget-error"></div><div class="resize-grip" id="resize-grip"></div>`;
    setupResize();
  }
  document.getElementById('widget-plan').textContent = state.snapshot?.plan ?? '';
  document.getElementById('widget-activity').textContent = state.activity.state;
  document.getElementById('widget-status')?.replaceChildren(document.createTextNode(state.status || '连接后自动显示额度'));
  document.getElementById('widget-health').textContent = `额度健康 · ${state.decision.overall.health}`;
  const error = document.getElementById('widget-error'); error.textContent = state.snapshot && state.status ? state.status : ''; error.title = state.status;
  document.querySelector('.status-dot').classList.toggle('pulse', state.activity.state === '工作中');
  document.querySelector('[data-action="refresh"]').classList.toggle('spin', state.busy);
  for (const w of windows) {
    const element = document.querySelector(`[data-window="${w.minutes}"]`), expired = w.reset && w.reset <= Date.now();
    element.querySelector('.quota-visual').outerHTML = quotaVisual(w.remaining,expired);
    element.querySelector('.percent').style.color = state.decision.fresh && !expired ? 'var(--text)' : 'var(--muted)';
    element.querySelector('.countdown').dataset.reset = w.reset || ''; element.querySelector('.countdown').textContent = countdown(w.reset);
  }
}
function setupResize() {
  const grip = document.getElementById('resize-grip'); let start;
  grip.addEventListener('pointerdown', event => { start = { pointerX:event.screenX, pointerY:event.screenY, x:window.screenX, y:window.screenY, width:window.outerWidth, height:window.outerHeight }; grip.setPointerCapture(event.pointerId); event.preventDefault(); });
  grip.addEventListener('pointermove', event => { if (!start) return; api.resize({ x:start.x, y:start.y, width:start.width+event.screenX-start.pointerX, height:start.height+event.screenY-start.pointerY }); });
  grip.addEventListener('pointerup', () => { start=null; }); grip.addEventListener('lostpointercapture',()=>{start=null;});
}
document.addEventListener('click', async event => {
  const button = event.target.closest('button'); if (!button) return;
  try {
    if (button.dataset.action) await api.action(button.dataset.action);
    else if (button.dataset.tab) { activeTab=button.dataset.tab; render(state,true); }
    else if (button.dataset.taskbarLayout) await api.preferences({taskbarLayout:button.dataset.taskbarLayout});
    else if (button.dataset.themeChoice) await api.preferences({ theme:button.dataset.themeChoice });
    else if (button.dataset.dashboardChoice) await api.preferences({ dashboardStyle:button.dataset.dashboardChoice });
    else if (button.dataset.size) { const size=Number(button.dataset.size)/100; await api.preferences({ width:Math.max(248,Math.round(290*size)),height:Math.max(188,Math.round(218*size)) }); }
    else if (button.dataset.toggle) await api.preferences({ [button.dataset.toggle]: !state.preferences[button.dataset.toggle] });
  } catch { /* The next state update shows persistence or connection errors. */ }
});
document.addEventListener('change', event => { if (event.target.id === 'taskbar-font-size') { const value = Number(event.target.value); if (Number.isFinite(value)) api.preferences({taskbarFontSize:Math.max(10,Math.min(24,value))}); } if (event.target.id === 'taskbar-offset') { const value = Number(event.target.value); if (Number.isFinite(value)) api.preferences({taskbarOffset:Math.max(0,Math.min(2000,value))}); } });
document.addEventListener('contextmenu', event => { event.preventDefault(); if(view!=='menu') api.action('menu'); });
document.addEventListener('keydown',event=>{if(event.key==='Escape')api.action('close');});
setInterval(()=>{document.querySelectorAll('[data-reset]').forEach(element=>{element.textContent=countdown(Number(element.dataset.reset)||null);});},1000);
window.__ready = api.state().then(initial=>{render(initial);api.subscribe(next=>render(next));return true;});
