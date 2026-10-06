const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const { parseQuota, analyze, decision } = require('../electron/quota.cjs');
const { Storage, normalize } = require('../electron/storage.cjs');
const { Activity } = require('../electron/activity.cjs');
const { RpcClient, isDesktop } = require('../electron/rpc.cjs');
const now = new Date(2026, 9, 6, 12).getTime();
test('Codex group, supported periods, fallback plan and clamped values', () => {
  const snapshot = parseQuota({ rateLimitsByLimitId: { codex: { primary: { usedPercent: 32, windowDurationMins: 300 }, secondary: { usedPercent: 120, windowDurationMins: 10080 } }, other: { primary: { usedPercent: 1 } } } }, 'plus', now);
  assert.equal(snapshot.plan, 'Plus'); assert.equal(snapshot.windows[0].remaining, 68); assert.equal(snapshot.windows[1].remaining, 0);
  assert.throws(() => parseQuota({ rateLimitsByLimitId: { other: {} }, rateLimits: {} }));
});
test('Absent windows and reset are unknown, not unlimited', () => {
  const snapshot = parseQuota({ rateLimits: { primary: null, secondary: { usedPercent: 10, windowDurationMins: 15 } } }, 'free', now);
  assert.equal(snapshot.windows.length, 1); assert.equal(snapshot.windows[0].label, '15 分钟'); assert.equal(snapshot.windows[0].reset, null);
  assert.equal(parseQuota({ rateLimits: { primary: { usedPercent: null } } }).windows.length, 0);
  assert.equal(parseQuota({ rateLimits: { primary: { usedPercent: 'NaN' } } }).windows.length, 0);
});
test('Rate compares consumption with remaining cycle, not fixed percentage', () => {
  const window = { minutes: 10080, reset: now + 4 * 86400000, remaining: 80 };
  const points = [{ minutes:10080,reset:window.reset,time:now-3600000,used:19 }, { minutes:10080,reset:window.reset,time:now,used:20 }];
  const advice = analyze(window, points, now); assert.equal(advice.rate, 24); assert.equal(advice.fast, true); assert.ok(advice.exhaustion < window.reset);
  assert.equal(analyze({ ...window, reset: window.reset + 1 }, points, now).rate, null);
});
test('Budget avoids double subtraction and suspends on corrections', () => {
  const window = { minutes:10080,reset:now+4*86400000,remaining:70 };
  const points = [{ minutes:10080,reset:window.reset,time:now-7200000,used:20 },{ minutes:10080,reset:window.reset,time:now,used:30 }];
  const advice = analyze(window,points,now); assert.equal(advice.today,10); assert.ok(Math.abs(advice.safe-(80/(4+2/24)-10))<.0001);
  points[1].used=15; assert.equal(analyze(window,points,now).rate,null); assert.equal(analyze(window,points,now).safe,null);
});
test('Stale and expired data never fabricate recovery or recommendations', () => {
  const window = { minutes:300,label:'5 小时',remaining:80,reset:now-1 };
  assert.equal(analyze(window,[],now).safe,null);
  const result = decision({ windows:[window],updated:now-80000 },[],now); assert.equal(result.fresh,false); assert.equal(result.windows[0].expired,true); assert.equal(result.timeline.length,0); assert.equal(result.recommendation,'等待有效额度数据');
});
test('Zero consumption has no finite exhaustion estimate', () => {
  const window={ minutes:10080,reset:now+86400000,remaining:90 };
  const points=[{ minutes:10080,reset:window.reset,time:now-3600000,used:10 },{ minutes:10080,reset:window.reset,time:now,used:10 }];
  assert.equal(analyze(window,points,now).health,'充足'); assert.equal(analyze(window,points,now).exhaustion,null);
});
test('Legacy preferences migrate and invalid settings are bounded', () => {
  assert.equal(normalize().showInTaskbar,false); assert.equal(normalize({showInTaskbar:true}).showInTaskbar,true); assert.equal(normalize({showInTaskbar:1}).showInTaskbar,false); assert.equal(normalize().translucent,false); assert.equal(normalize({translucent:false}).translucent,false); assert.equal(normalize({translucent:'false'}).translucent,false);
  const prefs=normalize({ Theme:'orbit',Opacity:.8,Width:300,Height:200,X:-1,Pinned:false,ResetAlert:false });
  assert.equal(prefs.theme,'orbit'); assert.equal(prefs.dashboardStyle,'bars'); assert.equal(prefs.x,null); assert.equal(prefs.pinned,true); assert.equal(prefs.resetAlert,false);
  assert.equal(normalize({theme:'bad',opacity:NaN,width:0,height:Infinity}).theme,'mint'); assert.equal(normalize({dashboardStyle:'bad'}).dashboardStyle,'bars'); assert.equal(normalize({dashboardStyle:'rings'}).dashboardStyle,'rings');
});
test('Account-isolated persistence migrates C# points and survives restart', async () => {
  const folder=await fs.mkdtemp(path.join(os.tmpdir(),'codex-storage-test-')); const storage=new Storage(folder);
  await storage.account('account-a'); const first=storage.historyFile;
  await storage.write(first,[{Time:now/1000,Minutes:10080,Used:20,Reset:now/1000+10000}]); await storage.account('account-a'); assert.equal(storage.points[0].time,now);
  await storage.account('account-b'); assert.notEqual(storage.historyFile,first); assert.equal(storage.points.length,0);
  await storage.save({theme:'amber',translucent:false,showInTaskbar:true}); const restarted=new Storage(folder); await restarted.load(); assert.equal(restarted.prefs.theme,'amber'); assert.equal(restarted.prefs.translucent,false); assert.equal(restarted.prefs.showInTaskbar,true);
  const files=await fs.readdir(folder); for(const file of files) await fs.unlink(path.join(folder,file)); await fs.rmdir(folder);
});
test('Task tokens, completed quotas and resets are not attributed to later tasks', () => {
  const activity=new Activity(); const event=(type,payload,time=now)=>activity.parse({type:'event_msg',timestamp:new Date(time).toISOString(),payload:{type,...payload}});
  event('token_count',{info:{total_token_usage:{total_tokens:100}}}); event('task_started',{turn_id:'task-1'}); event('token_count',{info:{total_token_usage:{total_tokens:250}}}); assert.equal(activity.tokens,150);
  const snapshot={updated:now,windows:[{minutes:300,remaining:80,reset:now+100000}]}; activity.observe(snapshot); snapshot.windows[0].remaining=75; activity.observe(snapshot); assert.equal(activity.changes[300],5);
  event('task_complete',{}); activity.observe(snapshot); snapshot.windows[0].remaining=20; activity.observe(snapshot); assert.equal(activity.changes[300],5);
  event('task_started',{turn_id:'task-2'}); activity.observe(snapshot); snapshot.windows[0].reset+=10000; activity.observe(snapshot); assert.equal(activity.changes[300],null);
});
test('Desktop detection excludes the monitor app-server', () => {
  assert.equal(isDesktop('C:\\Program Files\\WindowsApps\\OpenAI.Codex_1\\app\\ChatGPT.exe'),true);
  assert.equal(isDesktop('C:\\Users\\me\\AppData\\Local\\OpenAI\\Codex\\bin\\version\\codex.exe'),false);
});
test('RPC handshake, out-of-order responses, server errors and disconnect', async () => {
  const client=new RpcClient(); await client.connect(process.execPath,[path.join(__dirname,'mock-rpc.cjs')]);
  const a=client.request('test/a'),b=client.request('test/b'); const result=await Promise.all([a,b]); assert.equal(result[0].name,'a'); assert.equal(result[1].name,'b');
  await assert.rejects(client.request('test/error')); await assert.rejects(client.request('test/exit')); client.close();
});


test('Taskbar quota text keeps unknown, stale and expired windows distinct from zero', () => {
  const { taskbarData } = require('../electron/taskbar.cjs');
  const state = { preferences:normalize({showInTaskbar:true}), decision:{fresh:true,windows:[{minutes:300,remaining:0},{minutes:10080,remaining:86}]} };
  assert.deepEqual(taskbarData(state),{enabled:true,five:'0%',week:'86%',offset:340,dark:true,fontSize:12,layout:'two'});
  state.decision.windows[1].expired=true; assert.equal(taskbarData(state).week,'—');
  state.decision.fresh=false; assert.equal(taskbarData(state).five,'—');
  state.decision.windows=[]; assert.equal(taskbarData(state).week,'—');
  assert.equal(normalize({taskbarLayout:'one'}).taskbarLayout,'one');
  assert.equal(normalize({taskbarLayout:'invalid'}).taskbarLayout,'two');
  assert.equal(normalize({taskbarFontSize:100}).taskbarFontSize,24);
  assert.equal(normalize({taskbarFontSize:NaN}).taskbarFontSize,12);
  assert.equal(normalize({taskbarOffset:-10}).taskbarOffset,0);
  assert.equal(normalize({taskbarOffset:Infinity}).taskbarOffset,340);
});
