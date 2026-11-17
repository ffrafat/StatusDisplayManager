# AX206 Display Manager for Windows

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
# Debug build; run windows\AX206.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\AX206DisplayManager.exe
dotnet build windows\AX206.App\AX206.App.csproj -p:Platform=x64

# Self-contained release + installer -> windows\dist\
powershell -ExecutionPolicy Bypass -File windows\build-installer.ps1

# Regenerate icon.png / icon.ico after editing windows\assets\icon.svg (uses Edge to render)
powershell -ExecutionPolicy Bypass -File windows\tools\make-icon.ps1
```

## Layout

| Path (under `windows/`) | What it is |
| --- | --- |
| `AX206.Core` | USB driver, SkiaSharp renderer, screens, data sources, settings |
| `AX206.App` | WinUI 3 app: pages, tray icon, start-at-sign-in |
| `AX206.UsbTest` | Dev console: colour bars and timing on the device, or `render DIR` to dump every screen to PNG |
| `installer` | NSIS script used by `build-installer.ps1` |
| `assets` | App icon (SVG source plus generated ICO/PNG) |

The display protocol is described in the [top-level README](../README.md#how-the-display-protocol-works).
