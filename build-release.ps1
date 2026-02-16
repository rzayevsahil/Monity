# Monity Release Build Script
# 1. Publish App, 2. Publish Updater, 3. Updater.exe -> App publish, 4. Inno Setup
# Sürüm tek yerde: src/Monity.App/Monity.App.csproj -> <Version>
# Gereksinim: Inno Setup 6 kurulu (https://jrsoftware.org/isinfo.php)

$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot

# Sürümü csproj'dan oku (tek kaynak)
$csprojPath = Join-Path $RootDir "src\Monity.App\Monity.App.csproj"
$csprojXml = [xml](Get-Content $csprojPath -Encoding UTF8)
$Version = $csprojXml.Project.PropertyGroup.Version
if (-not $Version) {
    Write-Host "HATA: Monity.App.csproj icinde <Version> bulunamadi." -ForegroundColor Red
    exit 1
}

Write-Host "=== Monity Release Build (v$Version) ===" -ForegroundColor Cyan

# 1. Publish App
Write-Host "`n[1/4] Publishing Monity.App..." -ForegroundColor Yellow
Push-Location $RootDir
dotnet publish src/Monity.App/Monity.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { Pop-Location; exit 1 }
Pop-Location

# 2. Publish Updater
Write-Host "`n[2/4] Publishing Monity.Updater..." -ForegroundColor Yellow
Push-Location $RootDir
dotnet publish src/Monity.Updater/Monity.Updater.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { Pop-Location; exit 1 }
Pop-Location

# 3. Updater.exe'yi App publish klasorune kopyala (zip ve kurulum icin)
$AppPublishDir = Join-Path $RootDir "src\Monity.App\bin\Release\net8.0-windows\win-x64\publish"
$UpdaterExeSrc = Join-Path $RootDir "src\Monity.Updater\bin\Release\net8.0\win-x64\publish\Monity.Updater.exe"
$UpdaterExeDst = Join-Path $AppPublishDir "Updater.exe"

Write-Host "`n[3/4] Updater.exe -> App publish klasorune kopyalaniyor..." -ForegroundColor Yellow
if (-not (Test-Path $UpdaterExeSrc)) {
    Write-Host "HATA: Monity.Updater.exe bulunamadi: $UpdaterExeSrc" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $AppPublishDir)) {
    Write-Host "HATA: App publish klasoru bulunamadi: $AppPublishDir" -ForegroundColor Red
    exit 1
}
Copy-Item -Path $UpdaterExeSrc -Destination $UpdaterExeDst -Force
Write-Host "  -> $UpdaterExeDst" -ForegroundColor Gray

# 4. Inno Setup
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

Write-Host "`n[4/4] Compiling installer..." -ForegroundColor Yellow
& $Iscc "/DMyAppVersion=$Version" "$RootDir\installer\Monity.iss"
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "`n=== Tamamlandi ===" -ForegroundColor Green
Write-Host "Setup: $RootDir\installer\Output\Monity-Setup-$Version.exe"
Write-Host "Zip icin: $AppPublishDir icindekileri zip'leyip Monity-$Version-win-x64.zip adiyla release'e ekleyin (Updater.exe zaten icinde)."
