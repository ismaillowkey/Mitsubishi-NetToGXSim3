; ==============================================================================
; NSIS Script: NetToGXSim3 Installer
; Version: 0.7.1
; Developed by: Ismail Lowkey
; ==============================================================================

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

; --------------------------------------------------
; General Definitions
; --------------------------------------------------
!define PRODUCT_NAME "NetToGXSim3 by Ismail Lowkey"
!define PRODUCT_SHORT_NAME "NetToGXSim3"
!ifndef PRODUCT_VERSION
  !define PRODUCT_VERSION "0.7.1"
!endif
!define PRODUCT_PUBLISHER "Ismail Lowkey"
!define MAIN_EXE "NetToGXSim3.Wpf.exe"
!define REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\GX3Bridge"
!define PRODUCT_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\${MAIN_EXE}"
!define OPC_PROGID "NetToGXSim3.OPCServer.DA"
!define OPC_CLSID "{4A7E0D2C-3F5B-4C91-95E8-8F2B1A6D7C90}"
!define OPC_DESC "NetToGXSim3 Mitsubishi GX Simulator 3 OPC DA Server"

Name "${PRODUCT_NAME}"
OutFile "publish\Setup_NetToGXSim3_v${PRODUCT_VERSION}.exe"
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
; Helper: Auto-Uninstall Old Versions
; --------------------------------------------------
Function AutoUninstallOldVersion
    DetailPrint "Checking for previous installations..."
    
    ; Terminate running instance if open
    ExecWait 'taskkill /F /IM ${MAIN_EXE}' $R0

    ; Check registry for previous installation
    ReadRegStr $R0 HKLM "${REG_KEY}" "UninstallString"
    ${If} $R0 != ""
        DetailPrint "Uninstalling previous version..."
        ReadRegStr $R1 HKLM "${REG_KEY}" "InstallLocation"
        ${If} $R1 == ""
            StrCpy $R1 "$INSTDIR"
        ${EndIf}
        
        ; Execute previous uninstaller silently and wait
        ExecWait '$R0 /S _?=$R1' $R2
        
        ; Clean up previous shortcuts and old directory
        Delete "$DESKTOP\NetToGXSim3.lnk"
        Delete "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\NetToGXSim3.lnk"
        Delete "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\Example Program HMI.lnk"
        Delete "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\Uninstall.lnk"
        RMDir /r "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3"
        RMDir /r "$R1"
    ${EndIf}

    ; Check if target directory already has old files
    ${If} ${FileExists} "$INSTDIR\${MAIN_EXE}"
        DetailPrint "Cleaning up existing installation folder..."
        Delete "$DESKTOP\NetToGXSim3.lnk"
        Delete "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\NetToGXSim3.lnk"
        Delete "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\Example Program HMI.lnk"
        Delete "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\Uninstall.lnk"
        RMDir /r "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3"
        RMDir /r "$INSTDIR"
    ${EndIf}
FunctionEnd

