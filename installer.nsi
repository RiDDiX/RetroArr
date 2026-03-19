!include "MUI2.nsh"
!include "FileFunc.nsh"

; Define variables
!define APPNAME "RetroArr"
!define COMPANYNAME "RiDDiX"
!define DESCRIPTION "Self-Hosted Game Library Manager & PVR"
!ifndef VERSIONmajor
  !define VERSIONmajor 0
!endif
!ifndef VERSIONminor
  !define VERSIONminor 4
!endif
!ifndef VERSIONbuild
  !define VERSIONbuild 0
!endif
!define HELPURL "https://retroarr.app"
!define UPDATEURL "https://github.com/RiDDiX/RetroArr/releases"
!define ABOUTURL "https://retroarr.app"
!define INSTALLSIZE 125000 

Name "${APPNAME}"
!ifndef OUTFILE
  !define OUTFILE "build_artifacts/RetroArr-Windows-Setup-x64.exe"
!endif
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\${APPNAME}"
InstallDirRegKey HKLM "Software\${APPNAME}" "Install_Dir"

BrandingText "${APPNAME} v${VERSIONmajor}.${VERSIONminor}.${VERSIONbuild}"

; Interface settings
!define MUI_ABORTWARNING
!define MUI_ICON "frontend/src/assets/app_logo.ico"
!define MUI_UNICON "frontend/src/assets/app_logo.ico"

; Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; Languages
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Spanish"

Section "RetroArr (required)" SecDummy
    SectionIn RO
    
    SetOutPath "$INSTDIR"
    
    ; Copy files from build output
    File /r "build_artifacts/win-x64/*.*"
    
    ; Write Uninstaller
    WriteUninstaller "$INSTDIR\uninstall.exe"
    
    ; Registry keys for Add/Remove Programs
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" "$INSTDIR\uninstall.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "QuietUninstallString" "$INSTDIR\uninstall.exe /S"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayIcon" "$INSTDIR\RetroArr.Host.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "Publisher" "${COMPANYNAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "HelpLink" "${HELPURL}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "URLUpdateInfo" "${UPDATEURL}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "URLInfoAbout" "${ABOUTURL}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "${VERSIONmajor}.${VERSIONminor}.${VERSIONbuild}"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoRepair" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "EstimatedSize" ${INSTALLSIZE}

    ; Explicitly copy the icon file
    File "frontend/src/assets/app_logo.ico"

    ; Create Start Menu Shortcuts
    CreateDirectory "$SMPROGRAMS\${APPNAME}"
    ; Point shortcut to the icon file explicitly
    CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\RetroArr.Host.exe" "" "$INSTDIR\app_logo.ico" 0
    CreateShortCut "$SMPROGRAMS\${APPNAME}\Uninstall.lnk" "$INSTDIR\uninstall.exe"
SectionEnd

Section "Uninstall"
    ; Remove files
    RMDir /r "$INSTDIR"
    
    ; Remove Start Menu Shortcuts
    RMDir /r "$SMPROGRAMS\${APPNAME}"
    
    ; Remove Registry Keys
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
    DeleteRegKey HKLM "Software\${APPNAME}"
SectionEnd
