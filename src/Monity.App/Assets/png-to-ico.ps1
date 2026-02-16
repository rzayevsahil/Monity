Add-Type -AssemblyName System.Drawing
$pngPath = Join-Path $PSScriptRoot "Logo.png"
$icoPath = Join-Path $PSScriptRoot "AppIcon.ico"
$img = [System.Drawing.Bitmap]::FromFile($pngPath)
$size = [Math]::Min(256, [Math]::Min($img.Width, $img.Height))
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($img, 0, 0, $size, $size)
$g.Dispose()
$img.Dispose()
$icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
$fs = [System.IO.File]::Create($icoPath)
$icon.Save($fs)
$fs.Close()
$icon.Dispose()
$bmp.Dispose()
Write-Host "Created AppIcon.ico"
