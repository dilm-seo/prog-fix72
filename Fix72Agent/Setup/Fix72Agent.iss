; Inno Setup script — Fix72 Agent
; Documentation : https://jrsoftware.org/ishelp/

#define AppName       "Fix72 Agent"
#define AppVersion    "0.1.3"
#define AppPublisher  "Fix72 - Etienne Aubry"
#define AppURL        "https://fix72.com"
#define AppExeName    "Fix72Agent.exe"

[Setup]
AppId={{C7C6F3B4-3E5C-4B5E-9F1A-FIX72AGENT0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\Fix72Agent
DefaultGroupName=Fix72 Agent
DisableProgramGroupPage=yes
OutputDir=..\..\dist
; Filename stable (sans version) → URL React reste valide à chaque release future
OutputBaseFilename=Fix72Agent-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "startupicon"; Description: "Démarrer automatiquement avec Windows"; GroupDescription: "Options :"; Flags: checkedonce

[Files]
Source: "..\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Désinstaller {#AppName}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Fix72Agent"; ValueData: """{app}\{#AppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Lancer {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  ClientNamePage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  ClientNamePage := CreateInputQueryPage(wpWelcome,
    'Personnalisation',
    'Identification du client',
    'Renseignez les informations du client. Le prénom sera affiché en haut de l''interface. ' +
    'Le téléphone sera transmis à Etienne dans les rapports d''alerte (laissez vide si inconnu).');
  ClientNamePage.Add('Prénom du client :', False);
  ClientNamePage.Add('Téléphone du client (optionnel) :', False);
  ClientNamePage.Values[0] := GetUserNameString;
  ClientNamePage.Values[1] := '';
end;

function EscapeJsonString(const S: String): String;
begin
  Result := S;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  SettingsDir: String;
  SettingsFile: String;
  ClientName: String;
  CR: String;
  JsonContent: AnsiString;
begin
  if CurStep = ssPostInstall then begin
    SettingsDir := ExpandConstant('{userappdata}\Fix72Agent');
    SettingsFile := SettingsDir + '\settings.json';

    if not DirExists(SettingsDir) then
      ForceDirectories(SettingsDir);

    // On n'ecrase jamais un settings.json existant - c'est une reinstallation.
    if not FileExists(SettingsFile) then begin
      ClientName := Trim(ClientNamePage.Values[0]);
      CR := Chr(13) + Chr(10);
      JsonContent :=
        '{' + CR +
        '  "ClientName": "' + EscapeJsonString(ClientName) + '",' + CR +
        '  "ClientPhone": "' + EscapeJsonString(Trim(ClientNamePage.Values[1])) + '"' + CR +
        '}' + CR;
      SaveStringToFile(SettingsFile, JsonContent, False);
    end;
  end;
end;
