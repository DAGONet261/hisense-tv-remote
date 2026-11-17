# PLAN.md

Étapes pour construire `HisenseTvRemote`. À cocher/mettre à jour au fil de
l'avancement (par toi, ou par Claude Code dans Visual Studio).

- [x] Scaffolder le projet WPF (.NET 8) — `src/HisenseTvRemote.slnx` (Core + WPF + Tests)
- [x] Écrire `TvController` (appel async à `tv.exe`, capture stdout/stderr, timeout)
- [x] Onglet Télécommande : toutes les touches de `SPEC.md` §3.1
- [x] Onglet Apps/Sources : boutons Netflix/YouTube/Amazon/Disney/Hulu + `list`
- [x] Onglet TV : multi-TV (ajout, découverte, bascule d'alias, renommage, défaut)
- [x] Dialogue d'appairage (lecture PIN sur stdout → affichage → saisie →
      écriture sur stdin)
- [x] Panneau Journal (log de chaque commande envoyée)
- [x] Gestion du cas `status` qui échoue sans bloquer l'UI
- [x] Test complet sur la TV `salon`, sur ce poste (17/11) — build OK, tests
      automatisés OK, puis chaque fonction de SPEC §3 lancée sur la vraie TV via
      le Core et via l'UI (pilotée par UI Automation). Détail :
      `contexte/pyvidaa-comportement-reel.md`.
- [x] Corriger ce qui ne colle pas à la vraie TV (17/11) :
  - [x] Répertoire de travail fixe pour `tv.exe` (`config.yaml`/`tokens.json`)
  - [x] Liste des TV lue dans `config.yaml` (`config list/show` plantent)
  - [x] `config set-alias` : vraie syntaxe + contournement du bug d'alias
  - [x] Prompt PIN réel détecté ; annulation propre par `q`
  - [x] Sources réelles (boutons + `source list`) ; apps installées (`app list`)
  - [x] File d'attente des commandes TV (collision MQTT si simultanées)
  - [x] « Retrouver l'IP (via MAC) » quand le bail DHCP de la TV change
  - [x] Messages d'erreur lisibles dans la barre d'état (traces Python)
- [x] Validé par l'utilisateur devant la TV (17/11) : appairage avec le vrai PIN
      (jeton enregistré dans `~/.config/pyvidaa/tokens.json`), sources, power/on/off
      et le reste de l'app. Retour : **fonctionne mais lent**.
- [x] Moteur natif C# (17/11) : portage de pyvidaa (MQTT/TLS via MQTTnet,
      identifiants dynamiques, jetons, appairage, découverte, WoL, config),
      validé sur la TV (touches en 0–10 ms, listes, status, discover, appairage
      jusqu'au PIN). Voir `contexte/portage-natif-csharp.md`.
  - [x] Appairage natif avec un vrai PIN (17/11, 13:35) : jeton enregistré
        chiffré dans la base, `tokens.json` non recréé
  - [x] `off` puis `on` en natif testés par l'utilisateur (codes 0, 13:33)
  - [x] Touche `back` (17/11) : elle rembobine → bouton « Rembobiner » dans
        Lecture ; `returns` affiché « Retour » dans Menu
- [x] Base SQLite portable (17/11) : TV, jetons chiffrés DPAPI, réglages,
      historique du journal ; import unique de l'existant ; vérifiée sur la
      vraie base (jetons absents en clair). Voir `contexte/base-sqlite.md`.
  - [x] `tokens.json` (jetons en clair) supprimé le 17/11 ; l'app fonctionne
        avec la base seule (vérifié sur la TV)
- [x] **Interface** (17/11) — thème sombre « télécommande », MVVM pour l'écran
      principal, fenêtres « TV et réglages » et « Journal ». Voir
      `contexte/interface.md`.
  - [x] Logos officiels des apps (fournis par la TV, PNG ou JPEG, en cache)
  - [x] Sources : pas d'icône côté TV → nom d'affichage français de la TV
  - [x] Tous les boutons nommés en français (libellé visible + nom accessible)
  - [ ] Retours de l'utilisateur sur la nouvelle interface
- [x] **Python retiré de l'application** (17/11) : moteur C# seul, certificat
      importable depuis le `.p12` dans l'app, tests sur faux moteur en mémoire
- [ ] Lire le certificat client directement dans le **magasin de certificats
      Windows** (l'utilisateur l'y a importé le 17/11) : plus aucun fichier de
      clé sur le disque ; garder l'import `.p12` comme alternative
- [ ] Setup (installeur) et versionnage de l'application — certificat à
      importer au premier lancement, jamais embarqué (voir
      `contexte/certificat-client.md`)
- [x] **Sources TV et HDMI** (17/11) : corrigé (identifiants textuels « HDMI1 »…
      au lieu des numéros de pyvidaa), confirmé par l'utilisateur
- [ ] **Contrôles avancés (après l'interface)** :
  - [x] Volume à la molette de la souris (17/11) : au-dessus de la bascule
        Volume, ou Ctrl + molette partout ; 1 cran = 1 pas ; validé sur la TV
  - [x] Mode souris plein écran (17/11, F11 ; Échap pour sortir) : mouvements
        traduits en navigation, clics = OK/Retour/Accueil, molette = défilement
        (Ctrl = volume); sensibilité réglable (Maj + molette), calibrée
        sur la TV sans toucher la souris
  - [ ] Vrai pointeur libre de la TV (protocole inconnu, même piste APK)
  - [x] Pavé numérique du PC → chiffres 0-9 de la TV (17/11) ; rangée du haut,
        lettres et symboles (Maj/Verr. Maj comme d'habitude) → saisie de texte
  - [ ] **Saisie de texte : ne fonctionne pas** (testé dans YouTube le 17/11).
        L'interface est prête (frappe instantanée à chaque lettre), mais le
        TV annonce le contenu des champs VIDAA et du navigateur (écoute du
        17/11), mais refuse les 175 variantes d'envoi essayées ; APK chiffré.
        Pistes : vérifier l'app VIDAA du téléphone, clavier à l'écran piloté,
        lecture seule. Voir `contexte/saisie-texte.md`
  - [x] Raccourcis clavier (17/11) : flèches, Entrée = OK, Échap = Retour,
        Ctrl ± = zoom, volume, chaînes, couleurs, multimédia, F1 = aide ;
        validés sur la TV. Voir `contexte/interface.md`
  - [ ] Raccourcis configurables par l'utilisateur (table prête dans
        `KeyboardShortcuts`, à exposer et stocker en base)
- [ ] Réserver une IP fixe pour la TV sur la box/le routeur (MAC dans
      `CLAUDE.local.md`) — action utilisateur
- [x] Réduire la latence (17/11) : pont Python persistant (`vidaa_bridge.py` +
      `TvBridge`) pour touches/volume/app/source — voir
      `contexte/pont-python-persistant.md`. Testé sur la TV via l'UI.
  - [ ] Ressenti à confirmer par l'utilisateur devant la TV
- [x] ~~Packager `tv.exe`/`pyvidaa` avec PyInstaller~~ — devenu inutile : le
      moteur natif n'a pas besoin de Python (seul le certificat client reste à
      fournir sur chaque poste)
