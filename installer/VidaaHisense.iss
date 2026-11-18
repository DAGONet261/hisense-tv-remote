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
#define FirewallRule "VIDAA - Hisense (partage vers la TV)"

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

[Messages]
; Page d'accueil : avertissement de marque (installeur public).
french.WelcomeLabel2=Cet assistant va installer [name/ver] sur votre ordinateur.%n%nProjet indépendant, non affilié à Hisense : « Hisense » et « VIDAA » sont des marques de Hisense, utilisées ici seulement pour désigner les téléviseurs compatibles. Logiciel libre sous licence MIT.%n%nIl est recommandé de fermer toutes les autres applications avant de continuer.

[CustomMessages]
french.LaunchApp=Lancer {#AppName}
french.FirewallStatus=Autorisation du partage vers la TV dans le pare-feu Windows…
french.FirewallGroup=Partage vers la TV :
french.FirewallTask=Autoriser le partage vers la TV dans le pare-feu Windows (réseau privé, appareils du réseau local seulement)
french.FirewallFailed=Le partage vers la TV n'a pas pu être autorisé dans le pare-feu Windows (autorisation refusée ou impossible).%n%nCe n'est pas bloquant : à la première utilisation de « Partager », l'application proposera de l'autoriser en un clic.
french.PublicNetwork=Votre réseau est classé « Public » dans Windows : par sécurité, le partage vers la TV n'y est pas autorisé.%n%nSi vous êtes chez vous, classez-le en « Privé » : Paramètres Windows → Réseau et Internet → Ethernet (ou Wi-Fi) → Type de profil réseau → Privé.
french.RemoveDataQuestion=Supprimer aussi vos données personnelles ?%n%nTéléviseurs appairés, jetons d'accès chiffrés, certificat client VIDAA importé et journal des commandes.%n%nChoisissez « Non » pour les conserver (utile en cas de réinstallation).

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "firewall"; Description: "{cm:FirewallTask}"; GroupDescription: "{cm:FirewallGroup}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "Télécommande pour téléviseurs Hisense VIDAA"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "Télécommande pour téléviseurs Hisense VIDAA"; Tasks: desktopicon

[Run]
; Le pare-feu (partage vers la TV) est traité dans [Code] : CurStepChanged.
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"""; Flags: runhidden; RunOnceId: "SupprimerRegleParefeu"; Check: IsAdminInstallMode

[Code]
// ---------- Pare-feu : partage vers la TV ----------------------------------------------------
// La TV vient chercher les photos/vidéos sur ce PC (DLNA) : règle entrante pour l'exe, TCP, réseau
// privé, sous-réseau local. Installation pour tous : créée directement (Setup est administrateur).
// Installation « pour moi seul » : confirmation administrateur (UAC) juste pour la règle ; si elle est
// refusée, l'installation continue et l'application proposera de l'autoriser.

const
  FW_PROFILE_PRIVATE = 2;
  FW_PROFILE_PUBLIC = 4;

function FirewallPolicy(var Policy: Variant): Boolean;
begin
  try
    Policy := CreateOleObject('HNetCfg.FwPolicy2');
    Result := True;
  except
    Result := False;
  end;
end;

{ La règle de l'application existe-t-elle, active, pour l'exe installé ? }
function FirewallRuleOk(): Boolean;
var
  Policy, Rule: Variant;
  Enabled: Boolean;
  RuleApp: String;
begin
  Result := False;
  if not FirewallPolicy(Policy) then Exit;
  try
    Rule := Policy.Rules.Item('{#FirewallRule}');
    Enabled := Rule.Enabled;
    RuleApp := Rule.ApplicationName;
    Result := Enabled and (CompareText(RuleApp, ExpandConstant('{app}\{#AppExeName}')) = 0);
  except
    Result := False;
  end;
end;

{ Réseau actuel classé « Public » (et pas « Privé ») : la règle « réseau privé » ne s'y applique pas. }
function OnPublicNetworkOnly(): Boolean;
var
  Policy: Variant;
  Types: Integer;
begin
  Result := False;
  if not FirewallPolicy(Policy) then Exit;
  try
    Types := Policy.CurrentProfileTypes;
    Result := ((Types and FW_PROFILE_PUBLIC) <> 0) and ((Types and FW_PROFILE_PRIVATE) = 0);
  except
    Result := False;
  end;
end;

{ Lance une commande netsh, élevée si l'installation ne l'est pas (UAC). }
procedure RunFirewallCommand(const Params: String);
var
  ResultCode: Integer;
begin
  if IsAdminInstallMode then
    Exec(ExpandConstant('{cmd}'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
  else
    ShellExec('runas', ExpandConstant('{cmd}'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure AllowSharing();
begin
  RunFirewallCommand('/c netsh advfirewall firewall delete rule name="{#FirewallRule}" >nul 2>&1 & ' +
    'netsh advfirewall firewall add rule name="{#FirewallRule}" dir=in action=allow program="' +
    ExpandConstant('{app}\{#AppExeName}') + '" enable=yes profile=private remoteip=localsubnet protocol=TCP');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then
    Exit;
  if WizardIsTaskSelected('firewall') then
  begin
    WizardForm.StatusLabel.Caption := CustomMessage('FirewallStatus');
    if not FirewallRuleOk() then
      AllowSharing();
    if (not FirewallRuleOk()) and (not WizardSilent) then
      SuppressibleMsgBox(CustomMessage('FirewallFailed'), mbInformation, MB_OK, IDOK);
  end;
  if OnPublicNetworkOnly() and (not WizardSilent) then
    SuppressibleMsgBox(CustomMessage('PublicNetwork'), mbInformation, MB_OK, IDOK);
end;

// À la désinstallation : proposer (sans l'imposer) d'effacer les données de l'utilisateur,
// rangées dans %APPDATA%\HisenseTvRemote (base SQLite, certificat, journal des erreurs).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  // Installation « pour moi seul » : la règle (créée via UAC) est retirée de la même façon.
  if (CurUninstallStep = usUninstall) and (not IsAdminInstallMode) and FirewallRuleOk() then
    RunFirewallCommand('/c netsh advfirewall firewall delete rule name="{#FirewallRule}" program="' +
      ExpandConstant('{app}\{#AppExeName}') + '"');
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
