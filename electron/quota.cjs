const clamp = (value, min = 0, max = 100) => Math.max(min, Math.min(max, value));
const label = minutes => minutes === 10080 ? '每周' : minutes === 1440 ? '每日' : !minutes ? '当前额度' : minutes % 1440 === 0 ? `${minutes / 1440} 天` : minutes % 60 === 0 ? `${minutes / 60} 小时` : `${minutes} 分钟`;
function parseQuota(result, accountPlan, now = Date.now()) {
  const bucket = result?.rateLimitsByLimitId ? result.rateLimitsByLimitId.codex : result?.rateLimits;
  if (!bucket) throw new Error('此账号没有返回 Codex 套餐额度');
  const windows = ['primary', 'secondary'].map(key => bucket[key]).filter(item => item && item.usedPercent != null && Number.isFinite(Number(item.usedPercent))).map(item => {
    const minutes = Number.isInteger(Number(item.windowDurationMins)) && Number(item.windowDurationMins) > 0 ? Number(item.windowDurationMins) : 0;
    const reset = Number(item.resetsAt) * 1000;
    return { minutes, label: label(minutes), used: clamp(Number(item.usedPercent)), remaining: clamp(100 - Number(item.usedPercent)), reset: Number.isFinite(reset) && reset > 0 && reset <= 253402300799000 ? reset : null };
  }).sort((a, b) => a.minutes - b.minutes);
  const names = { free: 'Free', go: 'Go', plus: 'Plus', pro: 'Pro', business: 'Business', team: 'Business', enterprise: 'Enterprise', edu: 'Edu' };
  return { windows, plan: names[String(bucket.planType ?? accountPlan ?? '').toLowerCase()] ?? '', updated: now };
}
function analyze(window, points, now = Date.now(), fresh = true) {
  const result = { health: '数据不足', fast: false, rate: null, exhaustion: null, today: null, budget: null, safe: null, explanation: '预测需要至少 15 分钟同周期采样' };
  if (!fresh) return { ...result, explanation: '额度数据未更新，等待重新连接' };
  if (!window || !window.reset || !window.minutes || window.reset <= now) return { ...result, explanation: '等待有效额度与重置时间' };
  const cycle = points.filter(p => p.minutes === window.minutes && p.reset === window.reset && p.time <= now).sort((a, b) => a.time - b.time);
  const recent = cycle.filter(p => p.time >= now - Math.min(86400000, window.minutes * 60000));
  const consumption = list => list.slice(1).reduce((sum, p, i) => sum + Math.max(0, p.used - list[i].used), 0);
  const corrected = list => list.some((p, i) => i > 0 && p.used < list[i - 1].used);
  const span = recent.length > 1 ? recent.at(-1).time - recent[0].time : 0;
  const days = (window.reset - now) / 86400000;
  if (window.remaining === 0) { result.health = '已耗尽'; result.explanation = '等待接口确认额度恢复'; }
  if (span >= 900000 && !corrected(recent)) {
    result.rate = consumption(recent) * 86400000 / span;
    result.fast = result.rate * days > window.remaining;
    result.health = window.remaining === 0 ? '已耗尽' : result.fast ? '消耗偏快' : '充足';
    result.exhaustion = result.rate > 0 ? now + Math.min(3650, window.remaining / result.rate) * 86400000 : null;
    result.explanation = result.fast ? '按近期速度，可能在重置前耗尽' : `按近期速度，重置时预计剩余 ${Math.max(0, window.remaining - result.rate * days).toFixed(1)}%`;
  } else if (corrected(recent)) result.explanation = '额度发生修正，等待新采样再预测';
  const midnight = new Date(now); midnight.setHours(0, 0, 0, 0);
  const today = cycle.filter(p => p.time >= midnight.getTime());
  if (today.length >= 2 && !corrected(today)) {
    const start = today[0]; result.today = consumption(today);
    result.budget = Math.min(100 - start.used, (100 - start.used) / Math.max(1, (window.reset - start.time) / 86400000));
    result.safe = Math.min(window.remaining, Math.max(0, result.budget - result.today));
  }
  return result;
}
function decision(snapshot, points, now = Date.now()) {
  const fresh = !!snapshot && now - snapshot.updated < 75000;
  const windows = (snapshot?.windows ?? []).map(window => ({ ...window, expired: !!window.reset && window.reset <= now, advice: analyze(window, points, now, fresh) }));
  const budget = windows.find(w => w.minutes === 10080) ?? windows.find(w => w.minutes >= 1440);
  const timeline = [];
  for (const window of windows) {
    if (window.reset > now && window.reset <= now + 86400000) timeline.push({ time: window.reset, label: `${window.label}预计重置`, type: 'reset', detail: '以接口重新读取为准' });
    if (fresh && window.advice.fast && window.advice.exhaustion <= now + 86400000) timeline.push({ time: window.advice.exhaustion, label: `${window.label}预计耗尽`, type: 'risk', detail: '按近期观测速率估算' });
  }
  let recommendation = '等待有效额度数据';
  if (fresh && windows.length) {
    const limited = windows.find(w => w.remaining <= 20 || w.advice.fast);
    recommendation = windows.some(w => w.expired) ? '等重置确认后，再安排大型任务' : limited ? '建议放慢节奏，重置后重新检查额度' : windows.some(w => w.advice.rate == null) ? '额度尚有余量，积累采样后给出使用建议' : '当前节奏平稳，可以继续使用';
  }
  const overall = windows.find(w => w.advice.health === '已耗尽')?.advice ?? windows.find(w => w.advice.fast)?.advice ?? windows.find(w => w.advice.rate == null)?.advice ?? windows[0]?.advice ?? { health: '数据不足' };
  return { fresh, windows, overall, budget: budget ? { label: budget.label, remaining: budget.remaining, ...budget.advice } : null, timeline: timeline.sort((a, b) => a.time - b.time), recommendation };
}
module.exports = { clamp, label, parseQuota, analyze, decision };
