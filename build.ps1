param([switch]$SkipUiTests)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw '开发构建需要 Node.js 22 或更新版本。发布包无需安装 Node.js。' }
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'node_modules'))) {
        & npm ci
        if ($LASTEXITCODE -ne 0) { throw '依赖安装失败' }
    }
    & npm test
    if ($LASTEXITCODE -ne 0) { throw '核心测试失败' }
    if (-not $SkipUiTests) {
        & npm run test:ui
        if ($LASTEXITCODE -ne 0) { throw 'Electron 界面检查失败' }
    }
    & npm run dist
    if ($LASTEXITCODE -ne 0) { throw 'Electron 打包失败' }
    Write-Host '已生成 dist\electron\CodexMonitor-1.1.0-Windows.exe 和 dist\electron\win-unpacked。'
} finally { Pop-Location }
