const { execFileSync, spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const root = path.join(__dirname, '..');
const outDir = path.join(root, 'dist', 'electron', 'win-unpacked');
const asarPath = path.join(outDir, 'resources', 'app.asar');
const staging = path.join(os.tmpdir(), 'CodexMonitor-app-staging');
const asarBin = path.join(root, 'node_modules', '@electron', 'asar', 'bin', 'asar.js');

const packEnv = {
  ...process.env,
  CSC_IDENTITY_AUTO_DISCOVERY: 'false',
  ELECTRON_MIRROR: process.env.ELECTRON_MIRROR || 'https://npmmirror.com/mirrors/electron/',
  ELECTRON_BUILDER_BINARIES_MIRROR: process.env.ELECTRON_BUILDER_BINARIES_MIRROR || 'https://npmmirror.com/mirrors/electron-builder-binaries/',
};

function runAssets() {
  execFileSync(process.execPath, [path.join(__dirname, 'assets.cjs')], { cwd: root, stdio: 'inherit' });
}

function copyPath(from, to) {
  fs.mkdirSync(path.dirname(to), { recursive: true });
  if (fs.statSync(from).isDirectory()) {
    if (process.platform === 'win32') {
      fs.mkdirSync(to, { recursive: true });
      const result = spawnSync('robocopy', [from, to, '/E', '/NFL', '/NDL', '/NJH', '/NJS', '/nc', '/ns', '/np'], { stdio: 'ignore' });
      if (result.status != null && result.status > 7) throw new Error(`复制失败: ${from}`);
      return;
    }
    fs.cpSync(from, to, { recursive: true });
    return;
  }
  fs.copyFileSync(from, to);
}

function stageApp() {
  fs.rmSync(staging, { recursive: true, force: true });
  fs.mkdirSync(staging, { recursive: true });
  for (const item of ['package.json', 'electron', 'ui']) copyPath(path.join(root, item), path.join(staging, item));
  copyPath(path.join(root, 'assets', 'Logo-rounded.png'), path.join(staging, 'assets', 'Logo-rounded.png'));
  copyPath(path.join(root, 'assets', 'developer.png'), path.join(staging, 'assets', 'developer.png'));
  copyPath(path.join(root, 'assets', 'CodexMonitor.ico'), path.join(staging, 'assets', 'CodexMonitor.ico'));
}

function syncAsar() {
  stageApp();
  fs.mkdirSync(path.dirname(asarPath), { recursive: true });
  execFileSync(process.execPath, [asarBin, 'pack', staging, asarPath], { stdio: 'inherit' });
  fs.rmSync(staging, { recursive: true, force: true });
}

function fullPack() {
  console.log('首次打包：从镜像下载 Electron 运行时（约 1–2 分钟）…');
  const result = spawnSync(process.platform === 'win32' ? 'npx.cmd' : 'npx', ['electron-builder', '--win', '--dir'], {
    cwd: root,
    stdio: 'inherit',
    env: packEnv,
  });
  if (result.status !== 0) process.exit(result.status ?? 1);
}

try {
  runAssets();
  if (!fs.existsSync(path.join(outDir, 'CodexMonitor.exe'))) {
    fullPack();
    console.log(`已生成 ${path.relative(root, outDir)}`);
  } else {
    syncAsar();
    console.log(`已更新 ${path.relative(root, asarPath)}（本地快速同步，无需联网）`);
  }
} catch (error) {
  console.error(error.message);
  process.exit(1);
}
