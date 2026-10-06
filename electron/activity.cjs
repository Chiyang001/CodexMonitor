const fs = require('node:fs/promises');
const path = require('node:path');
const { StringDecoder } = require('node:string_decoder');
const { home } = require('./rpc.cjs');
class Activity {
  constructor() { this.reset(); }
  reset() { this.file = null; this.offset = 0; this.partial = ''; this.decoder = new StringDecoder('utf8'); this.state = '状态未知'; this.id = null; this.started = null; this.finished = null; this.tokens = null; this.total = null; this.tokenStart = null; this.model = null; this.lastEvent = 0; this.waiting = null; this.baselines = {}; this.changes = {}; this.finalObserved = false; this.error = null; }
  parse(entry) {
    const p = entry?.payload, time = Date.parse(entry?.timestamp); if (!p || !Number.isFinite(time)) return;
    if (entry.type === 'turn_context') { this.model = p.model ?? this.model; return; }
    if (entry.type === 'response_item') {
      if (p.type === 'function_call' && /(?:^|\.)request_user_input$/.test(p.name ?? '')) this.waiting = p.call_id;
      if (p.type === 'function_call_output' && p.call_id === this.waiting) this.waiting = null;
      return;
    }
    if (entry.type !== 'event_msg') return;
    this.lastEvent = time;
    if (p.type === 'task_started') { this.started = time; this.finished = null; this.id = p.turn_id ?? null; this.tokenStart = this.total; this.tokens = null; this.baselines = {}; this.changes = {}; this.finalObserved = false; this.waiting = null; }
    if (['task_complete', 'task_completed', 'turn_aborted'].includes(p.type)) this.finished = time;
    if (p.type === 'token_count') {
      const count = p.info?.total_token_usage?.total_tokens;
      if (Number.isFinite(count)) { this.total = count; this.tokens = this.tokenStart != null && count >= this.tokenStart ? count - this.tokenStart : null; }
    }
  }
  observe(snapshot) {
    if (!this.started || this.finalObserved) return;
    if (this.finished && !Object.keys(this.baselines).length) { this.finalObserved = true; return; }
    for (const w of snapshot.windows) {
      this.baselines[w.minutes] ??= { remaining: w.remaining, reset: w.reset };
      const start = this.baselines[w.minutes];
      this.changes[w.minutes] = start.reset === w.reset && (!w.reset || w.reset > snapshot.updated) && w.remaining <= start.remaining ? start.remaining - w.remaining : null;
    }
    if (this.finished && snapshot.updated >= this.finished) this.finalObserved = true;
  }
  async scan(running, now = Date.now()) {
    if (!running) { this.state = '离线'; return; }
    try {
      const files = [];
      for (let day = 0; day < 3; day++) {
        const date = new Date(now); date.setDate(date.getDate() - day);
        const folder = path.join(home(), 'sessions', String(date.getFullYear()), String(date.getMonth() + 1).padStart(2, '0'), String(date.getDate()).padStart(2, '0'));
        let entries; try { entries = await fs.readdir(folder); } catch { continue; }
        for (const name of entries.filter(name => name.endsWith('.jsonl'))) { const file = path.join(folder, name); try { const stat = await fs.stat(file); files.push({ file, modified: stat.mtimeMs, size: stat.size }); } catch {} }
      }
      const latest = files.sort((a, b) => b.modified - a.modified)[0];
      if (!latest) { this.state = '状态未知'; this.error = '未找到近期本地会话日志'; return; }
      if (this.file !== latest.file || latest.size < this.offset) { this.reset(); this.file = latest.file; }
      const handle = await fs.open(this.file, 'r');
      try {
        // Read in bounded chunks, carrying partial UTF-8 characters and JSON lines.
        const buffer = Buffer.alloc(64 * 1024);
        while (this.offset < latest.size) {
          const { bytesRead } = await handle.read(buffer, 0, Math.min(buffer.length, latest.size - this.offset), this.offset);
          if (!bytesRead) break; this.offset += bytesRead;
          const lines = (this.partial + this.decoder.write(buffer.subarray(0, bytesRead))).split('\n'); this.partial = lines.pop();
          for (const line of lines) { try { this.parse(JSON.parse(line)); } catch {} }
          if (this.partial.length > 8 * 1024 * 1024) this.partial = '';
        }
      } finally { await handle.close(); }
      this.error = null;
      this.state = !this.started || this.finished ? '空闲' : this.waiting ? '等待输入' : now - this.lastEvent >= 600000 ? '疑似卡住' : '工作中';
    } catch { this.state = '状态未知'; this.error = '本地日志暂不可读取'; }
  }
  view() { return { state: this.state, id: this.id, started: this.started, finished: this.finished, tokens: this.tokens, model: this.model, changes: this.changes, error: this.error }; }
}
module.exports = { Activity };
