; Installateur NEXUS LOBBY (Inno Setup 6).
; Construit par scripts\publish.ps1 : ISCC /DAppVersion=x.y.z /DSourceDir=<dist\NexusLobby> NexusLobby.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\NexusLobby"
#endif

#define AppName "NEXUS LOBBY"
#define ServiceName "VpnClientService"

[Setup]
AppId={{6E2F1C9A-4B7D-4E1A-9F3C-2D8B5A7E1C40}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=D4MNTV
DefaultDirName={autopf}\NEXUS LOBBY
DefaultGroupName={#AppName}
LicenseFile=..\LICENSE
OutputBaseFilename=NexusLobby-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Le service tourne en SYSTEM : l'installation demande les droits administrateur.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayIcon={app}\App\VpnClient.App.exe

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\App\*";     DestDir: "{app}\App";     Flags: recursesubdirs ignoreversion
Source: "{#SourceDir}\Service\*"; DestDir: "{app}\Service"; Flags: recursesubdirs ignoreversion
Source: "..\LICENSE";             DestDir: "{app}"
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\App\VpnClient.App.exe"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\App\VpnClient.App.exe"; Tasks: desktopicon

[Run]
; Service Windows : démarrage automatique, redémarrage en cas de plantage.
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""{app}\Service\VpnClientService.exe"" start= auto DisplayName= ""NEXUS LOBBY Service"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Gère les tunnels WireGuard et OpenVPN de NEXUS LOBBY."""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/30000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden
Filename: "{app}\App\VpnClient.App.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent unchecked

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"

[Code]
// Mise à jour : arrête et supprime l'ancien service avant de remplacer ses fichiers.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(1500);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

// Prévient si WireGuard n'est pas installé (l'app fonctionne, mais ne pourra pas se connecter aux profils .conf).
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not FileExists(ExpandConstant('{commonpf64}\WireGuard\wireguard.exe')) then
    MsgBox('WireGuard n''est pas installé. Installe-le depuis https://www.wireguard.com/install/ pour utiliser les profils .conf.',
      mbInformation, MB_OK);
end;
