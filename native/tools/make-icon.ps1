# Regenerates native\assets\icon.png and icon.ico from native\assets\icon.svg.
#   powershell -ExecutionPolicy Bypass -File native\tools\make-icon.ps1
# Edge (always installed on Windows 11) rasterises the SVG; each .ico frame is scaled from that.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\assets" | Resolve-Path
$svg    = Join-Path $assets "icon.svg"
$png    = Join-Path $assets "icon.png"
$ico    = Join-Path $assets "icon.ico"
$sizes  = 16, 20, 24, 32, 40, 48, 64, 128, 256

$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
          "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw "Microsoft Edge not found" }

# Render at 1024 px on a transparent page.
$work = Join-Path ([IO.Path]::GetTempPath()) "ax206-make-icon"
New-Item -ItemType Directory -Force $work | Out-Null
$page = Join-Path $work "icon.html"
$svgUri = ([Uri]$svg).AbsoluteUri
"<html><body style='margin:0;background:transparent'><img src='$svgUri' width='1024' height='1024'></body></html>" |
    Set-Content -Encoding utf8 $page
$shot = Join-Path $work "icon-1024.png"
if (Test-Path $shot) { Remove-Item $shot }
# Edge reports "bytes written" on stderr, which Windows PowerShell would otherwise treat as an error.
$ErrorActionPreference = "Continue"
& $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 `
    --default-background-color=00000000 --window-size=1024,1024 "--user-data-dir=$work\profile" `
    "--screenshot=$shot" ([Uri]$page).AbsoluteUri 2>&1 | Out-Null
$ErrorActionPreference = "Stop"
for ($i = 0; -not (Test-Path $shot) -and $i -lt 50; $i++) { Start-Sleep -Milliseconds 200 }
if (-not (Test-Path $shot)) { throw "Edge did not write a screenshot" }
Copy-Item $shot $png -Force

function Get-ScaledPng([System.Drawing.Image]$source, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

# .ico with PNG-compressed frames (supported since Windows Vista).
$source = [System.Drawing.Image]::FromFile($shot)
$frames = foreach ($s in $sizes) { ,(Get-ScaledPng $source $s) }
$source.Dispose()

$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $frames[$i]
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256))   # 256 is stored as 0
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $frames) { $w.Write($data) }
$w.Flush()
[IO.File]::WriteAllBytes($ico, $out.ToArray())
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Wrote $png and $ico ($($sizes -join ', ') px)"
