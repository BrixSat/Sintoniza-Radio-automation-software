; Windows installer for Sintoniza (Emi, Pro, Rec and Import). Built by build.sh with:
;   makensis -DVERSION=<version> -DSRCDIR=<publish dir> -DOUTFILE=<setup.exe> installer.nsi
Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"

!define APPNAME "Sintoniza Emi" ; the on-air program
!define OLDNAME "Radio Playout" ; shortcuts made by versions before the rename
!define OLDAPPNAME "Sintoniza Radio Broadcast system" ; its name before Sintoniza Emi
!define EXENAME "SintonizaEmi.exe"
!define OLDEXE "RadioPlayout" ; its executable before (.exe, .dll…)
!define RECNAME "Sintoniza Rec" ; the recorder: its own exe and icon
!define RECEXE "SintonizaRec.exe"
!define SCHEDNAME "Sintoniza Pro" ; the production program: its own exe (SintonizaPro.exe) and icon
!define SCHEDEXE "SintonizaPro.exe"
!define OLDSCHEDNAME "Sintoniza Schedule" ; its names before
!define OLDSCHEDEXE "SintonizaSchedule.exe"
!define OLDPRODNAME "Sintoniza Production"
!define OLDPRODEXE "SintonizaProduction.exe"
!define IMPORTNAME "Sintoniza Import" ; the import program: the same exe with --import, its own icon
!define PLAYLISTSNAME "Sintoniza Playlists" ; shortcuts of versions that had it apart (now in Sintoniza Pro)
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\RadioPlayout"

Name "${APPNAME}"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\${OLDNAME}" ; kept, so updates install over the same folder
InstallDirRegKey HKLM "Software\RadioPlayout" "InstallDir"
RequestExecutionLevel admin
SetCompressor /SOLID lzma

