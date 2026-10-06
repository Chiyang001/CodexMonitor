const { EventEmitter } = require('node:events');
const { spawn, execFile } = require('node:child_process');
const { promisify } = require('node:util');
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
const exec = promisify(execFile);

function taskbarData(state) {
  const percent = minutes => {
    const w = state.decision.windows.find(w => w.minutes === minutes);
    return !w || w.expired || !state.decision.fresh ? '—' : `${Math.round(w.remaining)}%`;
  };
  return { enabled: state.preferences.showInTaskbar, five: percent(300), week: percent(10080), offset: state.preferences.taskbarOffset, dark: state.preferences.taskbarDark, fontSize: state.preferences.taskbarFontSize, layout: state.preferences.taskbarLayout };
}
class TaskbarHost extends EventEmitter {
  constructor(folder) { super(); this.folder = folder; this.child = null; this.starting = null; this.latest = null; this.closed = false; this.error = ''; this.attached = false; }
  async start() {
    const source = await fs.readFile(path.join(__dirname, 'TaskbarText.cs'), 'utf8');
    const hash = crypto.createHash('sha256').update(source).digest('hex').slice(0, 16);
    await fs.mkdir(this.folder, { recursive: true });
    const file = path.join(this.folder, `taskbar-${hash}.exe`);
    try { await fs.access(file); } catch {
      const input = path.join(this.folder, `taskbar-${hash}.cs`);
      await fs.writeFile(input, source, 'utf8');
      const compiler = path.join(process.env.WINDIR || 'C:\\Windows', 'Microsoft.NET', 'Framework64', 'v4.0.30319', 'csc.exe');
      await exec(compiler, ['/nologo', '/target:exe', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll', `/out:${file}`, input], { windowsHide: true, timeout: 20000 });
    }
    if (this.closed || !this.latest?.enabled) return;
    const child = this.child = spawn(file, [String(process.pid)], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    let buffer = '';
    child.stdout.on('data', chunk => {
      buffer += chunk.toString(); let end;
      while ((end = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, end); buffer = buffer.slice(end + 1);
        try { const message = JSON.parse(line); if (message.attached) { this.attached = true; this.error = ''; this.emit('attached', message); } if (message.action) this.emit('action', message.action); if (message.error) this.fail(message.error); } catch {}
      }
    });
    child.stdin.on('error', error => this.fail(error.message));
    child.on('error', error => this.fail(error.message));
    child.on('exit', () => { if (this.child === child) { this.child = null; this.attached = false; } });
    this.send();
  }
  fail(message) { this.error = `任务栏文字显示失败：${message}`; this.emit('status'); }
  update(state) {
    this.latest = taskbarData(state);
    if (this.closed) return;
    if (!this.latest.enabled) { this.child?.kill(); this.child = null; this.attached = false; this.error = ''; return; }
    if (this.child) return this.send();
    if (!this.starting) this.starting = this.start().catch(error => this.fail(error.message)).finally(() => { this.starting = null; });
  }
  send() { if (this.child?.stdin.writable) this.child.stdin.write(JSON.stringify(this.latest) + '\n'); }
  close() { this.closed = true; this.child?.kill(); this.child = null; }
}
module.exports = { TaskbarHost, taskbarData };
