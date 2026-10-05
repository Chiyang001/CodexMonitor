$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows 自带的 .NET Framework 4.x。' }
$outputDir = Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$iconDir = Join-Path $taskRoot 'assets'
New-Item -ItemType Directory -Force -Path $iconDir | Out-Null
$iconBuilder = Join-Path $outputDir 'CodexMonitor.IconBuilder.exe'
$iconFile = Join-Path $iconDir 'CodexMonitor.ico'
& $compiler /nologo /target:winexe "/out:$iconBuilder" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $sourceFiles
if ($LASTEXITCODE -ne 0) { throw '图标生成工具编译失败' }
$iconProcess = Start-Process -FilePath $iconBuilder -ArgumentList @('--export-icon', ('"' + $iconFile + '"')) -WindowStyle Hidden -PassThru
if (-not $iconProcess.WaitForExit(10000) -or $iconProcess.ExitCode -ne 0) { throw '图标生成失败' }
Remove-Item -LiteralPath $iconBuilder -Force
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/win32manifest:$taskRoot\app.manifest" "/win32icon:$iconFile" "/out:$outputDir\CodexMonitor.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $sourceFiles
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
& $compiler /nologo /target:exe "/out:$outputDir\CodexMonitor.Tests.exe" "/reference:$outputDir\CodexMonitor.exe" /reference:System.Web.Extensions.dll (Join-Path $taskRoot 'tests\Tests.cs')
if ($LASTEXITCODE -ne 0) { throw '测试编译失败' }
& (Join-Path $outputDir 'CodexMonitor.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw '测试失败' }
Write-Host "已生成 $outputDir\CodexMonitor.exe"
