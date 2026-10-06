const { spawn, execFile } = require('node:child_process');
const { promisify } = require('node:util');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const readline = require('node:readline');
const execute = promisify(execFile);
const home = () => process.env.CODEX_HOME || path.join(os.homedir(), '.codex');
async function authStamp() { try { const stat = await fs.stat(path.join(home(), 'auth.json')); return `${stat.mtimeMs}:${stat.size}`; } catch { return 'missing'; } }
async function executable() {
  if (process.env.CODEX_MONITOR_CODEX_PATH) { await fs.access(process.env.CODEX_MONITOR_CODEX_PATH); return process.env.CODEX_MONITOR_CODEX_PATH; }
  const root = path.join(process.env.LOCALAPPDATA || '', 'OpenAI', 'Codex', 'bin');
  async function search(folder, depth = 0) {
    let entries; try { entries = await fs.readdir(folder, { withFileTypes: true }); } catch { return []; }
    return (await Promise.all(entries.map(async entry => {
      const file = path.join(folder, entry.name);
      if (entry.isFile() && entry.name.toLowerCase() === 'codex.exe') return [{ file, modified: (await fs.stat(file)).mtimeMs }];
      return entry.isDirectory() && depth < 3 ? search(file, depth + 1) : [];
    }))).flat();
  }
  const found = (await search(root)).sort((a, b) => b.modified - a.modified);
  if (found.length) return found[0].file;
  try { const { stdout } = await execute('where.exe', ['codex.exe'], { windowsHide: true, timeout: 5000 }); return stdout.trim().split(/\r?\n/)[0]; } catch { throw new Error('未找到 Codex，请先安装桌面应用'); }
}
const isDesktop = file => /(?:ChatGPT|Codex)\.exe$/i.test(file) && /OpenAI\.Codex_|\\OpenAI\\Codex\\app\\|\\Programs\\Codex\\/i.test(file);
async function desktopRunning() {
  try {
    const { stdout } = await execute('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', "Get-Process -Name ChatGPT,Codex -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Path } catch {} }"], { windowsHide: true, timeout: 5000 });
    return stdout.trim().split(/\r?\n/).some(isDesktop);
  } catch { return false; }
}
class RpcClient {
  constructor() { this.sequence = 0; this.pending = new Map(); }
  async connect(file, args = ['app-server', '--listen', 'stdio://']) {
    this.process = spawn(file, args, { windowsHide: true, cwd: os.homedir(), stdio: ['pipe', 'pipe', 'pipe'] });
    this.process.stderr.on('data', () => {});
    this.process.stdin.on('error', () => this.fail());
    this.process.on('error', () => this.fail()); this.process.on('exit', () => this.fail());
    this.lines = readline.createInterface({ input: this.process.stdout });
    this.lines.on('line', line => {
      let message; try { message = JSON.parse(line); } catch { return; }
      const item = this.pending.get(message.id); if (!item) return;
      this.pending.delete(message.id); clearTimeout(item.timer);
      message.error ? item.reject(new Error('Codex 额度接口暂不可用，请检查登录状态')) : item.resolve(message.result);
    });
    await this.request('initialize', { clientInfo: { name: 'codex_quota_monitor', title: 'Codex Quota Monitor', version: '1.1.0' } });
    this.send({ method: 'initialized', params: {} });
  }
  send(message) { if (!this.process || this.process.killed || !this.process.stdin.writable) throw new Error('Codex 连接已断开'); this.process.stdin.write(JSON.stringify(message) + '\n'); }
  request(method, params = null) {
    const id = ++this.sequence;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.pending.delete(id); reject(new Error('连接超时，请检查网络后刷新')); }, 15000);
      this.pending.set(id, { resolve, reject, timer });
      try { this.send({ id, method, params }); } catch (error) { clearTimeout(timer); this.pending.delete(id); reject(error); }
    });
  }
  fail() { for (const item of this.pending.values()) { clearTimeout(item.timer); item.reject(new Error('Codex 连接已断开')); } this.pending.clear(); }
  close() { this.fail(); this.lines?.close(); this.process?.stdin.end(); this.process?.kill(); this.process = null; }
}
module.exports = { RpcClient, executable, desktopRunning, isDesktop, authStamp, home };
