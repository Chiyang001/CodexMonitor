const { spawn } = require('node:child_process');
const readline = require('node:readline');
const proc = spawn(process.argv[2] || 'codex', ['app-server', '--listen', 'stdio://'], { windowsHide: true });
const timer = setTimeout(() => { console.error('RPC timeout'); proc.kill(); process.exitCode = 1; }, 30000);
const send = message => proc.stdin.write(JSON.stringify(message) + '\n');
const lines = readline.createInterface({ input: proc.stdout });
proc.stderr.on('data', () => {}); // Do not log server diagnostics or credentials.
lines.on('line', line => {
  let message;
  try { message = JSON.parse(line); } catch { return; }
  if (message.id === 1) {
    if (message.error) return finish({ error: 'initialize failed' });
    send({ method: 'initialized', params: {} });
    send({ id: 2, method: 'account/read', params: { refreshToken: false } });
  }
  if (message.id === 2) {
    console.log(JSON.stringify({ accountType: message.result?.account?.type, planType: message.result?.account?.planType }));
    send({ id: 3, method: 'account/rateLimits/read' });
  }
  if (message.id === 3) finish(message.error ? { error: 'quota request failed' } : {
    rateLimits: message.result?.rateLimits,
    rateLimitsByLimitId: message.result?.rateLimitsByLimitId
  });
});
function finish(value) { console.log(JSON.stringify(value)); clearTimeout(timer); proc.kill(); }
proc.on('error', error => { clearTimeout(timer); console.error(error.code); process.exitCode = 1; });
send({ id: 1, method: 'initialize', params: { clientInfo: { name: 'codex_quota_monitor', title: 'Codex Quota Monitor', version: '1.0.0' } } });
