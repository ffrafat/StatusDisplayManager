; Custom electron-builder NSIS hook for AX206 Display Manager.
;
; The app minimises to the tray on window-close, so NSIS's default
; "please close the running application" prompt can never be satisfied
; by the user clicking the X. Force-terminate the process tree (which
; includes the spawned backend.exe) before installing instead.

!macro customCheckAppRunning
  DetailPrint "Stopping any running AX206 Display Manager instance..."
  nsExec::Exec 'taskkill /F /T /IM "${APP_EXECUTABLE_FILENAME}"'
  Pop $0
  Sleep 1200
!macroend
