# Monity Release Build Script
# 1. Publish App, 2. Publish Updater, 3. Compile Inno Setup
# Gereksinim: Inno Setup 6 kurulu (https://jrsoftware.org/isinfo.php)

$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot

Write-Host "=== Monity Release Build ===" -ForegroundColor Cyan

# 1. Publish App
Write-Host "`n[1/3] Publishing Monity.App..." -ForegroundColor Yellow
Push-Location $RootDir
dotnet publish src/Monity.App/Monity.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { Pop-Location; exit 1 }
Pop-Location

# 2. Publish Updater
Write-Host "`n[2/3] Publishing Monity.Updater..." -ForegroundColor Yellow
Push-Location $RootDir
dotnet publish src/Monity.Updater/Monity.Updater.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { Pop-Location; exit 1 }
Pop-Location

# 3. Inno Setup
$IsccPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
$Iscc = $null
foreach ($p in $IsccPaths) {
    if (Test-Path $p) { $Iscc = $p; break }
}
if (-not $Iscc) {
    Write-Host "`nHATA: Inno Setup bulunamadi. Lutfen yukleyin: https://jrsoftware.org/isinfo.php" -ForegroundColor Red
    exit 1
}

Write-Host "`n[3/3] Compiling installer..." -ForegroundColor Yellow
& $Iscc "$RootDir\installer\Monity.iss"
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "`n=== Tamamlandi ===" -ForegroundColor Green
Write-Host "Setup: $RootDir\installer\Output\Monity-Setup-1.0.0.exe"
