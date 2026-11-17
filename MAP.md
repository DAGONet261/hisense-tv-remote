# MAP.md — carte du projet

Tenue à jour à chaque création ou déplacement de fichier. Pour trouver une
info : chercher d'abord ici le fichier qui devrait la contenir, avant de
chercher ailleurs.

## Racine

| Fichier | Contenu | Où ranger une nouvelle info de ce type |
|---|---|---|
| `CLAUDE.md` | Règles à respecter (sessions en parallèle, secrets) — en vigueur ; « qui / quoi / comment » encore à remplir (5 questions). | Règles de collaboration générales, non techniques |
| `SESSIONS.md` | Zones réservées par les sessions en cours (règle dans `CLAUDE.md`). | Réserver/libérer sa zone avant/après une session |
| `CLAUDE.local.md` | Détails techniques propres à cette machine (certificat, base, TV de test). | Toute info technique spécifique à ce poste, non partageable |
| `SPEC.md` | Ce qu'on veut obtenir : objectif, architecture, fonctionnalités de l'app WPF. | Exigences fonctionnelles, périmètre |
| `PLAN.md` | Les étapes de réalisation, à cocher. | Avancement, prochaines étapes |
| `MAP.md` | Ce fichier. | — |
| `README.md` | Présentation rapide du dossier. | — |
| `THIRD-PARTY-NOTICES.md` | Licences tierces (pyvidaa porté en C#, MQTTnet) ; certificat VIDAA non distribué. Copié à côté de l'exe. | Toute nouvelle dépendance ou code repris |
| `.gitignore` | Exclusions Git : secrets (`.env`), certificats et clés, APK VIDAA (`sources/`), base, journaux, notes locales. | Nouvelle exclusion si un outil ajoute des fichiers à ignorer |
| `version.txt` | Version de la dernière Release (une compilation Release l'incrémente). Voir `contexte/publication.md`. | — |
| `build/` | Scripts : `Publish-Release.ps1` (Release + installeur en ligne de commande), `New-AppIcon.ps1` (icône et images de l'installeur). | Nouveau script de construction/publication |
| `installer/` | Installeur Inno Setup 7 : `VidaaHisense.iss` (**UTF-8 avec BOM**), `images/` de l'assistant. | Tout ce qui concerne l'installeur |
| `dist/` | *Généré, non versionné* : `publish/` (app autonome) et `VIDAA-Hisense-Setup-<version>.exe`. | — |
| `.gitattributes` | Normalisation des fins de ligne entre appareils. | — |
| `.env` | Secrets réels (mot de passe PKCS12 du certificat VIDAA, `VIDAA_P12_PASSWORD`). **Jamais committé.** | Tout secret/clé réelle |
| `.env.example` | Modèle des variables attendues, sans valeurs. | Nouvelle variable d'environnement (déclarer ici sans sa valeur) |

## `src/` — code de l'application (solution `HisenseTvRemote.slnx`)

100 % C#/.NET 8, sans Python (retiré le 17/11/2026, voir `contexte/decisions-implementation.md`).

| Fichier / dossier | Contenu |
|---|---|
| `HisenseTvRemote.Core/TvController.cs` | Point d'entrée des commandes TV pour l'UI (interface `ITvEngine`) ; chaque commande va au journal. |
| `HisenseTvRemote.Core/Vidaa/NativeTv.cs` | Moteur : exécute les commandes (`--tv salon key home`…), garde les connexions MQTT ouvertes, config, découverte. |
| `HisenseTvRemote.Core/Vidaa/VidaaClient.cs` | Client MQTT/TLS (MQTTnet) d'une TV : connexion, jetons, requêtes, appairage. |
| `HisenseTvRemote.Core/Vidaa/VidaaProtocol.cs` | Constantes, sujets MQTT, calcul des identifiants dynamiques, touches/sources/apps (portage de pyvidaa). |
| `HisenseTvRemote.Core/Vidaa/VidaaDiscovery.cs` | Découverte SSDP + descripteur UPnP (MAC Ethernet/Wi-Fi, version de protocole), Wake-on-LAN. |
| `HisenseTvRemote.Core/Vidaa/ClientCertificate.cs` | Certificat client VIDAA : emplacement (`%APPDATA%\HisenseTvRemote\certs`), chargement, import depuis le `.p12` de l'APK ou une paire PEM. |
| `HisenseTvRemote.Core/Vidaa/NativePairingSession.cs`, `IPairingSession.cs` | Appairage par code PIN. |
| `HisenseTvRemote.Core/Vidaa/VidaaToken.cs` | Jetons d'appairage d'une TV (validité, renouvellement). |
| `HisenseTvRemote.Core/Storage/AppDatabase.cs` | Base SQLite portable : emplacement (à côté de l'exe ou %APPDATA%), schéma, réglages, journal, chiffrement DPAPI. |
| `HisenseTvRemote.Core/Storage/SqliteStores.cs`, `IStores.cs` | TV et jetons (chiffrés) dans la base ; interfaces de stockage. |
| `HisenseTvRemote.Core/Storage/TvEntry.cs` | Fiche d'une TV et recherche par alias/identifiant (`TvLookup`). |
| `HisenseTvRemote.Core/Storage/IconCache.cs` | Cache des logos d'apps fournis par la TV (`httpIcon`). |
| `HisenseTvRemote.Core/ViewModels/RemoteViewModel.cs` | Logique de l'écran principal (MVVM, testable sans WPF). |
| `HisenseTvRemote.Core/KeyboardShortcuts.cs` | Table des raccourcis clavier (touches, modificateurs, action, rubrique de l'aide). |
| `HisenseTvRemote.Core/PointerToKeys.cs` | Mode souris : traduit les déplacements de la souris en touches directionnelles. |
| `HisenseTvRemote.Core/Settings.cs` | Réglages de l'app (TV courante, dossier du certificat), en base. |
| `HisenseTvRemote.Core/KeyCatalog.cs`, `CommandResult.cs`, `ArpTable.cs` | Touches et libellés français ; résultat d'une commande et son résumé ; IP d'une TV par sa MAC (`arp -a`). |
| `HisenseTvRemote/MainWindow.xaml(.cs)` | Fenêtre principale (thème sombre) : télécommande, apps avec logos, sources, choix de la TV. |
| `HisenseTvRemote/TvWindow.xaml(.cs)` | Fenêtre « TV et réglages » : TV, réseau, appairage, import du certificat. |
| `HisenseTvRemote/JournalWindow.xaml(.cs)` | Fenêtre « Journal » : historique des commandes (conservé en base). |
| `HisenseTvRemote/PairingDialog.xaml(.cs)`, `LogEntry.cs` | Dialogue de PIN ; ligne du journal. |
| `HisenseTvRemote/ShortcutsWindow.xaml(.cs)` | Aide des raccourcis (F1), générée depuis la table. |
| `HisenseTvRemote/MouseModeWindow.xaml(.cs)` | Mode souris plein écran (F11) : la souris du PC pilote la TV. |
| `HisenseTvRemote/AppServices.cs` | Services partagés par les fenêtres (base, réglages, contrôleur, ViewModel, journal). |
| `Directory.Build.props`, `Directory.Build.targets` | Identité de l'exe (nom, éditeur, icône) ; version lue/incrémentée depuis `version.txt` ; en Release : publication et installeur. |
| `HisenseTvRemote/Resources/VidaaHisense.ico` | Icône de l'application (VIDAA officielle, 16 à 256 px), générée par `build/New-AppIcon.ps1`. |
| `HisenseTvRemote/Resources/Theme.xaml` | Thème sombre : couleurs, styles de boutons, champs, listes, barres de défilement. |
| `HisenseTvRemote.Tests/` | Tests xUnit sans TV ni réseau : `FakeEngine.cs` (faux moteur en mémoire), `CoreTests.cs` (contrôleur, touches, certificat), `VidaaNativeTests.cs` (identifiants, découverte, commandes), `StorageTests.cs` (base), `ViewModelTests.cs` (écran principal), `ShortcutTests.cs` (raccourcis, sources). `dotnet test src/HisenseTvRemote.Tests`. |

## `contexte/` — ce qu'on apprend, une note par sujet

| Fichier | Contenu |
|---|---|
| `architecture-decision.md` | Choix d'architecture : enveloppe Python (16/11) puis moteur C# natif et retrait de Python (17/11). |
| `protocole-vidaa.md` | Détail technique du protocole MQTT local VIDAA reconstruit ce soir (ports, auth, appairage PIN, extraction du certificat). |
| `decisions-implementation.md` | Choix techniques pris en autonomie pendant l'implémentation (première passe 16/11, validation réelle 17/11). |
| `certificat-client.md` | Le certificat client VIDAA : durée de vie (2034), pourquoi il peut changer, pourquoi il ne va ni dans le dépôt ni dans le setup. |
| `saisie-texte.md` | Saisie de texte à distance : annonces de la TV (formats `input_data`/`inputdata`), liste blanche des sujets, variantes refusées, pistes. |
| `publication.md` | Version (`version.txt`), compilation Release dans Visual Studio, installeur Inno Setup (UTF-8 avec BOM), ce qui est embarqué ou non. |
| `interface.md` | Refonte de l'interface : organisation, règles de nommage des boutons, logos fournis par la TV, architecture MVVM. |
| `base-sqlite.md` | Base SQLite portable : emplacement, tables, chiffrement DPAPI des jetons, import, repli. |
| `portage-natif-csharp.md` | Moteur natif C# : correspondance avec pyvidaa, mesures, améliorations, points d'attention. |
| `capacites-tv.md` | Ce que la TV déclare savoir faire (pas de capture d'écran, pilotage souris…), modèle exact, deux cartes réseau, touches de lecture vérifiées (Rembobiner = `KEY_BACKS`), lecteur DLNA (partage de photos/vidéos). |
| `pont-python-persistant.md` | *Historique* : lenteur de `tv.exe` et pont Python persistant (retirés le 17/11). |
| `pyvidaa-comportement-reel.md` | *Historique* : ce que faisait vraiment `tv.exe` sur la 55A53FXVT : fichiers dans le répertoire courant, bugs de pyvidaa 2.3.0, sorties réelles, collision MQTT, changement d'IP. |
| `preferences-travail.md` | Comment l'utilisateur préfère qu'on travaille (style de réponses, choix techniques). |

## `.claude/` — réglages Claude Code du projet

| Fichier / dossier | Contenu |
|---|---|
| `skills/wpf/` | Skill « wpf » (managedcode/dotnet-skills, MIT) : bonnes pratiques WPF/MVVM, liaisons, threading, styles. `SOURCE.md` = provenance et vérification avant installation. |

## `sources/`

Vide pour l'instant (hormis son `README.md` explicatif). Destiné aux
documents bruts déposés par l'utilisateur.

## `livrables/`

Vide depuis le 17/11/2026 (le script PowerShell autour de `tv.exe` a été retiré avec Python). Destiné aux futurs exports de l'app (setup, versions).

## `scripts/`

Vide pour l'instant (hormis son `README.md` explicatif).

## Éléments volontairement absents aujourd'hui

Le projet est versionné avec Git depuis le 17/11/2026 (dépôt privé GitHub
`DAGONet261/hisense-tv-remote`) ; les sessions parallèles utilisent des
`git worktree` (règle dans `CLAUDE.md`). Pour rester léger : pas encore de
`AGENTS.md`, pas de `.mcp.json` (aucun outil MCP spécifique identifié), pas de
`.worktreeinclude` (rien d'ignoré à recopier dans un worktree : `.env` et le
certificat se recopient à la main), pas de `docs/` (le contenu envisagé tient pour
l'instant dans `contexte/` et `SPEC.md`), pas de `.github/workflows/`
(pas de CI/CD pour une app de bureau développée et testée manuellement).
Ces éléments seront ajoutés — et documentés ici — dès qu'un besoin réel
apparaît.
