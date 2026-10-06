param([switch]$NoStartup, [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$bundle = Join-Path $taskRoot 'dist\electron\win-unpacked'
$binary = Join-Path $bundle 'CodexMonitor.exe'
if (-not (Test-Path -LiteralPath $binary)) { & (Join-Path $taskRoot 'build.ps1') }
if (-not (Test-Path -LiteralPath $binary)) { throw '未找到 Electron 发布程序' }
$installDir = Join-Path $env:LOCALAPPDATA 'CodexMonitor'
$appDir = Join-Path $installDir 'app'
New-Item -ItemType Directory -Force -Path $appDir | Out-Null
$installedBinary = Join-Path $appDir 'CodexMonitor.exe'
if (Test-Path -LiteralPath $installedBinary) {
    $running = Get-Process -Name CodexMonitor -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installedBinary }
    if ($running) {
        $quitProcess = Start-Process -FilePath $installedBinary -ArgumentList '--quit' -WindowStyle Hidden -PassThru
        if (-not $quitProcess.WaitForExit(10000)) { throw '退出请求未完成' }
        foreach ($process in $running) { if (-not $process.WaitForExit(10000)) { throw '旧 Electron 版本未退出，请从菜单退出。' } }
    }
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CodexMonitorUpgrade {
 public delegate bool Callback(IntPtr h, IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback c, IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
 public static void Close(uint id) { EnumWindows((h,p) => { uint owner; GetWindowThreadProcessId(h,out owner); if(owner==id) PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero); return true; },IntPtr.Zero); }
}
'@
$legacyBinary = Join-Path $installDir 'CodexMonitor.exe'
Get-Process -Name CodexMonitor -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.Path -eq $legacyBinary) { [CodexMonitorUpgrade]::Close([uint32]$_.Id); if (-not $_.WaitForExit(10000)) { throw '旧悬浮窗未退出' } }
}
Get-ChildItem -LiteralPath $bundle -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $appDir -Recurse -Force }
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $installDir 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'Uninstall.ps1') -Destination (Join-Path $installDir 'Uninstall.ps1') -Force
$shortcutShell = New-Object -ComObject WScript.Shell
foreach ($location in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
    $shortcut = $shortcutShell.CreateShortcut((Join-Path $location 'Codex Quota Monitor.lnk'))
    $shortcut.TargetPath = $installedBinary
    $shortcut.WorkingDirectory = $appDir
    $shortcut.IconLocation = $installedBinary + ',0'
    $shortcut.Description = 'Codex 额度决策助手 · Electron'
    $shortcut.Save()
}
$legacyStartup = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Quota Monitor.lnk'
if (Test-Path -LiteralPath $legacyStartup) { Remove-Item -LiteralPath $legacyStartup -Force }
Write-Host 'Electron 版安装完成，设置与历史已保留。请从桌面或开始菜单手动启动，不会开机自启动。'
Write-Host $installedBinary
