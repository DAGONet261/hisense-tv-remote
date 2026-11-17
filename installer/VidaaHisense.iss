; ==============================================================================
;  Installeur « VIDAA - Hisense » — Inno Setup 7
;
;  ENCODAGE : ce fichier DOIT rester en UTF-8 AVEC BOM. Sans BOM, Inno Setup le lit
;  dans l'encodage ANSI du poste et les accents (é, è, à, ç…) et caractères spéciaux
;  (©, «, », —) deviennent illisibles dans l'assistant.
;
;  Compilé automatiquement par une compilation Release (src\Directory.Build.targets),
;  qui fournit /DAppVersion et /DPublishDir. Compilation manuelle possible :
;    ISCC.exe /DAppVersion=1.0.0.0 /DPublishDir=..\dist\publish VidaaHisense.iss
;
;  Aucun secret, certificat ni base de données n'est embarqué : le certificat client
;  VIDAA est importé par l'utilisateur au premier lancement (« TV et réglages »).
; ==============================================================================

#define AppName      "VIDAA - Hisense"
#define AppExeName   "HisenseTvRemote.exe"
#define AppPublisher "Tiana Riantsoa RAHARISON"
#define AppDataDir   "HisenseTvRemote"

#ifndef PublishDir
  #define PublishDir AddBackslash(SourcePath) + "..\dist\publish"
#endif
#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(AddBackslash(PublishDir) + AppExeName)
#endif

[Setup]
; Identifiant permanent de l'application : ne jamais le changer (mises à jour et désinstallation).
AppId={{6FA552D5-287E-4782-A7C9-2B96E0B84752}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=© 2026 {#AppPublisher}
AppComments=Télécommande pour téléviseurs Hisense VIDAA, sur le réseau local.

VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=Installation de {#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoCopyright=© 2026 {#AppPublisher}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
; Installation pour tous les utilisateurs (administrateur) ou pour soi seul, au choix.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

OutputDir=..\dist
OutputBaseFilename=VIDAA-Hisense-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes

WizardStyle=modern
WizardImageFile=images\WizardImage-100.bmp,images\WizardImage-125.bmp,images\WizardImage-150.bmp,images\WizardImage-200.bmp,images\WizardImage-250.bmp
WizardSmallImageFile=images\WizardSmallImage-100.bmp,images\WizardSmallImage-125.bmp,images\WizardSmallImage-150.bmp,images\WizardSmallImage-200.bmp,images\WizardSmallImage-250.bmp
SetupIconFile=..\src\HisenseTvRemote\Resources\VidaaHisense.ico

UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
; Ferme l'application si elle est ouverte pendant une mise à jour ou une désinstallation.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
french.LaunchApp=Lancer {#AppName}
french.RemoveDataQuestion=Supprimer aussi vos données personnelles ?%n%nTéléviseurs appairés, jetons d'accès chiffrés, certificat client VIDAA importé et journal des commandes.%n%nChoisissez « Non » pour les conserver (utile en cas de réinstallation).

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "Télécommande pour téléviseurs Hisense VIDAA"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "Télécommande pour téléviseurs Hisense VIDAA"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
// À la désinstallation : proposer (sans l'imposer) d'effacer les données de l'utilisateur,
// rangées dans %APPDATA%\HisenseTvRemote (base SQLite, certificat, journal des erreurs).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  DataDir := ExpandConstant('{userappdata}\{#AppDataDir}');
  if not DirExists(DataDir) then
    Exit;
  if UninstallSilent then
    Exit;
  if SuppressibleMsgBox(CustomMessage('RemoveDataQuestion'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    DelTree(DataDir, True, True, True);
end;
