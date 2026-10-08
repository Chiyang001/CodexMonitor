const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
const DEFAULTS = { theme: 'mint', dashboardStyle: 'bars', translucent: false, showInTaskbar: false, showWidgetOnStartup: true, autoStart: false, taskbarOffset: 340, taskbarDark: true, taskbarFontSize: 12, taskbarLayout: 'two', width: 290, height: 218, pinned: true, x: null, y: null, resetAlert: true, beforeResetAlert: false, lowAlert: false, paceAlert: true };
function normalize(value = {}) {
  const pick = (key, old) => value[key] ?? value[old] ?? DEFAULTS[key];
  const prefs = { ...DEFAULTS };
  for (const [key, old] of Object.entries({ theme: 'Theme', translucent: 'Translucent', showInTaskbar: 'ShowInTaskbar', showWidgetOnStartup: 'ShowWidgetOnStartup', autoStart: 'AutoStart', taskbarOffset: 'TaskbarOffset', taskbarDark: 'TaskbarDark', taskbarFontSize: 'TaskbarFontSize', taskbarLayout: 'TaskbarLayout', dashboardStyle: 'DashboardStyle', width: 'Width', height: 'Height', pinned: 'Pinned', x: 'X', y: 'Y', resetAlert: 'ResetAlert', beforeResetAlert: 'BeforeResetAlert', lowAlert: 'LowAlert', paceAlert: 'PaceAlert' })) prefs[key] = pick(key, old);
  if (value.width == null && value.Width === 208 && [122,152].includes(value.Height)) { prefs.width = DEFAULTS.width; prefs.height = DEFAULTS.height; }
  if (!["mint","paper","orbit","amber","ocean","rose","graphite","sage","peach","lavender"].includes(prefs.theme)) prefs.theme = 'mint';
  if (!['bars', 'rings', 'gauge', 'segments', 'numbers'].includes(prefs.dashboardStyle)) prefs.dashboardStyle = 'bars';
  if (!['one','two'].includes(prefs.taskbarLayout)) prefs.taskbarLayout = 'two';
  for (const [key, min, max] of [['taskbarFontSize', 10, 24], ['taskbarOffset', 0, 2000], ['width', 248, 580], ['height', 188, 440]]) prefs[key] = Number.isFinite(prefs[key]) ? Math.max(min, Math.min(max, prefs[key])) : DEFAULTS[key];
  for (const key of ['x', 'y']) if (!Number.isFinite(prefs[key]) || prefs[key] === -1) prefs[key] = null;
  for (const key of ['showWidgetOnStartup', 'autoStart', 'taskbarDark', 'showInTaskbar', 'translucent', 'pinned', 'resetAlert', 'beforeResetAlert', 'lowAlert', 'paceAlert']) if (typeof prefs[key] !== 'boolean') prefs[key] = DEFAULTS[key];
  prefs.pinned = true; // The widget always stays above other application windows.
  return prefs;
}
class Storage {
  constructor(folder) { this.folder = folder; this.prefs = { ...DEFAULTS }; this.points = []; this.writeQueue = Promise.resolve(); this.error = false; }
  async load() { try { this.prefs = normalize(JSON.parse(await fs.readFile(path.join(this.folder, 'settings.json'), 'utf8'))); } catch {} return this.prefs; }
  async write(file, data) {
    const operation = async () => {
      await fs.mkdir(this.folder, { recursive: true });
      const target = path.join(this.folder, file), temporary = target + '.tmp';
      await fs.writeFile(temporary, JSON.stringify(data)); await fs.rename(temporary, target);
    };
    this.writeQueue = this.writeQueue.catch(() => {}).then(operation);
    try { await this.writeQueue; this.error = false; } catch { this.error = true; }
  }
  async save(patch) { this.prefs = normalize({ ...this.prefs, ...patch }); await this.write('settings.json', this.prefs); return this.prefs; }
  async account(identity) {
    this.historyFile = `usage-${crypto.createHash('sha256').update(identity).digest('hex').slice(0, 24).toUpperCase()}.json`;
    try {
      const data = JSON.parse(await fs.readFile(path.join(this.folder, this.historyFile), 'utf8'));
      this.points = Array.isArray(data) ? data.map(p => ({ time: p.time ?? p.Time * 1000, minutes: p.minutes ?? p.Minutes, used: p.used ?? p.Used, reset: p.reset ?? (p.Reset ? p.Reset * 1000 : null) })).filter(p => Number.isFinite(p.time) && Number.isFinite(p.used) && p.used >= 0 && p.used <= 100 && Number.isFinite(p.minutes)) : [];
    } catch { this.points = []; }
  }
  async record(snapshot) {
    this.points = this.points.filter(p => p.time >= snapshot.updated - 8 * 86400000 && p.time <= snapshot.updated);
    for (const window of snapshot.windows) {
      if (window.reset && window.reset <= snapshot.updated) continue;
      const last = this.points.findLast(p => p.minutes === window.minutes);
      if (last && snapshot.updated - last.time < 25000 && last.reset === window.reset) continue;
      this.points.push({ time: snapshot.updated, minutes: window.minutes, reset: window.reset, used: window.used });
    }
    if (this.historyFile) await this.write(this.historyFile, this.points);
  }
}
module.exports = { Storage, normalize, DEFAULTS };
