# Moteur natif C# (portage de pyvidaa)

**Date : 2026-11-17** — à la demande de l'utilisateur (« se passer de Python,
comme VIDAA l'a fait en Kotlin »). Remplace, par défaut, l'enveloppe autour
de `tv.exe` (voir `architecture-decision.md`, mise à jour du même jour).

## Ce qui a rendu le portage raisonnable

- pyvidaa est sous **licence MIT** et lisible localement : c'était une
  traduction, pas de la rétro-ingénierie. Mention de licence dans
  `THIRD-PARTY-NOTICES.md` (copiée à côté de l'exe).
- Cœur porté : ~3 200 lignes Python → `src/HisenseTvRemote.Core/Vidaa/`.
- L'algorithme d'identifiants dynamiques est vérifié par un test sur le
  vecteur relevé dans le logcat de l'app officielle (client_id, username,
  password identiques).

## Correspondance

| pyvidaa | C# |
|---|---|
| `credentials.py`, `protocol.py`, `config/constants.py`, `topics.py`, `keys.py` | `Vidaa/VidaaProtocol.cs` |
| `client.py` (paho-mqtt) | `Vidaa/VidaaClient.cs` (MQTTnet 4.3.7) |
| `config/storage.py` (`tokens.json`) | `Storage/SqliteStores.cs` (base SQLite, jetons chiffrés) |
| `discovery.py`, `wol.py` | `Vidaa/VidaaDiscovery.cs` |
| `cli.py` (commandes et textes) | `Vidaa/NativeTv.cs`, `Vidaa/NativePairingSession.cs` |
| `config/loader.py` (`config.yaml`) | `Storage/SqliteStores.cs` (table `tvs`) |
| `certs.py` (+ conversion `.p12` faite à la main) | `Vidaa/ClientCertificate.cs` (import `.p12` intégré) |

`NativeTv` accepte **les mêmes arguments que `tv.exe`** et produit des sorties
au même format (journal : préfixe `tv`). Les commandes non portées
(`monitor`, `sniff`, `nav`, options `--ip`/`--auth-mode`) sont refusées
proprement depuis le retrait de Python (17/11).

## Mesures sur la TV `salon` (17/11)

| Opération | tv.exe | pont Python | natif |
|---|---|---|---|
| Une touche | ~2 100 ms | quelques dizaines de ms | **0–10 ms** (1re : ~0,6–0,8 s, connexion TLS) |
| 6 touches d'affilée | ~13 s | < 1 s | **4 ms** |
| `source list` / `app list` | ~2,5 s | — | 0,5 s / 0,03 s |
| `volume get` | échoue le plus souvent | — | **répond** |
| `status` | ~6 s | — | ~1 s (5 s au tout premier appel) |
| Appairage jusqu'au PIN | ~1 s | — | 0,7 s |

## Améliorations par rapport à pyvidaa

- Jetons retrouvés **par MAC** après un changement d'IP (pyvidaa indexe par
  `IP:port` et les perd) ; l'ancienne entrée est remplacée à la sauvegarde.
- `config set-alias` résout l'alias (plus d'entrée fantôme) et `default_tv`
  suit le renommage ; `config list/show` ne plantent plus.
- `discover` ne garde que les TV VIDAA (lecture du descripteur UPnP) et
  donne les **deux MAC** (Ethernet, Wi-Fi) → « Retrouver l'IP » n'a plus
  besoin de la table ARP.
- `volume get` attend la réponse sur le bon sujet (le mécanisme générique de
  pyvidaa se faisait voler la réponse par d'autres messages).
- `status` : 1 s au lieu de 5 quand un état a déjà été diffusé.
- Pas de reconnexion automatique de la bibliothèque MQTT : plus de lutte de
  client-id avec un autre programme.

## À savoir

- **Certificat client** : toujours requis, jamais livré
  (`%APPDATA%\HisenseTvRemote\certs\vidaa_client.pem/.key`, ou
  `Settings.CertDirectory`), importable depuis le `.p12` de l'APK. Sous
  Windows, la clé PEM est réimportée en PKCS#12 (SChannel refuse une clé
  éphémère).
- **Touche `back`** : comme `tv key back`, elle envoie `KEY_BACK`, qui
  **rembobine** (vérifié devant la TV le 17/11). Le vrai retour arrière est
  `returns` (`KEY_RETURNS`). Dans l'UI : « Rembobiner » (groupe Lecture) et
  « Retour » (groupe Menu) — libellés dans `KeyCatalog.Labels`.
- **Sources** : la 55A53FXVT ignore les numéros de source de pyvidaa
  (`hdmi1` → « 3 », `tv` → « 0 ») et n'accepte que les identifiants textuels
  de `source list` (« HDMI1 », « TV », « AVS », « MusicSharing »). Signalé
  par l'utilisateur le 17/11 (seuls AV et Partage de musique marchaient,
  car non convertis) ; table `VidaaCatalog.SourceIds` corrigée.
- **Saisie de texte** (absente de pyvidaa) : essai de
  `{"text": "…", "action": "insert|replace|clear"}` sur
  `/remoteapp/tv/platform_service/{client}/actions/txtinputdata` **et**
  `…/actions/bwsinputdata` (repris de stevene1919/hisense_vidaa, MIT).
  **Sans effet dans YouTube sur la 55A53FXVT** (17/11). L'analyse de
  protocole de pyvidaa pense que ces sujets sont des notifications TV → app.
  Format réel à trouver dans l'APK VIDAA.
- **Abonnements joker refusés** : `#` et `/remoteapp/#` → refusés par la
  TV ; impossible d'observer ce que publie l'app officielle du téléphone.
- **Souris** : aucun projet connu (pyvidaa, hisense_vidaa) n'envoie de
  déplacement de pointeur ; seulement les touches `KEY_LEFTMOUSEKEYS`,
  `KEY_UDDLEFTMOUSEKEYS` (appui), `KEY_UDULEFTMOUSEKEYS` (relâche).
- Les **sources évoluent** : une clé USB ajoute « Media » (`Media0`) ; la
  liste est relue depuis la TV.
- Connexion gardée ouverte, fermée après 60 s d'inactivité ; keepalive 20 s.
- TV et jetons sont dans la base SQLite de l'app (jetons chiffrés DPAPI),
  voir `base-sqlite.md`.
