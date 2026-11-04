; Windows installer for Sintoniza (Emi, Pro, Rec, Tempo and Import). Built by build.sh with:
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
!define TEMPONAME "Sintoniza Tempo" ; the studio clock: its own exe and icon
!define TEMPOEXE "SintonizaTempo.exe"
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
  ; An in-app update starts this silently and then closes the app: its files come free in a few seconds (the app as
  ; it is now, or under its name before when updating from an older version). SintonizaEmi.dll is loaded by every
  ; program (Emi, Pro, Rec, Tempo, Import): free only when they are all closed.
  StrCpy $1 0
  StrCpy $3 0 ; Sintoniza Rec was closed here: started again after
  StrCpy $4 0 ; Sintoniza Emi was closed here: started again after
  StrCpy $5 0 ; Sintoniza Tempo was closed here: started again after
  wait:
    IfFileExists "$INSTDIR\SintonizaEmi.dll" 0 waitexe
    ClearErrors
    FileOpen $0 "$INSTDIR\SintonizaEmi.dll" a
    IfErrors busy
    FileClose $0
  waitexe:
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
    ; After 5 s, whatever of Sintoniza is still open is closed, and again every half second until its files are free:
    ; Emi with the supervisor that starts it again after a crash (the same program: otherwise it would come straight
    ; back and the files would never be free), Import (also the same program), Pro, Rec and Tempo. Emi, Rec and Tempo
    ; are started again after the update.
    IntCmp $1 10 close notyet close
  close:
    nsExec::Exec 'taskkill /F /T /IM ${EXENAME}'
    Pop $0
    StrCmp $0 "0" 0 +2
    StrCpy $4 1
    nsExec::Exec 'taskkill /F /T /IM ${OLDEXE}.exe'
    Pop $0
    nsExec::Exec 'taskkill /F /IM ${SCHEDEXE}'
    Pop $0
    nsExec::Exec 'taskkill /F /IM ${OLDSCHEDEXE}'
    Pop $0
    nsExec::Exec 'taskkill /F /IM ${OLDPRODEXE}'
    Pop $0
    nsExec::Exec 'taskkill /F /IM ${RECEXE}'
    Pop $0
    StrCmp $0 "0" 0 +2
    StrCpy $3 1
    nsExec::Exec 'taskkill /F /IM ${TEMPOEXE}'
    Pop $0
    StrCmp $0 "0" 0 +2
    StrCpy $5 1
  notyet:
    IntCmp $1 120 ready
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
  CreateShortcut "$SMPROGRAMS\${TEMPONAME}.lnk" "$INSTDIR\${TEMPOEXE}"
  CreateShortcut "$DESKTOP\${TEMPONAME}.lnk" "$INSTDIR\${TEMPOEXE}"
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

  ; In-app update (/S /RESTART), or Emi was running and closed for the update: start the new version, so the
  ; station is on air again.
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/RESTART" $1
  IfErrors 0 restart
  StrCmp $4 "1" restart norestart
  restart:
    Exec '"$INSTDIR\${EXENAME}"'
  norestart:
  StrCmp $3 "1" 0 +2
  Exec '"$INSTDIR\${RECEXE}"'
  StrCmp $5 "1" 0 +2
  Exec '"$INSTDIR\${TEMPOEXE}"'
SectionEnd

Section "Uninstall"
  SetShellVarContext all
  ; Everything of Sintoniza closed first (Emi with its supervisor, Import, Pro, Rec, Tempo), or its files would stay behind.
  nsExec::Exec 'taskkill /F /T /IM ${EXENAME}'
  Pop $0
  nsExec::Exec 'taskkill /F /IM ${SCHEDEXE}'
  Pop $0
  nsExec::Exec 'taskkill /F /IM ${RECEXE}'
  Pop $0
  nsExec::Exec 'taskkill /F /IM ${TEMPOEXE}'
  Pop $0
  Sleep 1000
  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  Delete "$DESKTOP\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${SCHEDNAME}.lnk"
  Delete "$DESKTOP\${SCHEDNAME}.lnk"
  Delete "$SMPROGRAMS\${RECNAME}.lnk"
  Delete "$DESKTOP\${RECNAME}.lnk"
  Delete "$SMPROGRAMS\${TEMPONAME}.lnk"
  Delete "$DESKTOP\${TEMPONAME}.lnk"
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
