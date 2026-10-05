param([switch]$NoStartup, [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$binary = Join-Path $taskRoot 'dist\CodexMonitor.exe'
if (-not (Test-Path -LiteralPath $binary)) { & (Join-Path $taskRoot 'build.ps1') }
$installDir = Join-Path $env:LOCALAPPDATA 'CodexMonitor'
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
$installedBinary = Join-Path $installDir 'CodexMonitor.exe'
# Close only the installed monitor, so it saves preferences and releases its own
# app-server child before replacing the executable.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CodexMonitorInstaller {
    public delegate bool Callback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    public static void Close(uint processId) {
        EnumWindows((window, parameter) => {
            uint owner; GetWindowThreadProcessId(window, out owner);
            if (owner == processId) PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }
}
'@
Get-Process -Name CodexMonitor -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.Path -eq $installedBinary) {
        [CodexMonitorInstaller]::Close([uint32]$_.Id)
        if (-not $_.WaitForExit(10000)) { throw '旧悬浮窗未退出，请从托盘退出后重新安装。' }
    }
}
Copy-Item -LiteralPath $binary -Destination (Join-Path $installDir 'CodexMonitor.exe') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $installDir 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'Uninstall.ps1') -Destination (Join-Path $installDir 'Uninstall.ps1') -Force
$shortcutShell = New-Object -ComObject WScript.Shell
$locations = @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))
# Remove the shortcut created by older versions; launching is always manual.
$legacyStartup = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Quota Monitor.lnk'
if (Test-Path -LiteralPath $legacyStartup) { Remove-Item -LiteralPath $legacyStartup -Force }
foreach ($location in $locations) {
    $shortcut = $shortcutShell.CreateShortcut((Join-Path $location 'Codex Quota Monitor.lnk'))
    $shortcut.TargetPath = Join-Path $installDir 'CodexMonitor.exe'
    $shortcut.WorkingDirectory = $installDir
    $shortcut.Description = 'Codex 5 小时和每周额度悬浮窗'
    $shortcut.Save()
}
Write-Host '安装完成。请双击桌面或开始菜单快捷方式手动启动，不会开机自启动。'
Write-Host "安装位置：$installDir"
