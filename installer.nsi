; ==============================================================================
; NSIS Script: NetToGXSim3 Installer
; Version: 0.5.0
; Developed by: Ismail Lowkey
; ==============================================================================

!include "MUI2.nsh"
!include "FileFunc.nsh"

; --------------------------------------------------
; General Definitions
; --------------------------------------------------
!define PRODUCT_NAME "NetToGXSim3 by Ismail Lowkey"
!define PRODUCT_SHORT_NAME "NetToGXSim3"
!define PRODUCT_VERSION "0.5.0"
!define PRODUCT_PUBLISHER "Ismail Lowkey"
!define MAIN_EXE "NetToGXSim3.Wpf.exe"
!define REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\GX3Bridge"

Name "${PRODUCT_NAME}"
OutFile "Setup_NetToGXSim3_v${PRODUCT_VERSION}.exe"
InstallDir "$PROGRAMFILES32\MELSOFT\NetToGXSim3"
InstallDirRegKey HKLM "${REG_KEY}" "InstallLocation"
RequestExecutionLevel admin
Unicode True
SetCompressor /SOLID lzma

; --------------------------------------------------
; Interface Settings & Branding
; --------------------------------------------------
!define MUI_ABORTWARNING
!define MUI_ICON "src\NetToGXSim3.Wpf\Resources\app_icon.ico"
!define MUI_UNICON "src\NetToGXSim3.Wpf\Resources\app_icon.ico"
BrandingText "${PRODUCT_NAME} v${PRODUCT_VERSION}"

; --------------------------------------------------
; Installer Pages
; --------------------------------------------------
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${MAIN_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${PRODUCT_SHORT_NAME} now"
!insertmacro MUI_PAGE_FINISH

; --------------------------------------------------
; Uninstaller Pages
; --------------------------------------------------
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

; --------------------------------------------------
; Languages
; --------------------------------------------------
!insertmacro MUI_LANGUAGE "English"

; --------------------------------------------------
; Installer Section
; --------------------------------------------------
Section "MainSection" SEC01
    SetOutPath "$INSTDIR"
    SetOverwrite on

    ; Copy application files from publish directory
    File /r "publish\*.*"

    ; Also copy application icon
    CreateDirectory "$INSTDIR\Resources"
    File /oname=Resources\app_icon.ico "src\NetToGXSim3.Wpf\Resources\app_icon.ico"

    ; Create Uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    ; ----------------------------------------------
    ; Start Menu Shortcuts:
    ; Start Menu -> MELSOFT NetToGXSim -> NetToGXSim3
    ; ----------------------------------------------
    CreateDirectory "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3"
    CreateShortcut "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\NetToGXSim3.lnk" "$INSTDIR\${MAIN_EXE}" "" "$INSTDIR\${MAIN_EXE}" 0
    CreateShortcut "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\Uninstall.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0

    ; Desktop Shortcut
    CreateShortcut "$DESKTOP\NetToGXSim3.lnk" "$INSTDIR\${MAIN_EXE}" "" "$INSTDIR\${MAIN_EXE}" 0

    ; ----------------------------------------------
    ; Registry Entries for Add/Remove Programs
    ; ----------------------------------------------
    WriteRegStr HKLM "${REG_KEY}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr HKLM "${REG_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr HKLM "${REG_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
    WriteRegStr HKLM "${REG_KEY}" "DisplayIcon" "$INSTDIR\${MAIN_EXE},0"
    WriteRegStr HKLM "${REG_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKLM "${REG_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
    WriteRegStr HKLM "${REG_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegDWORD HKLM "${REG_KEY}" "NoModify" 1
    WriteRegDWORD HKLM "${REG_KEY}" "NoRepair" 1

    ; Estimate install size in KB
    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    IntFmt $0 "0x%08X" $0
    WriteRegDWORD HKLM "${REG_KEY}" "EstimatedSize" "$0"
SectionEnd

; --------------------------------------------------
; Uninstaller Section
; --------------------------------------------------
Section "Uninstall"
    ; Terminate running instance if open
    ExecWait 'taskkill /F /IM ${MAIN_EXE}'

    ; Remove Shortcuts
    Delete "$DESKTOP\NetToGXSim3.lnk"
    Delete "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\NetToGXSim3.lnk"
    Delete "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\Uninstall.lnk"
    RMDir "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3"
    RMDir "$SMPROGRAMS\MELSOFT NetToGXSim"

    ; Remove Installed Files
    Delete "$INSTDIR\Resources\app_icon.ico"
    RMDir "$INSTDIR\Resources"
    Delete "$INSTDIR\*.*"
    RMDir "$INSTDIR"

    ; Remove Registry Keys
    DeleteRegKey HKLM "${REG_KEY}"
SectionEnd
