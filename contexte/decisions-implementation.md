# Décisions d'implémentation

## Première passe (2026-11-16, sans TV)

- **Structure en 3 projets** (`src/`) : `HisenseTvRemote.Core` (logique sans UI,
  net8.0), `HisenseTvRemote` (WPF, net8.0-windows), `HisenseTvRemote.Tests` (xUnit).
  Écart avec la suggestion de SPEC §4 (un seul projet) : permet de tester le
  wrapper sans UI. Format de solution `.slnx` (SDK 10).
- **Pas de MVVM** : code-behind simple, l'UI est un tableau de boutons ; un
  framework serait disproportionné.
- **Arguments** passés via `ProcessStartInfo.ArgumentList` (pas de concaténation → pas d'injection).
  `PYTHONIOENCODING=utf-8` et `PYTHONUNBUFFERED=1` forcés pour recevoir le PIN sans délai.
- **stdin fermé** pour toutes les commandes sauf `auth pair`, pour qu'aucun
  appel ne reste bloqué en attente de saisie. Timeout 30 s par défaut (60 s pour `discover`).
- **Appairage** : la sortie est lue par morceaux (le prompt du PIN n'a pas
  forcément de retour à la ligne) ; le champ PIN s'active dès la première sortie.
  ✔ 17/11 : prompt réel confirmé (`Enter the PIN (or 'q' to cancel): `) ; le champ
  s'active désormais au prompt, pas à la première sortie (voir plus bas).
- **`status` en échec** : affiché tel quel (code + sortie), jamais de popup.
- **Sources** : comportement de `source` non vérifié (SPEC §3.3) → boutons
  « Aide » (`source --help`) et champ libre d'arguments.
  ✔ 17/11 : remplacé par des boutons `tv/hdmi1/hdmi2/av` + « Lister les sources ».
- **Liste des alias** : `config list` parsé de façon tolérante (`ConfigParser`) ;
  le champ alias reste éditable si le parsing ne convient pas.
  ✔ 17/11 : `config list` plante dans pyvidaa 2.3.0 → lecture de `config.yaml`.
- **Localisation de `tv.exe`** : chemin des réglages → PATH →
  `%LOCALAPPDATA%\Programs\Python\Python3*\Scripts`. Réglages dans
  `%APPDATA%\HisenseTvRemote\settings.json`.
- **Tests** : faux `tv.cmd` généré dans le dossier temp (`FakeTv.cs`).
- **Historique** : cette première passe de code a été écrite sur un poste
  sans Python/`tv.exe` ni accès réseau à la TV — validée uniquement par les
  10 tests automatisés avec un faux `tv.exe`. À partir du 17/11/2026,
  développement et tests réels se poursuivent sur le poste `RIANTSOA`
  (Python 3.12 + `pyvidaa`/`tv.exe` + certificat + accès réseau à la TV +
  Visual Studio 2026), ce qui permet enfin la validation sur la vraie TV
  `salon`. Les chemins de `CLAUDE.local.md` sont ceux de ce poste.

## Validation sur la vraie TV (2026-11-17)

Détail des comportements observés : `pyvidaa-comportement-reel.md`.

- **Répertoire de travail de `tv.exe` fixé** à `%USERPROFILE%\.config\pyvidaa`
  (`Settings.DataDirectory`, vide = ce défaut). pyvidaa y trouve `config.yaml`
  même lancé d'ailleurs (chemin de recherche standard) ; `tokens.json` n'y est
  trouvé que si on s'y place, d'où le `Push-Location` ajouté au script PowerShell.
- **Liste des TV lue dans `config.yaml`** (`TvConfigFile`, parseur YAML minimal
  maison) plutôt que via `config list/show`, qui plantent. Pas de dépendance
  NuGet YAML : le fichier est produit par `yaml.safe_dump` en style bloc, très
  régulier. Le bouton « Afficher » ne lance donc plus `tv.exe`.
- **`ConfigSetAliasAsync` prend l'identifiant** de la TV (pas l'alias) ; si la TV
  renommée était la TV par défaut (référencée par alias), l'app relance
  `config set-default <nouvel alias>`.
