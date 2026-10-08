$ErrorActionPreference = 'Stop'
$installDir = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'CodexMonitor'))
$appDir = [IO.Path]::GetFullPath((Join-Path $installDir 'app'))
if (-not $appDir.StartsWith($installDir + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '卸载目录校验失败' }
$binary = Join-Path $appDir 'CodexMonitor.exe'
if (Test-Path -LiteralPath $binary) {
    $running = Get-Process -Name CodexMonitor -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $binary }
    if ($running) {
        $quitProcess = Start-Process -FilePath $binary -ArgumentList '--quit' -WindowStyle Hidden -PassThru
        if (-not $quitProcess.WaitForExit(10000)) { throw '退出请求未完成' }
        foreach ($process in $running) { if (-not $process.WaitForExit(10000)) { throw '请先从菜单退出程序' } }
    }
}
foreach ($location in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Startup'))) {
    $shortcut = Join-Path $location 'Codex Quota Monitor.lnk'
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
}
$startupName = 'local.codex.quota.monitor'
foreach ($registryPath in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run')) {
    if (Test-Path -LiteralPath $registryPath) { Remove-ItemProperty -LiteralPath $registryPath -Name $startupName -ErrorAction SilentlyContinue }
}
if (Test-Path -LiteralPath $appDir) { Remove-Item -LiteralPath $appDir -Recurse -Force }
foreach ($name in @('CodexMonitor.exe','README.md','Uninstall.ps1')) {
    $file = Join-Path $installDir $name
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
}
Write-Host '已卸载。窗口偏好与历史采样已保留。'
