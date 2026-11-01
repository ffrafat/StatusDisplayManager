# Builds the native app as a self-contained release and wraps it in an NSIS installer.
#   powershell -ExecutionPolicy Bypass -File native\build-installer.ps1
# Output: native\dist\AX206-Display-Manager-Setup-<version>.exe

param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"

$root    = $PSScriptRoot
$project = Join-Path $root "AX206.App\AX206.App.csproj"

# Keep build and makensis scratch files on this drive rather than %TEMP% on C: (makensis
# needs room for the whole compressed payload, and C: can be nearly full).
$scratch = Join-Path $root "obj\build-temp"
New-Item -ItemType Directory -Force $scratch | Out-Null
$env:TMP = $scratch
$env:TEMP = $scratch
$publish = Join-Path $root "publish"
$dist    = Join-Path $root "dist"
$icon    = Join-Path $root "assets\icon.ico" | Resolve-Path

# Version comes from the app project; NSIS also needs a purely numeric a.b.c.d form.
[xml]$xml = Get-Content $project
$version = @($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw "No <Version> in $project" }
$numeric = (($version -split '-')[0] + ".0.0.0").Split('.')[0..3] -join '.'

Write-Host "Publishing $version ($Configuration, self-contained win-x64)..."
# A copy started from the publish folder (e.g. for testing) locks its files.
Get-Process AX206DisplayManager -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($publish, [StringComparison]::OrdinalIgnoreCase) } |
    Stop-Process -Force
Start-Sleep -Milliseconds 500
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c $Configuration -r win-x64 --self-contained true -p:Platform=x64 -o $publish -nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# makensis: on PATH, a normal NSIS install, or the copy electron-builder caches (left over from
# the Electron version's builds).
$makensis = @(
    (Get-Command makensis -ErrorAction SilentlyContinue | ForEach-Object { $_.Source }),
    "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
    "$env:ProgramFiles\NSIS\makensis.exe",
    (Get-ChildItem "$env:LOCALAPPDATA\electron-builder\Cache\nsis\nsis-*\makensis.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 | ForEach-Object { $_.FullName })
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $makensis) { throw "makensis.exe not found. Install NSIS (winget install NSIS.NSIS)." }

New-Item -ItemType Directory -Force $dist | Out-Null
$outFile = Join-Path $dist "AX206-Display-Manager-Setup-$version.exe"
Write-Host "Packaging with $makensis..."
& $makensis /V2 "/DVERSION=$version" "/DVERSION_NUMERIC=$numeric" "/DPUBLISH_DIR=$publish" "/DICON=$icon" "/DOUTFILE=$outFile" (Join-Path $root "installer\AX206.nsi")
if ($LASTEXITCODE -ne 0) { throw "makensis failed" }

$size = [math]::Round((Get-Item $outFile).Length / 1MB, 1)
Write-Host "Done: $outFile ($size MB)"