- **Écriture directe dans `config.yaml`** limitée à la ligne `host:` d'une TV
  (« Retrouver l'IP »), le reste du fichier est conservé octet pour octet.
  Seule écriture faite hors de `tv.exe`, faute de commande pyvidaa équivalente.
- **IP retrouvée par la MAC via `arp -a`** (après `discover`) : `discover`
  n'affiche pas les MAC sur ce réseau, la table ARP si.
- **Commandes TV sérialisées** (`SemaphoreSlim` dans `TvController`,
  `RunQueuedAsync`) ; `discover` et `config` (sans MQTT) restent hors file.
  Pas de fusion des clics répétés (5 × Vol+ = 5 commandes ≈ 10 s) : simple et
  fidèle ; à revoir si gênant (`volume up N`, ou processus Python persistant).
- **Appairage** : champ PIN activé uniquement au prompt réel
  (`PairingSession.PinRequested`, marqueur `Enter the PIN`) ; fermer le dialogue
  pendant l'appairage envoie `q` puis tue le processus après 5 s ; un seul
  essai de PIN par session (pyvidaa sort après la vérification).
- **Barre d'état** : `CommandResult.Summary` — dernière ligne de stderr en cas
  d'échec, première ligne (+ nombre de lignes) en cas de succès ; message
  « TV injoignable ? → Retrouver l'IP » si la sortie l'indique.
- **Apps et sources dynamiques** : boutons générés depuis `app list` et
  `source list`, en plus des raccourcis fixes.
- **Non testés par moi sur la vraie TV** : `power`, `off`, `on` (risque de
  laisser la TV éteinte sans pouvoir vérifier le rallumage), saisie d'un vrai
  PIN (il faut lire l'écran), effet visuel de `source` (publié, code 0, mais
  invisible dans `status`). À valider par l'utilisateur.
- **Pont Python persistant** (17/11, après « c'est lent ») : seules les commandes
  fréquentes y passent ; protocole JSON ligne à ligne plutôt qu'un serveur
  HTTP/socket (pas de port à ouvrir, cycle de vie lié à l'app) ; repli
  systématique sur `tv.exe`. Détail : `pont-python-persistant.md`.
- **Moteur natif C#** (17/11) : `NativeTv` reprend l'interface en ligne de
  commande de `tv.exe` (mêmes arguments, mêmes textes) plutôt qu'une API
  objet séparée, pour ne rien changer à l'UI, au journal ni aux parseurs, et
  garder un repli trivial vers `tv.exe`. MQTTnet 4.3.7 (API stable connue ;
  la 5.x change l'API). Fichiers partagés avec pyvidaa (même format), pour
  pouvoir basculer de moteur à tout moment. Détail : `portage-natif-csharp.md`.
- **Appairage** : interface `IPairingSession` commune (tv.exe / natif) ; le
  dialogue n'a pas changé. Le moteur natif tient sa connexion pendant tout
  l'appairage (les autres commandes attendent : le dialogue est modal).
- **Base SQLite** (17/11) : Microsoft.Data.Sqlite + DPAPI (portée utilisateur)
  pour les jetons ; base à côté de l'exe si inscriptible, sinon %APPDATA%.
  Stockages derrière deux interfaces (`ITvConfigStore`, `ITokenStore`) :
  SQLite pour le moteur natif, fichiers pyvidaa pour le mode Python. Import
  unique, fichiers d'origine intacts. « Vider » le journal vide aussi
  l'historique en base (après confirmation). Détail : `base-sqlite.md`.
- **Interface** (17/11) : MVVM (CommunityToolkit.Mvvm 8.2.2) pour l'écran
  principal, ViewModel dans le Core pour être testé par le projet de tests
  (net8.0, sans WPF) ; fenêtres secondaires en code-behind (formulaires).
  Choix de la TV par un menu contextuel plutôt qu'une ComboBox (difficile à
  styler en sombre). Volume relu 0,5 s après une rafale de clics (une seule
  requête). Détail : `interface.md`.
- **Retrait de Python** (17/11) : suppression de `TvLocator`, `TvBridge` +
  `vidaa_bridge.py`, `PairingSession` (tv.exe), `TvConfigFile` (YAML),
  `VidaaTokenStore` (tokens.json), `PyvidaaImport`, `ConfigParser`, du script
  `livrables/Invoke-HisenseTv.ps1` et des réglages associés (chemin de
  tv.exe, moteur, pont, dossier pyvidaa). `TvController` s'appuie sur une
  interface `ITvEngine` (moteur natif ; faux moteur en mémoire pour les
  tests). Journal : préfixe `tv` au lieu de `[natif]`. Identifiant MQTT
  « statique » renommé `HisenseTvRemote` (sert seulement aux vieux
  firmwares). Certificat déplacé dans `%APPDATA%\HisenseTvRemote\certs`,
  importable depuis le `.p12` (Windows lit son ancien chiffrement RC2/3DES).
  La base ne s'ouvre plus en repli sur des fichiers : sans base, message
  d'erreur et arrêt.
- **Molette = volume** (17/11) : zone ciblée (bascule Volume) + Ctrl + molette
  global, plutôt que la molette partout (elle doit continuer à faire défiler
  la grille d'apps). Logique dans `RemoteViewModel.WheelVolumeAsync`
  (testée) ; la vue ne fait que transmettre `e.Delta`.
- **Raccourcis clavier** (17/11) : table déclarative dans le Core (testable,
  future configuration), interception en `PreviewKeyDown` sur la fenêtre
  principale, zoom reconnu par caractère selon la disposition du clavier.
  Lettres/chiffres seuls réservés, pas de raccourci pour Marche/Arrêt.
- **Saisie de texte** (17/11) : champ dédié dont le contenu entier est recopié
  (`replace`) plutôt que des caractères envoyés un à un (`insert`) — gère le
  Retour arrière et les corrections sans connaître le contenu côté TV.
  Taper hors du champ démarre une **nouvelle** saisie (cas d'une nouvelle
  recherche) ; cliquer dans le champ permet de modifier l'ancienne.

