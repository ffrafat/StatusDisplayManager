; Installer for the native AX206 Display Manager. Built by native/build-installer.ps1, which
; passes VERSION, VERSION_NUMERIC, PUBLISH_DIR, ICON and OUTFILE.
;
; Per-user install (no admin prompt). The Electron version used the same product name, so
; replacing it is an option here: only one app can drive the display at a time.

Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

!define APPNAME "AX206 Display Manager"
!define EXE "AX206DisplayManager.exe"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\AX206DisplayManager.Native"
!define RUNKEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define RUNVALUE "AX206 Display Manager (native)"

; What the Electron version leaves behind
!define ELECTRON_EXE "AX206 Display Manager.exe"
!define ELECTRON_UNINSTALLER "Uninstall AX206 Display Manager.exe"

Name "${APPNAME}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\AX206DisplayManager"
InstallDirRegKey HKCU "${UNINSTKEY}" "InstallLocation" ; upgrades go where the last install went
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "${APPNAME} ${VERSION}"

VIProductVersion "${VERSION_NUMERIC}"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "${APPNAME} setup"
VIAddVersionKey "LegalCopyright" "GPL-3.0"

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!define MUI_COMPONENTSPAGE_SMALLDESC
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Start ${APPNAME}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "${APPNAME}" SecApp
  SectionIn RO

  ; Replace a running copy (an upgrade); it keeps no unsaved state.
  nsExec::Exec 'taskkill /F /IM "${EXE}"'
  Sleep 1000

  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortcut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0

  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "Faisal F Rafat"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "URLInfoAbout" "https://github.com/ffrafat/StatusDisplayManager"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" "$0"
SectionEnd

Section "Start with Windows" SecStartup
  WriteRegStr HKCU "${RUNKEY}" "${RUNVALUE}" '"$INSTDIR\${EXE}" --hidden'
SectionEnd

Section /o "Desktop shortcut" SecDesktop
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0
SectionEnd

Section "Replace the Electron version" SecReplace
  ; Stop it (it holds the USB display) and stop it launching at sign-in. Its files stay.
  nsExec::Exec 'taskkill /F /T /IM "${ELECTRON_EXE}"'
  DeleteRegValue HKCU "${RUNKEY}" "electron.app.AX206 Display Manager"
  DeleteRegValue HKCU "${RUNKEY}" "com.ax206.displaymanager"
SectionEnd

Section /o "Uninstall the Electron version" SecUninstallElectron
  ; Runs its own uninstaller (it was installed for all users, so Windows asks for permission).
  ${If} ${FileExists} "$PROGRAMFILES64\AX206 Display Manager\${ELECTRON_UNINSTALLER}"
    ExecWait '"$PROGRAMFILES64\AX206 Display Manager\${ELECTRON_UNINSTALLER}"'
  ${ElseIf} ${FileExists} "$LOCALAPPDATA\Programs\AX206 Display Manager\${ELECTRON_UNINSTALLER}"
    ExecWait '"$LOCALAPPDATA\Programs\AX206 Display Manager\${ELECTRON_UNINSTALLER}"'
  ${EndIf}
SectionEnd

; The uninstaller removes $INSTDIR recursively, so only allow a new or empty folder, or one that
; already holds this app (an upgrade). Otherwise picking e.g. D:\ would let uninstall wipe it.
Function .onVerifyInstDir
  ${If} ${FileExists} "$INSTDIR\${EXE}"
    Return
  ${EndIf}
  FindFirst $0 $1 "$INSTDIR\*"
  ${DoWhile} $1 != ""
    ${If} $1 != "."
    ${AndIf} $1 != ".."
      FindClose $0
      Abort
    ${EndIf}
    FindNext $0 $1
  ${Loop}
  FindClose $0
FunctionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecApp} "The app itself, a Start menu shortcut and an entry in Settings > Apps."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStartup} "Start quietly in the notification area when you sign in, so the display is always running."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Add a shortcut to the desktop."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecReplace} "Only one app can drive the display. Stops the old Electron version and turns off its start at sign-in. Its files are left in place."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecUninstallElectron} "Also run the Electron version's uninstaller. Windows will ask for permission."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  nsExec::Exec 'taskkill /F /IM "${EXE}"'
  Sleep 1000

  DeleteRegValue HKCU "${RUNKEY}" "${RUNVALUE}"
  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  Delete "$DESKTOP\${APPNAME}.lnk"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${UNINSTKEY}"
  ; Settings and the Claude token in %LOCALAPPDATA%\AX206DisplayManager are kept for a reinstall.
SectionEnd
