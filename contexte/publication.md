# Publication : version, Release et installeur (17/11/2026)

## En bref

- **`version.txt`** (racine) = version de la **dernière Release produite**
  (format `majeur.mineur.correctif.révision`). Première version : **1.0.0.0**.
- **Visual Studio 2026** : configuration **Release** → **Générer**. Cela :
  1. prend la version suivante (révision + 1, ex. 1.0.0.0 → 1.0.0.1) ;
  2. compile ; **seulement si la compilation réussit**, écrit la version
     dans `version.txt` (un échec ne « consomme » pas de numéro) ;
  3. publie l'app autonome (win-x64, .NET inclus) dans `dist\publish\` ;
  4. compile l'installeur Inno Setup à la même version :
     `dist\VIDAA-Hisense-Setup-<version>.exe`.
- **Ligne de commande** (même résultat) : `build\Publish-Release.ps1`
  (`-NoBump` : recompiler la même version ; `-SetVersion 1.1.0.0` : fixer
  la version, ex. pour passer en 1.1).
- **Debug** : lit `version.txt`, n'incrémente jamais, ne produit pas
  d'installeur. Les tests (`dotnet test`) ne sont pas concernés.
- `dist\` est généré et n'est **pas versionné** ; `version.txt`, lui, l'est :
  **committer `version.txt` après chaque Release** pour que l'autre poste
  reparte du bon numéro.

## Où c'est câblé

- `src/Directory.Build.props` : identité de l'exe (nom « VIDAA - Hisense »,
  éditeur, icône, `RuntimeIdentifiers=win-x64` pour que la publication
  autonome marche aussi depuis Visual Studio).
- `src/Directory.Build.targets` : cible `HtrApplyVersion` (avant la
  compilation : version lue/incrémentée, appliquée à `Version`,
  `AssemblyVersion`, `FileVersion`, `InformationalVersion`) et cible
  `HtrRelease` (après une compilation Release réussie : écriture de
  `version.txt`, publication, `ISCC.exe`).
  - Garde-fous : seulement le projet `HisenseTvRemote` (pas le projet
    temporaire `_wpftmp` de la compilation XAML), jamais pendant les
    « compilations de conception » de Visual Studio (`DesignTimeBuild`), et
    la publication imbriquée reçoit `HtrPackaging=true` +
    `HtrVersionOverride` pour ne pas incrémenter une deuxième fois.
  - Propriétés : `-p:HtrBump=false` (pas d'incrément),
    `-p:HtrSetup=false` (pas d'installeur), `-p:HtrIscc=<chemin>` (autre
    ISCC). Inno Setup 7 est cherché dans Program Files (puis 6 en secours) ;
    s'il manque : avertissement, l'app est quand même publiée.
- `installer/VidaaHisense.iss` : script de l'installeur.
- `build/New-AppIcon.ps1` : régénère l'icône et les images de l'assistant.

## Piège Visual Studio : « Générer » ne fait rien si rien n'a changé

Le contrôle rapide « à jour » de Visual Studio saute la compilation quand
aucun fichier source n'a changé : **pas de nouvelle version ni
d'installeur**. Pour forcer une Release : **Régénérer** (Rebuild) au lieu de
Générer.

## Installeur (Inno Setup 7)

- **Encodage : `VidaaHisense.iss` doit rester en UTF-8 avec BOM.** Sans
  BOM, Inno Setup lit le script en ANSI et les accents/caractères spéciaux
  (é, à, ©, «, —) sont corrompus dans l'assistant (problème déjà rencontré
  sur E-connecteur). Idem pour les `.ps1` (Windows PowerShell 5.1 lit un
  fichier sans BOM en ANSI). Vérifier : `head -c3 fichier | od -An -tx1` →
  `ef bb bf`.
- Assistant 100 % français (`French.isl` + messages propres), style
  moderne, images jaune VIDAA en 5 résolutions (100 % à 250 %).
- `AppId` fixe `{6FA552D5-287E-4782-A7C9-2B96E0B84752}` : **ne jamais le
  changer** (sinon Windows voit une autre application : pas de mise à jour
  en place, deux entrées dans « Applications installées »).
- Installation dans `Program Files\VIDAA - Hisense` (pour tous) ou, au
  choix de l'utilisateur, pour lui seul (sans droits administrateur).
  Raccourci menu Démarrer ; bureau en option.
- Une nouvelle version s'installe par-dessus l'ancienne (même `AppId`) ;
  l'app ouverte est fermée automatiquement.
- Données : l'app installée dans Program Files ne peut pas écrire à côté de
  l'exe → base, certificat et `erreurs.log` vont dans
  `%APPDATA%\HisenseTvRemote` (logique d'`AppDatabase.DefaultPath`). À la
  désinstallation, l'utilisateur choisit de les garder (par défaut) ou de
  les supprimer.
- **Rien de sensible n'est embarqué** : ni certificat, ni base, ni `.env`
  (vérifié sur la 1.0.0.0 : 474 fichiers publiés, aucun `*.db`, `*.pem`,
  `*.key`, `*.p12`, `.env`). Le certificat client s'importe au premier
  lancement (« TV et réglages » → « Importer le certificat (.p12) »).

## Décisions prises (17/11)

- **Publication autonome** (self-contained win-x64) plutôt que dépendante
  du runtime .NET : aucun prérequis sur le poste cible ; installeur ~50 Mo.
- **Incrément de la révision** (4e nombre) à chaque Release ; les montées de
  version volontaires (1.1, 2.0) se font avec `-SetVersion` ou en éditant
  `version.txt`.
- **Nom affiché** « VIDAA - Hisense » (fenêtre, raccourcis, Applications
  installées) ; l'exe garde son nom technique `HisenseTvRemote.exe` (pas
  d'espace dans le nom de fichier) et le dossier de données reste
  `HisenseTvRemote`.
- **Éditeur et copyright** : « Tiana Riantsoa RAHARISON » (à changer au besoin : `Company`/`Copyright`
  dans `Directory.Build.props` et `AppPublisher` dans le `.iss`).
- **Icône** : icône officielle de l'app VIDAA (fond jaune `#FFE212`, logo
  noir), reconstruite en grand à partir du logo de l'app pour rester nette
  de 16 à 256 px. Sources dans l'APK (`sources/`, non versionné) :
  `split_config.xxhdpi.apk` → `res/drawable-xxhdpi-v4/app_icon.png` et
  `res/mipmap-xhdpi-v4/vidaa_logo.png`. Le logo VIDAA est une marque
  d'Hisense : sans conséquence pour un usage personnel ; à reconsidérer
  avant toute diffusion publique.
- **Signature de code** : non faite (pas de certificat de signature). Sans
  signature, Windows SmartScreen peut avertir au premier lancement de
  l'installeur (« Informations complémentaires » → « Exécuter quand
  même »).
