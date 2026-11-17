# AX206 Display Manager

Turns an AX206 USB LCD (VID `1908`, PID `0102`, 480×320) into a status display that rotates through screens. There are two native apps, one per platform, sharing the same USB protocol:

| Folder | Platform | What it is |
| --- | --- | --- |
| [`windows/`](windows/README.md) | Windows 10/11 | WinUI 3 + .NET 10 app with nine screens (clock, stats, now playing, Claude usage, weather, radar and more), tray icon, installer |
| [`mac/`](mac/README.md) | macOS 14+, Apple Silicon | Swift menu bar app with a clock face and a system dashboard (CPU, GPU, memory, power, fans, network, disk) |

## How the display protocol works

The AX206 runs mass-storage-style firmware that takes vendor SCSI commands over bulk endpoints `0x01` (out) and `0x81` (in). Every command is a 31-byte Command Block Wrapper (`USBC` signature, tag, data length, direction, LUN 0, CDB), optionally followed by data, and answered by a 13-byte Command Status Wrapper (`USBS`). Frames are sent as 16-bit big-endian RGB565. When a transfer fails the driver does a Bulk-Only Mass Storage Reset, clears both endpoints and retries.

On Windows the app reaches the device through `libusb0.dll` (installed with Zadig). On macOS no driver is needed: the system leaves the display's vendor interface (class `0xDC`) unclaimed, so the app opens it directly with Apple's IOUSBHost framework.

## License

GPL-3.0
