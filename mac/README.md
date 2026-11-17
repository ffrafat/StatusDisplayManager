# AX206 Display for macOS

A native Swift menu bar app that drives the AX206 USB LCD from a Mac. No drivers, libusb or kernel extensions: it talks to the panel through Apple's IOUSBHost framework.

## Screens

- **Clock**: an analog dial in the style of the macOS Clock widget (orange second hand, city name from your time zone) beside a calendar panel with the weekday, date, digital time, week number and a day-progress bar.
- **System**: a dashboard of widgets for this Mac:
  - CPU load with a 60-second history and a bar per core (E-cores and P-cores), CPU temperature
  - GPU load and temperature
  - Memory used and total, coloured by memory pressure
  - Power: total system watts with history, plus CPU and GPU watts
  - Fans: RPM for each fan, with a bar from its minimum to maximum speed
  - Network down/up rates and free disk space

Screens rotate on a timer (15 s each by default). Turn screens on or off, change durations, pause rotation or jump to a screen from the menu bar icon or the Settings window.

## Build and run

Requires Xcode 16 or later (Swift 6 toolchain) on an Apple Silicon Mac.

```bash
# Build "AX206 Display.app" into mac/dist/
mac/scripts/build-app.sh

# Build it and copy it to /Applications
mac/scripts/build-app.sh --install
```

The app lives in the menu bar (no Dock icon). **Settings…** in its menu has the larger preview, per-screen durations, 24-hour time and **Open at login**.

### Dev tool

`ax206ctl` exercises the pieces without the app. Quit the app first for the commands that use the panel, since only one process can hold the USB interface.

```bash
cd mac
swift run ax206ctl stats              # print one sample of every sensor
swift run ax206ctl smc T              # list SMC keys starting with T (temperatures)
swift run ax206ctl bars               # colour bars on the panel + frame-rate timing
swift run ax206ctl render /tmp/ax206  # write each screen to PNG
swift run ax206ctl show clock 10      # push one screen to the panel for 10 s
```

## Where the numbers come from

| Reading | Source |
| --- | --- |
| CPU load per core | `host_processor_info`; core types from `hw.perflevel*` sysctls |
| Memory | `host_statistics64` (app + wired + compressed, as Activity Monitor), pressure from `kern.memorystatus_level` |
| GPU load | `PerformanceStatistics` → `Device Utilization %` on the IOAccelerator |
| Temperatures | SMC: average of the CPU core sensors (`Tp0x`) and GPU sensors (`Tg0x`) that exist on this Mac |
| Fans | SMC `FNum`, `F<n>Ac` (actual), `F<n>Mn`/`F<n>Mx` (range) |
| System power | SMC `PSTR` (whole machine) |
| CPU power | IOReport Energy Model when its CPU counters advance; otherwise SMC `PHPC` (CPU rail) |
| GPU power | IOReport Energy Model `GPU Energy` |
| Network, disk | `getifaddrs` byte counters on `en*`; volume capacity of `/` |

On macOS 27 the IOReport CPU and DRAM energy counters don't advance for unprivileged readers, which is why CPU power falls back to the SMC. All of these read without root.

## Layout

| Path | What it is |
| --- | --- |
| `Sources/AX206Kit/Usb` | `AX206Device`: IOUSBHost driver (open, blit, recover) |
| `Sources/AX206Kit/Sensors` | SMC reader, IOReport power sampler, `SystemSampler` |
| `Sources/AX206Kit/Rendering` | Theme and widget components; `FrameRenderer` (SwiftUI → 2x render → 480×320 → RGB565) |
| `Sources/AX206Kit/Screens` | Clock and System screens |
| `Sources/AX206Display` | Menu bar app: rotation loop, USB link with auto-reconnect, settings |
| `Sources/ax206ctl` | Dev tool |
| `Sources/CIOReport` | Header for the private `libIOReport` |
| `scripts/build-app.sh` | Builds and ad-hoc signs the `.app` |

The panel is a USB full-speed device, so a full frame takes about 0.4 s and the app sends one frame per second. Frames are drawn slightly ahead of time so the clock's seconds change on the panel when they change on the Mac.
