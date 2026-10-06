const { app, BrowserWindow, Tray, nativeImage, ipcMain, screen, Notification, shell } = require('electron');
const path = require('node:path');
const fs = require('node:fs/promises');
const os = require('node:os');
const { Storage, DEFAULTS } = require('./storage.cjs');
const { MonitorService } = require('./service.cjs');
const { decision } = require('./quota.cjs');
const { TaskbarHost } = require('./taskbar.cjs');
const smoke = process.argv.includes('--smoke-test');
const verify = process.argv.includes('--verify-live');
app.setName('CodexMonitor'); app.setAppUserModelId('local.codex.quota.monitor');
if (!smoke && !verify && !app.requestSingleInstanceLock()) app.quit();
const windows = new Map(); let tray, storage, service, quitting = false, geometryTimer, taskbarHost;
const icon = path.join(__dirname, '..', 'assets', 'Logo-rounded.png');
const ui = path.join(__dirname, '..', 'ui', 'index.html');
function state() { return { ...service.view(), taskbarError: taskbarHost?.error || '' }; }
function publish() {
  if (quitting) return;
  const value = state(); taskbarHost?.update(value); for (const window of windows.values()) if (!window.isDestroyed()) window.webContents.send('state', value);
  if (tray) tray.setToolTip(('Codex ' + (value.snapshot?.plan ?? '') + ' · ' + value.decision.windows.map(w => `${w.label} ${w.expired ? '—' : Math.round(w.remaining) + '%'}`).join(' · ')).slice(0, 120));
}
function fit(bounds) {
  const area = screen.getDisplayMatching(bounds).workArea;
  return { ...bounds, x: Math.round(Math.max(area.x, Math.min(bounds.x, area.x + area.width - bounds.width))), y: Math.round(Math.max(area.y, Math.min(bounds.y, area.y + area.height - bounds.height))) };
}
function saveGeometry() {
  if (smoke || quitting) return; clearTimeout(geometryTimer);
  geometryTimer = setTimeout(() => {
    const window = windows.get('widget'); if (!window || window.isDestroyed()) return;
    const { x, y } = window.getBounds(); storage.save({ x, y }).then(publish);
  }, 250);
}
// CSS rounds the content; also trim the native transparent window so its
// rectangular composition boundary cannot leave a faint outline on Windows.
function clipWindow(window, view) {
  if (process.platform !== 'win32') return;
  const { width, height } = window.getContentBounds();
  const inset = view === 'widget' ? 0 : view === 'menu' ? 10 : 12;
  const w = width - inset * 2, h = height - inset * 2;
  const radius = Math.min(view === 'widget' ? 18 : view === 'menu' ? 17 : 24, w / 2, h / 2);
  const rows = [];
  for (let y = 0; y < h; y++) {
    const dy = Math.max(0, radius - (y + .5), y + .5 - (h - radius));
    const edge = Math.floor(radius - Math.sqrt(Math.max(0, radius * radius - dy * dy)));
    const previous = rows[rows.length - 1];
    if (previous && previous.x === inset + edge && previous.width === w - edge * 2) previous.height++;
    else rows.push({ x: inset + edge, y: inset + y, width: w - edge * 2, height: 1 });
  }
  window.setShape(rows);
}
function keepWidgetOnTop(window) {
  if (!window.isDestroyed()) window.setAlwaysOnTop(true, 'screen-saver');
}
function open(view, visible = true) {
  let window = windows.get(view);
  if (window && !window.isDestroyed()) { if (visible) { if (view === 'widget') { keepWidgetOnTop(window); window.showInactive(); } else window.show(); if (view !== 'widget') window.focus(); } return window; }
  const widget = view === 'widget', menu = view === 'menu'; const area = screen.getPrimaryDisplay().workArea, prefs = storage.prefs;
  const width = widget ? prefs.width : menu ? 238 : Math.min(960, area.width - 48);
  const height = widget ? prefs.height : menu ? 390 : Math.min(688, area.height - 48);
  const bounds = widget ? fit({ width, height, x: prefs.x ?? area.x + area.width - width - 24, y: prefs.y ?? area.y + 74 }) : { width, height };
  window = new BrowserWindow({ ...bounds, frame: false, transparent: true, backgroundColor: '#00000000', resizable: false,
    show: false, hasShadow: false, skipTaskbar: menu || widget, alwaysOnTop: widget || menu,
    title: `Codex · ${view}`, icon, autoHideMenuBar: true,
    webPreferences: { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true, nodeIntegration: false, sandbox: true, webSecurity: true, backgroundThrottling: !smoke } });
  windows.set(view, window);
  clipWindow(window, view);
  window.on('resize', () => clipWindow(window, view));
  window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  window.webContents.on('will-navigate', event => event.preventDefault());
  window.webContents.on('context-menu', () => showMenu());
  window.loadFile(ui, { query: { view } });
  window.once('ready-to-show', () => { if (visible && !window.isDestroyed()) widget ? window.showInactive() : window.show(); });
  if (widget) {
    keepWidgetOnTop(window);
    window.on('show', () => keepWidgetOnTop(window));
    window.on('blur', () => keepWidgetOnTop(window));
    window.on('move', saveGeometry);
  }
  if (menu) window.on('blur', () => { if (!window.isDestroyed()) window.close(); });
  window.on('closed', () => { if (windows.get(view) === window) windows.delete(view); });
  return window;
}
function showMenu() {
  const cursor = screen.getCursorScreenPoint(), area = screen.getDisplayNearestPoint(cursor).workArea;
  const window = open('menu', false);
  window.setPosition(Math.max(area.x, Math.min(cursor.x, area.x + area.width - window.getBounds().width)), Math.max(area.y, Math.min(cursor.y, area.y + area.height - window.getBounds().height)));
  if (!window.webContents.isLoading()) { window.show(); window.focus(); }
  else window.once('ready-to-show', () => { if (!window.isDestroyed()) { window.show(); window.focus(); } });
}
function trusted(event) { return [...windows.values()].some(window => !window.isDestroyed() && window.webContents === event.sender) && event.senderFrame === event.sender.mainFrame; }
ipcMain.handle('state', event => { if (!trusted(event)) throw new Error('Unknown window'); return state(); });
ipcMain.handle('preferences', async (event, patch) => {
  if (!trusted(event) || !patch || typeof patch !== 'object') throw new Error('Invalid settings');
  const safe = Object.fromEntries(Object.entries(patch).filter(([key]) => ['theme', 'dashboardStyle', 'translucent', 'showInTaskbar', 'taskbarOffset', 'taskbarDark', 'taskbarFontSize', 'taskbarLayout', 'width', 'height', 'resetAlert', 'beforeResetAlert', 'lowAlert', 'paceAlert'].includes(key)));
  const prefs = await storage.save(safe), widget = windows.get('widget');
  if (widget && !widget.isDestroyed() && ('width' in safe || 'height' in safe)) { widget.setBounds(fit({ ...widget.getBounds(), width: Math.round(prefs.width), height: Math.round(prefs.height) })); }
  publish(); return prefs;
});
ipcMain.handle('resize-widget', async (event, bounds) => {
  if (!trusted(event) || windows.get('widget')?.webContents !== event.sender) return;
  if (!bounds || !['width', 'height', 'x', 'y'].every(key => Number.isFinite(bounds[key]))) return;
  const window = windows.get('widget');
  const next = fit({ width: Math.max(248, Math.min(580, Math.round(bounds.width))), height: Math.max(188, Math.min(440, Math.round(bounds.height))), x: Math.round(bounds.x), y: Math.round(bounds.y) });
  window.setBounds(next); await storage.save(next); publish();
});
ipcMain.handle('action', async (event, action) => {
  if (!trusted(event)) throw new Error('Unknown window');
  const developerLinks = { 'developer-bilibili':'https://space.bilibili.com/404891612', 'developer-github':'https://github.com/Chiyang001?tab=repositories', 'check-update':'https://github.com/Chiyang001/CodexMonitor/releases' };
  if (Object.hasOwn(developerLinks, action)) return shell.openExternal(developerLinks[action]);
  if (action === 'menu') return showMenu();
  if (action === 'show') return void open('widget');
  if (action === 'refresh') { windows.get('menu')?.close(); await service.refresh(); return; }
  if (['settings', 'intelligence', 'notifications', 'history'].includes(action)) { windows.get('menu')?.close(); open(action); return; }
  if (action === 'close') { const window = BrowserWindow.fromWebContents(event.sender); if (window === windows.get('widget')) window.hide(); else window.close(); return; }
  if (action === 'quit') app.quit();
});
app.on('second-instance', (_, args) => { if (args.includes('--quit')) app.quit(); else open('widget'); });
app.on('window-all-closed', () => {});
app.on('before-quit', event => {
  if (quitting) return;
  quitting = true; clearTimeout(geometryTimer); service?.close(); taskbarHost?.close(); tray?.destroy();
  if (!storage) return;
  event.preventDefault();
  const widget = windows.get('widget');
  const position = widget && !widget.isDestroyed() ? widget.getBounds() : null;
  const save = position ? storage.save({ x: position.x, y: position.y }) : storage.writeQueue;
  Promise.resolve(save).finally(() => app.quit());
});
app.whenReady().then(async () => {
  storage = new Storage(smoke ? path.join(os.tmpdir(), `CodexMonitor-smoke-${process.pid}`) : path.join(process.env.LOCALAPPDATA || app.getPath('userData'), 'CodexMonitor'));
  await storage.load();
  service = new MonitorService(storage, (title, body) => { if (Notification.isSupported()) new Notification({ title, body, icon }).show(); });
  taskbarHost = new TaskbarHost(path.join(storage.folder, 'native-taskbar'));
  taskbarHost.on('status', () => { if (!quitting) { const value = state(); for (const w of windows.values()) if (!w.isDestroyed()) w.webContents.send('state', value); } });
  taskbarHost.on('action', action => { if (action === 'menu') showMenu(); else if (action === 'settings') open('settings'); });
  service.on('update', publish);
  service.on('desktop', running => { if (verify) return; const widget = windows.get('widget'); if (running) open('widget'); else widget?.hide(); });
  if (smoke) return smokeTest();
  if (verify) {
    await service.watch();
    if (!service.snapshot) throw new Error(service.status);
    console.log('PASS: Electron live Codex RPC · ' + service.snapshot.plan + ' · ' + service.snapshot.windows.length + ' quota windows');
    service.close(); app.exit(0); return;
  }
  tray = new Tray(nativeImage.createFromPath(icon).resize({ width: 20, height: 20 }));
  tray.on('double-click', () => open('widget')); tray.on('click', () => open('widget')); tray.on('right-click', showMenu);
  open('widget', false); await service.start(); publish();
}).catch(error => { console.error(error.message); app.exit(1); });
async function smokeTest() {
  const now = Date.now(); storage.prefs = { ...DEFAULTS };
  service.running = true; service.status = ''; service.snapshot = { plan: 'Plus', updated: now, windows: [{ minutes: 300, label: '5 小时', used: 32, remaining: 68, reset: now + 14400000 }, { minutes: 10080, label: '每周', used: 14, remaining: 86, reset: now + 5.9 * 86400000 }] };
  storage.points = Array.from({ length: 25 }, (_, i) => ({ time: now - (24 - i) * 3600000, minutes: 10080, used: 9 + i * 5 / 24, reset: service.snapshot.windows[1].reset }));
  service.activity.state = '工作中'; service.activity.started = now - 1080000; service.activity.tokens = 128000; service.activity.model = 'GPT-6'; service.activity.changes = { 300: 9, 10080: 2 };
  const output = process.argv.find(arg => arg.startsWith('--capture-dir='))?.slice('--capture-dir='.length);
  const artifacts = output && path.isAbsolute(output) ? output : app.isPackaged ? path.join(os.tmpdir(), 'CodexMonitor-previews') : path.join(__dirname, '..', 'artifacts');
  await fs.mkdir(artifacts, { recursive: true });
  try {
    for (const view of ['widget', 'settings', 'intelligence', 'notifications', 'history', 'menu']) {
      const window = open(view, false);
      await new Promise((resolve, reject) => { if (!window.webContents.isLoading()) resolve(); else { window.webContents.once('did-finish-load', resolve); window.webContents.once('did-fail-load', (_, code, description) => reject(new Error(description))); } });
      const errors = []; window.webContents.on('console-message', (_, level, message) => { if (level >= 3) errors.push(message); });
      await window.webContents.executeJavaScript('window.__ready');
      await window.webContents.executeJavaScript("document.documentElement.classList.add('capture')");
      window.setPosition(-10000, -10000); window.showInactive();
      if (view === 'widget') {
        window.setOpacity(1);
        if (!window.isAlwaysOnTop()) throw new Error('Widget is not always on top');
        window.hide(); window.setAlwaysOnTop(false); open('widget');
        if (!window.isAlwaysOnTop()) throw new Error('Restored widget lost always-on-top');
        window.setAlwaysOnTop(false); window.emit('blur');
        if (!window.isAlwaysOnTop()) throw new Error('Unfocused widget lost always-on-top');
      }
      await window.webContents.executeJavaScript('new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      await new Promise(resolve => setTimeout(resolve, 180));
      const check = await window.webContents.executeJavaScript(`({ count: document.querySelectorAll('button').length, view: document.body.dataset.view, text: document.body.textContent, overflow: document.documentElement.scrollWidth > innerWidth })`);
      if (!check.count || check.view !== view || check.overflow) throw new Error(`Invalid ${view} render`);
      if (view === 'settings') {
        await window.webContents.executeJavaScript("document.querySelector('[data-tab=display]').click()");
        const taskbarWidget = windows.get('widget'), taskbarBounds = JSON.stringify(taskbarWidget.getBounds());
        for (const enabled of [true, false]) {
          await window.webContents.executeJavaScript("document.querySelector('[data-toggle=showInTaskbar]').click()");
          await new Promise(resolve => setTimeout(resolve, 80));
          if (enabled) {
            await taskbarHost.starting;
            if (!taskbarHost.attached) await new Promise((resolve,reject) => { const timeout=setTimeout(()=>reject(new Error(taskbarHost.error || 'Taskbar attachment timed out')),5000); taskbarHost.once('attached',()=>{clearTimeout(timeout);resolve();}); });
            if (!taskbarHost.child || taskbarHost.error) throw new Error(taskbarHost.error || 'No taskbar host');
            for (const layout of ['one','two']) {
              const updated = new Promise((resolve,reject) => { const handler = message => { if(message.layout === layout) { clearTimeout(timeout); taskbarHost.removeListener('attached',handler); resolve(); } }; const timeout=setTimeout(()=>{taskbarHost.removeListener('attached',handler);reject(new Error('Taskbar layout did not update'));},3000); taskbarHost.on('attached',handler); });
              await window.webContents.executeJavaScript(`document.querySelector('[data-taskbar-layout="${layout}"]').click()`);
              await updated;
              if(storage.prefs.taskbarLayout !== layout) throw new Error('Taskbar layout did not save');
            }
            for (const size of [24, 12]) {
              const updated = new Promise((resolve,reject) => { const handler = message => { if(message.fontSize === size) { clearTimeout(timeout); taskbarHost.removeListener('attached',handler); resolve(); } }; const timeout=setTimeout(()=>{taskbarHost.removeListener('attached',handler);reject(new Error('Taskbar font did not update'));},3000); taskbarHost.on('attached',handler); });
              await window.webContents.executeJavaScript(`(() => { const input=document.getElementById('taskbar-font-size'); input.value=${size}; input.dispatchEvent(new Event('change',{bubbles:true})); })()`);
              await updated;
              if(storage.prefs.taskbarFontSize !== size) throw new Error('Taskbar font did not save');
            }
          } else if (taskbarHost.child) throw new Error('Taskbar host did not close');
          if (storage.prefs.showInTaskbar !== enabled || JSON.stringify(taskbarWidget.getBounds()) !== taskbarBounds) throw new Error('Taskbar toggle changed size');
        }
        console.log('PASS: Native quota text attached to Windows taskbar');
        await window.webContents.executeJavaScript("document.querySelector('[data-tab=effects]').click()");
        const widget = windows.get('widget'), before = JSON.stringify(widget.getBounds());
        for (const enabled of [true, false]) {
          await window.webContents.executeJavaScript("document.querySelector('[data-toggle=translucent]').click()");
          await new Promise(resolve => setTimeout(resolve, 80));
          if (storage.prefs.translucent !== enabled) throw new Error('Transparency toggle failed');
          if (JSON.stringify(widget.getBounds()) !== before) throw new Error('Transparency changed widget bounds');
          const actual = await widget.webContents.executeJavaScript("document.documentElement.dataset.translucent");
          if (actual !== String(enabled)) throw new Error('Transparency did not sync');
        }
        await window.webContents.executeJavaScript("document.querySelector('[data-tab=theme]').click()");
        for (const theme of ["mint","paper","orbit","amber","ocean","rose","graphite","sage","peach","lavender"]) {
          await window.webContents.executeJavaScript(`document.querySelector('[data-theme-choice="${theme}"]').click()`);
          await new Promise(resolve => setTimeout(resolve, 160));
          const actual = await window.webContents.executeJavaScript('document.documentElement.dataset.theme'); if (actual !== theme) throw new Error('Theme switch failed');
          const solid = await window.webContents.executeJavaScript("(() => { const s=getComputedStyle(document.querySelector('.surface')); return s.backgroundColor.startsWith('rgb(') && s.backdropFilter === 'none'; })()");
          if (!solid) throw new Error('Theme lost solid background');
          await fs.writeFile(path.join(artifacts, `electron-settings-${theme}.png`), (await window.webContents.capturePage()).toPNG());
        }
        await storage.save({ theme: 'mint' }); publish();
        await window.webContents.executeJavaScript("document.querySelector('[data-tab=about]').click()");
        const about = await window.webContents.executeJavaScript("({name:document.body.textContent.includes('炽阳001'),logo:document.querySelector('.developer-logo').complete && document.querySelector('.developer-logo').naturalWidth > 0})");
        if(!about.name || !about.logo) throw new Error('Developer information missing');
        const external = [], originalExternal = shell.openExternal;
        shell.openExternal = async url => { external.push(url); };
        try {
          for(const action of ['developer-bilibili','developer-github','check-update']) {
            await window.webContents.executeJavaScript(`document.querySelector('[data-action="${action}"]').click()`);
            await new Promise(resolve=>setTimeout(resolve,50));
          }
        } finally { shell.openExternal = originalExternal; }
        if(external.join('|') !== 'https://space.bilibili.com/404891612|https://github.com/Chiyang001?tab=repositories|https://github.com/Chiyang001/CodexMonitor/releases') throw new Error('Developer links incorrect');
        await fs.writeFile(path.join(artifacts,'electron-settings-about.png'),(await window.webContents.capturePage()).toPNG());
        for (const tab of ['dashboard', 'size']) {
          await window.webContents.executeJavaScript(`document.querySelector('[data-tab="${tab}"]').click()`);
          await fs.writeFile(path.join(artifacts, `electron-settings-${tab}.png`), (await window.webContents.capturePage()).toPNG());
        }
        await window.webContents.executeJavaScript("document.querySelector('[data-tab=dashboard]').click()");
        for (const style of ['rings', 'gauge', 'segments', 'numbers', 'bars']) {
          await window.webContents.executeJavaScript(`document.querySelector('[data-dashboard-choice="${style}"]').click()`);
          await new Promise(resolve => setTimeout(resolve, 100));
          if (storage.prefs.dashboardStyle !== style || storage.prefs.theme !== 'mint') throw new Error('Dashboard style setting failed');
          const widget = windows.get('widget');
          const valid = await widget.webContents.executeJavaScript(`document.querySelectorAll('[data-visual-style="${style}"]').length === 2`);
          if (!valid) throw new Error('Widget dashboard did not update');
          await widget.webContents.executeJavaScript("document.documentElement.classList.remove('capture')");
          await fs.writeFile(path.join(artifacts, `electron-widget-${style}.png`), (await widget.webContents.capturePage()).toPNG());
        }
        if (await window.webContents.executeJavaScript("!!document.querySelector('[data-tab=opacity], #opacity')")) throw new Error('Opacity setting still visible');
        await window.webContents.executeJavaScript("document.querySelector('[data-tab=size]').click()");
        await window.webContents.executeJavaScript(`document.querySelector('[data-size="125"]').click()`);
        await new Promise(resolve => setTimeout(resolve, 80)); if (storage.prefs.width !== Math.round(DEFAULTS.width * 1.25)) throw new Error('Size setting failed');
      }
      if (view === 'menu') {
        const visible = await window.webContents.executeJavaScript("(() => { const b=document.querySelector('[data-action=quit]').getBoundingClientRect(); const m=document.querySelector('.menu').getBoundingClientRect(); return b.bottom <= m.bottom - 8 && b.bottom <= innerHeight - 10; })()");
        if (!visible) throw new Error('Quit button clipped');
      }
      if (view === 'notifications') {
        const widget = windows.get('widget'), beforeBounds = widget.getBounds();
        const size = { width: storage.prefs.width, height: storage.prefs.height };
        let resized = 0; const countResize = () => resized++; widget.on('resize', countResize);
        for (const key of ['beforeResetAlert', 'resetAlert', 'lowAlert', 'paceAlert']) {
          for (let i = 0; i < 6; i++) {
            const before = storage.prefs[key];
            await window.webContents.executeJavaScript(`document.querySelector('[data-toggle="${key}"]').click()`);
            await new Promise(resolve => setTimeout(resolve, 50));
            if (storage.prefs[key] === before) throw new Error('Notification switch failed');
          }
        }
        widget.removeListener('resize', countResize);
        if (resized || JSON.stringify(widget.getBounds()) !== JSON.stringify(beforeBounds)) throw new Error('Reminder changed widget bounds');
        if (storage.prefs.width !== size.width || storage.prefs.height !== size.height) throw new Error('Reminder changed saved size');
        widget.close();
        let reopenedSize;
        for (let i = 0; i < 3; i++) {
          await storage.load(); const reopened = open('widget', false);
          await new Promise(resolve => reopened.once('ready-to-show', resolve));
          const actual = reopened.getBounds();
          if (!reopenedSize) reopenedSize = { width: actual.width, height: actual.height };
          if (actual.width !== reopenedSize.width || actual.height !== reopenedSize.height || storage.prefs.width !== size.width || storage.prefs.height !== size.height) throw new Error('Widget grew on reopen');
          if (i < 2) reopened.close();
        }
        console.log('PASS: 24 reminder toggles and 3 widget reloads preserve size');
      }
      if (!storage.prefs.translucent) {
        await window.webContents.executeJavaScript("document.documentElement.classList.remove('capture')");
        const solid = await window.webContents.executeJavaScript("(() => { const s=getComputedStyle(document.querySelector('.surface')); return s.backgroundColor.startsWith('rgb(') && s.backdropFilter === 'none'; })()");
        if (!solid) throw new Error('Non-opaque ' + view);
      }
      await new Promise(resolve => setTimeout(resolve, 250));
      const capture = await window.webContents.capturePage();
      const bitmap = capture.toBitmap();
      if (!bitmap.some((value, index) => index % 4 === 3 && value > 0)) throw new Error(`Blank ${view} capture`);
      await fs.writeFile(path.join(artifacts, `electron-${view}.png`), capture.toPNG());
      if (errors.length) throw new Error(errors.join('\n')); console.log(`PASS: Electron ${view}`);
      if (view !== 'widget') window.close();
    }
    console.log('PASS: UI themes, navigation, size and reminder interactions'); app.exit(0);
  } catch (error) { console.error(error.stack); app.exit(1); }
}
