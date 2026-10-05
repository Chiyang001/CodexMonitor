$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'CodexMonitor'
$executable = Join-Path $installDir 'CodexMonitor.exe'
Get-Process -Name CodexMonitor -ErrorAction SilentlyContinue | ForEach-Object {
    try { if ($_.Path -eq $executable) { Stop-Process -Id $_.Id } } catch { }
}
foreach ($location in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Startup'))) {
    $shortcutPath = Join-Path $location 'Codex Quota Monitor.lnk'
    if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }
}
# Delete only named files in our fixed installation directory; retain user preferences.
foreach ($name in @('CodexMonitor.exe', 'README.md', 'Uninstall.ps1')) {
    $file = Join-Path $installDir $name
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
}
Write-Host '已卸载额度悬浮窗并移除启动项。窗口位置设置已保留。'
