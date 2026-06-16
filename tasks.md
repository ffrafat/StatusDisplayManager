# AX206 SmartCool Display Manager - Tasks

This document tracks active development tasks, completed refactoring milestones, and long-term feature ideas for the AX206 Display Manager application.

## Active & Completed Tasks

- [x] Consolidate all telemetry and API providers into a unified Python backend (`backend.py`)
- [x] Port the low-level AX206 USB SCSI protocol driver from Node.js (`usb`) to Python (`pyusb`)
- [x] Streamline Electron wrapper to act as a lightweight GUI shell communicating via stdout/stdin JSON
- [x] Resolve winsdk enum mismatches on playback status to support dynamic music screen visibility
- [x] Implement backpressure flow control to synchronize the LCD display and desktop app preview
- [x] Add next, previous, and rotation play/pause navigation controls below the preview panel
- [x] Optimize frame render rate to 1 FPS (1000ms) for enhanced hardware stability and low power consumption
- [x] Add status pills (`PLAYING` and `PAUSED`) below the metadata on the full-screen music player

## Long-Term Feature Backlog

- [ ] **External Input Navigation**: Introduce USB keyboard hotkeys, mouse shortcuts, or external input devices (like Arduino macro buttons or macro pads) to manually switch screens and pause/resume rotation.
  - *Implementation note*: Hook global triggers in the Python backend (e.g., using `keyboard` or serial inputs) and pipe them back to the Electron renderer via `stdout`.