; --------------------------------------------------
; Installer Section
; --------------------------------------------------
Section "MainSection" SEC01
    ; Uninstall/clean previous version first
    Call AutoUninstallOldVersion

    SetOutPath "$INSTDIR"
    SetOverwrite on

    ; Copy application files from staging directory
    File /r "src\NetToGXSim3.Wpf\obj\publish_staging\*.*"

    ; Also copy application icon
    CreateDirectory "$INSTDIR\Resources"
    File /oname=Resources\app_icon.ico "src\NetToGXSim3.Wpf\Resources\app_icon.ico"

    ; Copy Example Program HMI folder
    SetOutPath "$INSTDIR\Example Program HMI"
    File /r "Example Program HMI\*.*"
    SetOutPath "$INSTDIR"

    ; Create Uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    ; ----------------------------------------------
    ; Start Menu Shortcuts:
    ; Start Menu -> MELSOFT NetToGXSim -> NetToGXSim3
    ; ----------------------------------------------
    CreateDirectory "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3"
    CreateShortcut "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\NetToGXSim3.lnk" "$INSTDIR\${MAIN_EXE}" "" "$INSTDIR\${MAIN_EXE}" 0
    CreateShortcut "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\Example Program HMI.lnk" "$INSTDIR\Example Program HMI"
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

    ; App Path for Shell Execution
    WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\${MAIN_EXE}"
    WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "Path" "$INSTDIR"

    ; ----------------------------------------------
    ; Register OPC DA Server in 32-bit and 64-bit Registry
    ; ----------------------------------------------
    SetRegView 32
    WriteRegStr HKLM "Software\Classes\${OPC_PROGID}" "" "${OPC_DESC}"
    WriteRegStr HKLM "Software\Classes\${OPC_PROGID}\CLSID" "" "${OPC_CLSID}"
    WriteRegStr HKLM "Software\Classes\${OPC_PROGID}\OPC" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}" "" "${OPC_DESC}"
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}" "AppID" "${OPC_CLSID}"
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\ProgID" "" "${OPC_PROGID}"
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\LocalServer32" "" '"$INSTDIR\${MAIN_EXE}"'
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\Implemented Categories\{63D5F432-CFE4-11d1-B2C8-0060083BA1FB}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\Implemented Categories\{63D5F430-CFE4-11d1-B2C8-0060083BA1FB}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\Implemented Categories\{CC54E38A-DB8C-11d2-AB76-00805F77D1E1}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\Implemented Categories\{63AE88B3-2331-11d1-B3E6-0060975CE62B}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\Implemented Categories\{63AE88B2-2331-11d1-B3E6-0060975CE62B}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\Implemented Categories\{CC603642-66D7-48f1-B69A-B625E73652D7}" "" ""
    WriteRegStr HKLM "Software\Classes\AppID\${OPC_CLSID}" "" "${OPC_DESC}"
    WriteRegDWORD HKLM "Software\Classes\AppID\${OPC_CLSID}" "AuthenticationLevel" 1
    WriteRegStr HKLM "Software\Classes\AppID\${OPC_CLSID}" "RunAs" "Interactive User"

    SetRegView 64
    WriteRegStr HKLM "Software\Classes\${OPC_PROGID}" "" "${OPC_DESC}"
    WriteRegStr HKLM "Software\Classes\${OPC_PROGID}\CLSID" "" "${OPC_CLSID}"
    WriteRegStr HKLM "Software\Classes\${OPC_PROGID}\OPC" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}" "" "${OPC_DESC}"
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}" "AppID" "${OPC_CLSID}"
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\ProgID" "" "${OPC_PROGID}"
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\LocalServer32" "" '"$INSTDIR\${MAIN_EXE}"'
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\Implemented Categories\{63D5F432-CFE4-11d1-B2C8-0060083BA1FB}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\Implemented Categories\{63D5F430-CFE4-11d1-B2C8-0060083BA1FB}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\Implemented Categories\{CC54E38A-DB8C-11d2-AB76-00805F77D1E1}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\Implemented Categories\{63AE88B3-2331-11d1-B3E6-0060975CE62B}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\Implemented Categories\{63AE88B2-2331-11d1-B3E6-0060975CE62B}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\${OPC_CLSID}\Implemented Categories\{CC603642-66D7-48f1-B69A-B625E73652D7}" "" ""
    WriteRegStr HKLM "Software\Classes\AppID\${OPC_CLSID}" "" "${OPC_DESC}"
    WriteRegDWORD HKLM "Software\Classes\AppID\${OPC_CLSID}" "AuthenticationLevel" 1
    WriteRegStr HKLM "Software\Classes\AppID\${OPC_CLSID}" "RunAs" "Interactive User"

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
    Delete "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\Example Program HMI.lnk"
    Delete "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3\Uninstall.lnk"
    RMDir /r "$SMPROGRAMS\MELSOFT NetToGXSim\NetToGXSim3"
    RMDir "$SMPROGRAMS\MELSOFT NetToGXSim"

    ; Remove Installed Files
    RMDir /r "$INSTDIR"

    ; Remove Registry Keys
    DeleteRegKey HKLM "${REG_KEY}"
    DeleteRegKey HKLM "${PRODUCT_DIR_REGKEY}"

    ; Remove OPC DA Server Registry Keys
    SetRegView 32
    DeleteRegKey HKLM "Software\Classes\${OPC_PROGID}"
    DeleteRegKey HKLM "Software\Classes\CLSID\${OPC_CLSID}"
    DeleteRegKey HKLM "Software\Classes\AppID\${OPC_CLSID}"
    DeleteRegKey HKLM "Software\Classes\AppID\${MAIN_EXE}"
    SetRegView 64
    DeleteRegKey HKLM "Software\Classes\${OPC_PROGID}"
    DeleteRegKey HKLM "Software\Classes\CLSID\${OPC_CLSID}"
    DeleteRegKey HKLM "Software\Classes\AppID\${OPC_CLSID}"
    DeleteRegKey HKLM "Software\Classes\AppID\${MAIN_EXE}"
SectionEnd
