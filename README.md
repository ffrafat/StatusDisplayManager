# AX206 Display Manager

A native Windows app (WinUI 3, .NET 10) that turns an AX206 USB LCD (VID `1908`, PID `0102`, 480×320) into a status display. It rotates through screens, runs from the notification area, and starts with Windows.

Version 2 replaces the earlier Electron + Python app; that code lives in the git history before the native port.

## Screens

Clock, system stats, now playing (Windows media session), Claude usage, Antigravity quotas, bangla.gov.bd, weather, pixel pet and plane radar. Each can be turned on or off and given its own duration on the Screens page.

## Install

Run `AX206-Display-Manager-Setup-<version>.exe`. It installs per user (no admin prompt) and offers to:

- start with Windows, hidden in the notification area
- add a desktop shortcut
- stop and replace an old Electron install, or run its uninstaller

Settings live in `%LOCALAPPDATA%\AX206DisplayManager` and are kept when you uninstall.

### USB driver

The app talks to the display through `libusb0.dll`. Install it once with [Zadig](https://zadig.akeo.ie/): pick the AX206 device and install the **libusbK** driver, which also puts `libusb0.dll` in System32.

## Build

Requires the .NET 10 SDK. NSIS is only needed for the installer.

```powershell
# Debug build; run native\AX206.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\AX206DisplayManager.exe
dotnet build native\AX206.App\AX206.App.csproj -p:Platform=x64

# Self-contained release + installer -> native\dist\
powershell -ExecutionPolicy Bypass -File native\build-installer.ps1

# Regenerate icon.png / icon.ico after editing native\assets\icon.svg (uses Edge to render)
powershell -ExecutionPolicy Bypass -File native\tools\make-icon.ps1
```

## Layout

| Path | What it is |
| --- | --- |
| `native/AX206.Core` | USB driver, SkiaSharp renderer, screens, data sources, settings |
| `native/AX206.App` | WinUI 3 app: pages, tray icon, start-at-sign-in |
| `native/AX206.UsbTest` | Dev console: colour bars and timing on the device, or `render DIR` to dump every screen to PNG |
| `native/installer` | NSIS script used by `build-installer.ps1` |
| `native/assets` | App icon (SVG source plus generated ICO/PNG) |

## How the display protocol works

The AX206 runs mass-storage-style firmware that takes vendor SCSI commands over bulk endpoints `0x01` (out) and `0x81` (in). Every command is a 31-byte Command Block Wrapper (`USBC` signature, tag, data length, direction, LUN 0, CDB), optionally followed by data, and answered by a 13-byte Command Status Wrapper (`USBS`). Frames are sent as 16-bit big-endian RGB565. When a transfer fails the driver does a Bulk-Only Mass Storage Reset, clears both endpoints and retries.

## License

GPL-3.0
