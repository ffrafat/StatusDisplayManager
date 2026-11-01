# AX206 Display Manager - Tasks

## Long-Term Feature Backlog

- [ ] **External Input Navigation**: Introduce USB keyboard hotkeys, mouse shortcuts, or external input devices (like Arduino macro buttons or macro pads) to manually switch screens and pause/resume rotation.
  - *Implementation note*: Register global hotkeys (or read the serial device) in `AX206.App` and drive the same next / previous / pause commands the Display page buttons use.
