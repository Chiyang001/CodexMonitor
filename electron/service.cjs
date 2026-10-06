const { EventEmitter } = require('node:events');
const { RpcClient, executable, desktopRunning, authStamp } = require('./rpc.cjs');
const { Activity } = require('./activity.cjs');
const { parseQuota, decision } = require('./quota.cjs');
class MonitorService extends EventEmitter {
  constructor(storage, notify) { super(); this.storage = storage; this.notify = notify; this.activity = new Activity(); this.running = false; this.snapshot = null; this.status = '等待 Codex 启动'; this.alerted = new Set(); this.timers = []; this.closed = false; this.lastAttempt = 0; this.busy = false; this.watching = false; }
  view(now = Date.now()) {
    return { preferences: this.storage.prefs, snapshot: this.snapshot, status: this.status, busy: this.busy, running: this.running, activity: this.activity.view(), decision: decision(this.snapshot, this.storage.points, now), history: this.storage.points.filter(p => p.time >= now - 86400000), saveError: this.storage.error };
  }
  publish() { this.emit('update', this.view()); }
  async start() { await this.watch(); if (this.closed) return; this.timers.push(setInterval(() => this.watch(), 6000), setInterval(() => { if (this.running) this.refresh(); }, 30000), setInterval(() => { const expired = this.snapshot?.windows.some(w => w.reset && w.reset <= Date.now()); if (this.running && expired && Date.now() - this.lastAttempt >= 10000) this.refresh(); }, 1000)); }
  async watch() {
    if (this.watching || this.closed) return; this.watching = true;
    try {
      const found = await desktopRunning(); if (this.closed) return;
      const changed = found !== this.running; this.running = found;
      if (changed) {
        this.snapshot = null; this.activity.reset();
        this.client?.close(); this.client = null; this.status = found ? '正在连接 Codex…' : '等待 Codex 启动';
        this.emit('desktop', found);
      }
      if (found) {
        const stamp = await authStamp();
        if (stamp !== this.stamp) { this.snapshot = null; this.client?.close(); this.client = null; this.status = '正在同步登录账号…'; }
        if (changed || stamp !== this.stamp) await this.refresh();
      }
      await this.activity.scan(found); if (this.snapshot) this.activity.observe(this.snapshot); this.publish();
    } finally { this.watching = false; }
  }
  async refresh() {
    if (this.busy || this.closed || !this.running) return; this.busy = true; this.lastAttempt = Date.now(); this.publish();
    try {
      const stamp = await authStamp();
      if (!this.client || stamp !== this.stamp) { this.client?.close(); this.snapshot = null; this.stamp = stamp; this.client = new RpcClient(); await this.client.connect(await executable()); }
      const accountResult = await this.client.request('account/read', { refreshToken: false }); const account = accountResult?.account;
      if (!['chatgpt', 'chatgptAuthTokens'].includes(account?.type)) { this.snapshot = null; throw new Error(account?.type === 'apiKey' ? 'API Key 登录不提供套餐额度' : '请先在 Codex 中登录 ChatGPT 账号'); }
      const result = await this.client.request('account/rateLimits/read');
      if (this.closed || !this.running) return;
      if (stamp !== await authStamp()) { this.snapshot = null; this.client?.close(); this.client = null; this.status = '账号已切换，正在更新…'; return; }
      const identity = account.id ?? account.email ?? stamp;
      if (this.identity !== identity) { this.identity = identity; this.snapshot = null; this.activity.baselines = {}; this.activity.changes = {}; this.alerted.clear(); await this.storage.account(identity); }
      const previous = this.snapshot; this.snapshot = parseQuota(result, account.planType); this.status = '';
      await this.storage.record(this.snapshot); this.activity.observe(this.snapshot); this.alerts(previous);
    } catch (error) { if (!this.closed && this.running) this.status = error.message; this.client?.close(); this.client = null; }
    finally { this.busy = false; this.publish(); }
  }
  onceAlert(key, title, body) { if (!this.alerted.has(key)) { this.alerted.add(key); this.notify(title, body); } }
  alerts(previous, now = Date.now()) {
    const prefs = this.storage.prefs, insights = decision(this.snapshot, this.storage.points, now);
    for (const w of this.snapshot.windows) {
      if (w.reset && w.reset <= now) continue;
      const key = `${w.minutes}:${w.reset}`, old = previous?.windows.find(p => p.minutes === w.minutes);
      if (prefs.resetAlert && old?.reset && old.reset <= now && w.reset > old.reset) this.onceAlert(`reset:${key}`, `${w.label}重置已确认`, `当前剩余 ${w.remaining.toFixed(0)}%。${insights.recommendation}`);
      if (prefs.beforeResetAlert && w.reset && w.reset - now <= 1800000) this.onceAlert(`before:${key}`, '重置雷达', `${w.label}将在 30 分钟内重置`);
      if (prefs.lowAlert && w.remaining <= 20) this.onceAlert(`low:${key}`, '额度余量偏低', `${w.label}剩余 ${w.remaining.toFixed(0)}%`);
      if (prefs.paceAlert && w.minutes === 10080 && insights.windows.find(p => p.minutes === w.minutes)?.advice.fast) this.onceAlert(`pace:${key}`, '周额度消耗偏快', '按近期速度，可能在重置前耗尽');
    }
  }
  close() { this.closed = true; for (const timer of this.timers) clearInterval(timer); this.client?.close(); }
}
module.exports = { MonitorService };