!define MUI_ICON "${__FILEDIR__}/../../src/Playout.App/Assets/icon.ico"
!define MUI_UNICON "${__FILEDIR__}/../../src/Playout.App/Assets/icon.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXENAME}"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "Install"
  SetShellVarContext all
  ; An in-app update starts this silently and then closes the app: wait (up to 30 s) until its files are free
  ; (the app as it is now, or under its name before when updating from an older version).
  StrCpy $1 0
  wait:
    IfFileExists "$INSTDIR\${EXENAME}" 0 waitold
    ClearErrors
    FileOpen $0 "$INSTDIR\${EXENAME}" a
    IfErrors 0 opened
    Goto busy
  waitold:
    IfFileExists "$INSTDIR\${OLDEXE}.exe" 0 ready
    ClearErrors
    FileOpen $0 "$INSTDIR\${OLDEXE}.exe" a
    IfErrors 0 opened
  busy:
    IntOp $1 $1 + 1
    IntCmp $1 60 ready
    Sleep 500
    Goto wait
  opened:
    FileClose $0
  ready:

  SetOutPath "$INSTDIR"
  File /r "${SRCDIR}/*.*"
  File "${__FILEDIR__}/../../src/Playout.App/Assets/schedule.ico"
  File "${__FILEDIR__}/../../src/Playout.App/Assets/import.ico"

  Delete "$SMPROGRAMS\${OLDNAME}.lnk"
  Delete "$DESKTOP\${OLDNAME}.lnk"
  ; Renamed to Sintoniza Emi: the old executable and its shortcuts go.
  Delete "$SMPROGRAMS\${OLDAPPNAME}.lnk"
  Delete "$DESKTOP\${OLDAPPNAME}.lnk"
  Delete "$INSTDIR\${OLDEXE}.exe"
  Delete "$INSTDIR\${OLDEXE}.dll"
  Delete "$INSTDIR\${OLDEXE}.deps.json"
  Delete "$INSTDIR\${OLDEXE}.runtimeconfig.json"
  ; Starting at login, set by an older version: now the new executable.
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "RadioPlayout"
  StrCmp $0 "" +2
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "RadioPlayout" '"$INSTDIR\${EXENAME}"'
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortcut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"
  CreateShortcut "$SMPROGRAMS\${SCHEDNAME}.lnk" "$INSTDIR\${SCHEDEXE}"
  CreateShortcut "$DESKTOP\${SCHEDNAME}.lnk" "$INSTDIR\${SCHEDEXE}"
  CreateShortcut "$SMPROGRAMS\${RECNAME}.lnk" "$INSTDIR\${RECEXE}"
  CreateShortcut "$DESKTOP\${RECNAME}.lnk" "$INSTDIR\${RECEXE}"
  CreateShortcut "$SMPROGRAMS\${IMPORTNAME}.lnk" "$INSTDIR\${EXENAME}" "--import" "$INSTDIR\import.ico" 0
  CreateShortcut "$DESKTOP\${IMPORTNAME}.lnk" "$INSTDIR\${EXENAME}" "--import" "$INSTDIR\import.ico" 0
  ; The user manual (PDF), in English and in Portuguese.
  IfFileExists "$INSTDIR\Sintoniza-Manual.pdf" 0 +2
    CreateShortcut "$SMPROGRAMS\Sintoniza Manual (English).lnk" "$INSTDIR\Sintoniza-Manual.pdf"
  IfFileExists "$INSTDIR\Sintoniza-Manual-PT.pdf" 0 +2
    CreateShortcut "$SMPROGRAMS\Manual do Sintoniza (Português).lnk" "$INSTDIR\Sintoniza-Manual-PT.pdf"
  Delete "$SMPROGRAMS\${PLAYLISTSNAME}.lnk"
  Delete "$DESKTOP\${PLAYLISTSNAME}.lnk"
  Delete "$SMPROGRAMS\${OLDSCHEDNAME}.lnk"
  Delete "$DESKTOP\${OLDSCHEDNAME}.lnk"
  Delete "$INSTDIR\${OLDSCHEDEXE}"
  Delete "$SMPROGRAMS\${OLDPRODNAME}.lnk"
  Delete "$DESKTOP\${OLDPRODNAME}.lnk"
  Delete "$INSTDIR\${OLDPRODEXE}"

  WriteRegStr HKLM "Software\RadioPlayout" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${EXENAME}"
  WriteRegStr HKLM "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTKEY}" "Publisher" "César Araújo"
  WriteRegStr HKLM "${UNINSTKEY}" "URLInfoAbout" "https://cesararaujo.net"
  WriteRegStr HKLM "${UNINSTKEY}" "HelpLink" "mailto:cesarsilvaaraujo@gmail.com"
  WriteRegStr HKLM "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoRepair" 1

  ; In-app update (/S /RESTART): start the new version.
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/RESTART" $1
  IfErrors +2
  Exec '"$INSTDIR\${EXENAME}"'
SectionEnd

Section "Uninstall"
  SetShellVarContext all
  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  Delete "$DESKTOP\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${SCHEDNAME}.lnk"
  Delete "$DESKTOP\${SCHEDNAME}.lnk"
  Delete "$SMPROGRAMS\${RECNAME}.lnk"
  Delete "$DESKTOP\${RECNAME}.lnk"
  Delete "$SMPROGRAMS\${OLDAPPNAME}.lnk"
  Delete "$DESKTOP\${OLDAPPNAME}.lnk"
  Delete "$SMPROGRAMS\${IMPORTNAME}.lnk"
  Delete "$DESKTOP\${IMPORTNAME}.lnk"
  Delete "$SMPROGRAMS\${PLAYLISTSNAME}.lnk"
  Delete "$DESKTOP\${PLAYLISTSNAME}.lnk"
  Delete "$SMPROGRAMS\${OLDSCHEDNAME}.lnk"
  Delete "$DESKTOP\${OLDSCHEDNAME}.lnk"
  Delete "$SMPROGRAMS\${OLDPRODNAME}.lnk"
  Delete "$DESKTOP\${OLDPRODNAME}.lnk"
  Delete "$SMPROGRAMS\Sintoniza Manual (English).lnk"
  Delete "$SMPROGRAMS\Manual do Sintoniza (Português).lnk"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKLM "${UNINSTKEY}"
  DeleteRegKey HKLM "Software\RadioPlayout"
  ; Settings in %APPDATA%\RadioPlayout are left in place.
SectionEnd
