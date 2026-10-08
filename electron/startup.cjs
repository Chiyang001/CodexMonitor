const path = require('node:path');
const STARTUP_NAME = 'local.codex.quota.monitor';
class LoginStartup {
  constructor(app, { simulated = false, env = process.env, execPath = process.execPath, appPath = app.getAppPath() } = {}) {
    this.app = app; this.simulated = simulated; this.enabled = false;
    // Portable builds unpack into a temporary folder; register the original EXE.
    this.options = { path: env.PORTABLE_EXECUTABLE_FILE || execPath, args: app.isPackaged ? [] : [`"${path.resolve(appPath)}"`] };
  }
  get() {
    if (this.simulated) return this.enabled;
    const settings = this.app.getLoginItemSettings(this.options);
    return settings.openAtLogin && settings.executableWillLaunchAtLogin;
  }
  set(enabled) {
    if (this.simulated) { this.enabled = enabled; return; }
    this.app.setLoginItemSettings({ ...this.options, name: STARTUP_NAME, openAtLogin: enabled, enabled });
    if (this.get() !== enabled) throw new Error('Windows 未应用更改，请检查系统启动应用设置。');
  }
}
module.exports = { LoginStartup, STARTUP_NAME };
