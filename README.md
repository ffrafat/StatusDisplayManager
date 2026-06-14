# AX206 SmartCool Display Manager

A pure Node.js and Electron GUI application to drive the AX206 320x240 LCD display (VID: `1908`, PID: `0102`). This project has been fully decoupled from the legacy Python script, making it self-contained, lightweight, and modern. It provides dynamic dashboard views, automated hardware driver controls, and local service quota monitoring.

---

## Architecture & Project Structure

The project is structured logically into separate backend, frontend, and utility modules:

- **[main.js](file:///c:/ax206-display/ax206-node-gui/main.js)**: The Electron main process. Creates the dashboard browser window, coordinates background timers, polls system/API telemetry, auto-connects to the USB screen, and acts as the IPC bridge.
- **[driver.js](file:///c:/ax206-display/ax206-node-gui/driver.js)**: Hardware driver implementing the USB Bulk-Only Transport (BOT) protocol. Handles endpoint claims, packet wrapping (CBW/CSW), and converting HTML5 Canvas 32-bit RGBA buffers to 16-bit big-endian RGB565 format.
- **[stats.js](file:///c:/ax206-display/ax206-node-gui/stats.js)**: Standard system resource monitor. Gathers real-time CPU load, memory utilization, disk usage, and network RX/TX bandwidth bytes.
- **[media.js](file:///c:/ax206-display/ax206-node-gui/media.js)**: Integrates native OS scripting (such as Windows PowerShell WinRT, macOS AppleScript, or Linux `playerctl` and `xdotool`) to extract active foreground window titles and current music player status.
- **[providers.js](file:///c:/ax206-display/ax206-node-gui/providers.js)**: Scrapers for local development workflows:
  - **Claude Code**: Safely checks `~/.claude/.credentials.json` to extract current token quota usage.
  - **Antigravity**: Audits local processes to find running Antigravity Language Servers, checks CSRF flags, and checks active ports to monitor Gemini and Claude token quotas.
- **[index.html](file:///c:/ax206-display/ax206-node-gui/index.html)**: The dashboard layout, designed with inline Lucide vector SVGs, telemetry cards, and connection configuration toggles.
- **[style.css](file:///c:/ax206-display/ax206-node-gui/style.css)**: Glassmorphic dark theme stylesheet. Coordinates layouts, states, micro-animations, and SVG icon sizing.
- **[renderer.js](file:///c:/ax206-display/ax206-node-gui/renderer.js)**: Runs on the Electron frontend. Renders dashboard telemetry readouts, manages client settings in local storage, handles screen rotations, and performs 2x supersampled drawing on an offscreen canvas (960x640) for anti-aliasing before sending frame data to the USB driver.

---

## How the USB Driver Works (BOT Protocol)

The AX206 display runs a customized mass storage firmware that listens to SCSI command blocks on raw USB endpoints.

### 1. Endpoint Map
- **Out Endpoint**: `0x01` (Bulk Transfer)
- **In Endpoint**: `0x81` (Bulk Transfer)

### 2. CBW & CSW Wrapping
Every command sent to the display must be wrapped in a 31-byte **Command Block Wrapper (CBW)** structure, and must wait for a 13-byte **Command Status Wrapper (CSW)** block back from the display:

* **CBW Structure (31 bytes)**:
  - `USBC` (4 bytes): Signature `0x43425355`
  - Tag (4 bytes): Transaction identifier (we use `0xdeadbeef`)
  - Data Length (4 bytes): Size of payload to be transferred
  - Direction (1 byte): `0x00` (Host-to-Device) or `0x80` (Device-to-Host)
  - LUN (1 byte): Logical unit number (`0x00`)
  - CDB Length (1 byte): Command descriptor block length (usually `0x10`)
  - CDB (16 bytes): Actual command payload (e.g. initialize, set cursor, write buffer)

* **CSW Structure (13 bytes)**:
  - `USBS` (4 bytes): Signature `0x53425355`
  - Tag (4 bytes): Transaction identifier (matches the CBW tag)
  - Residue (4 bytes): Bytes left untransferred
  - Status (1 byte): `0x00` indicates success, `0x01` / `0x02` indicates a phase/hardware error.

### 3. Image Packing (RGB565 BE)
The display expects pixel colors in 16-bit Big-Endian RGB565 format (5 bits red, 6 bits green, 5 bits blue). The driver converts canvas pixels in a fast loop:
```javascript
const rgb565 = ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3);
outBuf[i * 2] = (rgb565 >> 8) & 0xFF; // High byte
outBuf[i * 2 + 1] = rgb565 & 0xFF;    // Low byte
```

---

## Setup & Prerequisites

This application runs on Node.js. Because it communicates directly with USB hardware via node-usb, some platform-specific drivers are needed.

### Windows Setup
1. **USB Driver**: The AX206 screen must be associated with the WinUSB driver. Download [Zadig](https://zadig.akeo.ie/), select your AX206 device (usually identified as a bulk storage device or custom display), and install/replace the driver with **WinUSB**.
2. **Node.js**: Install Node.js 18 or later.

### Linux / Raspberry Pi Setup
1. **libusb**: Install the libusb library dependencies:
   ```bash
   sudo apt-get install libusb-1.0-0-dev udev
   ```
2. **udev Rules**: To access the USB device without root permissions, create a udev rule at `/etc/udev/rules.d/99-ax206.rules`:
   ```text
   SUBSYSTEM=="usb", ATTR{idVendor}=="1908", ATTR{idProduct}=="0102", MODE="0666", GROUP="plugdev"
   ```
   Reload udev rules:
   ```bash
   sudo udevadm control --reload-rules && sudo udevadm trigger
   ```

---

## Running the Application

### 1. Install Dependencies
Run in your project directory:
```bash
npm install
```

### 2. Start the Application
Run:
```bash
npm start
```
This launches the Electron GUI dashboard. The manager will automatically look for connected AX206 screens, initialize them, and start rotating through active telemetry panels.

---

## Troubleshooting & Maintenance

- **"AX206 display not found"**:
  - Check the physical USB connection.
  - Verify that the Zadig WinUSB driver is successfully installed (on Windows) or that the udev rules are loaded (on Linux).
- **USB Write Collisions / Blit Stalls**:
  - The driver contains an `isWriting` guard in `main.js` to prevent overlapping frame writes.
  - If a USB transmission fails, the driver executes a Bulk-Only Transport (BOT) reset to clear the endpoints and attempts recovery automatically.
- **Claude or Antigravity Quotas not updating**:
  - Ensure the local Antigravity Language Server is running on your machine.
  - Check that the Claude Code CLI tool is authenticated and that `~/.claude/.credentials.json` exists.
