# Partager vers la TV (DLNA) — 18/11/2026

Afficher sur la TV des photos, vidéos et musiques du PC. Bouton **Partager**
(Ctrl + P) de la fenêtre principale, ou glisser des fichiers sur la
télécommande.

## Principe

Standard **DLNA / UPnP AV** (le même que l'app VIDAA du téléphone, qui embarque
`libmbldlna.so`) ; rien de propre à Hisense, rien de chiffré.

1. Le PC ouvre un petit **serveur HTTP** (`Dlna/MediaHttpServer.cs`) et y
   publie le fichier sous un jeton aléatoire.
2. Il donne cette adresse au **lecteur DLNA de la TV** (`Dlna/DlnaRenderer.cs`,
   service AVTransport) : `SetAVTransportURI` (avec métadonnées DIDL-Lite) puis
   `Play`.
3. La TV télécharge le fichier elle-même et l'affiche / le lit.

Lecteur de la 55A53FXVT : voir `capacites-tv.md` (SSDP `MediaRenderer:1`,
port 38400, formats acceptés).

## Fonctions

- Liste de fichiers : ajout par boîte de dialogue, par dossier, ou glisser-
  déposer (sur l'écran de partage ou sur la télécommande) ; doublons ignorés,
  formats non pris en charge signalés.
- Double-clic / Entrée : envoyer ; ← / → : précédent / suivant (en boucle).
- **Diaporama** (3 à 60 s par photo) ; le décompte part du moment où la TV
  affiche vraiment la photo (état PLAYING), ou au plus tard 20 s après l'envoi.
- Vidéo / musique : lecture/pause (Espace), stop, **−30/−10/+10/+30 s**
  (Maj/Ctrl + ← / →) par `Seek` REL_TIME — plus besoin de la touche de recul ;
  position affichée ; **enchaînement** au fichier suivant en fin de lecture
  (pas après un Stop demandé).
- Fermer la fenêtre arrête l'affichage sur la TV et le serveur local.

## Vidéos MP4/MOV : réorganisées à la volée (18/11)

**Symptôme** : vidéo de mariage (MP4 H.264 1080p30, ~10,8 Mbit/s = 1,35 Mo/s,
4,6 Go) qui saccade avec des micro-chargements, alors que PC (1 Gbit/s) et TV
sont en Ethernet.

**Mesures** (relevé de chaque transfert du serveur, `TransferCompleted`) :
le lecteur DLNA de la TV lit image et son **séparément** ; avec un MP4 rangé
par gros blocs (~0,3 s d'image puis le son : 262 Ko / 13 Ko, typique d'un
logiciel de montage), il revient sans cesse en arrière chercher le son, coupe
et rouvre sa connexion (337 demandes en 2 min) et ne tire que **1,06 Mo/s**
< 1,35 Mo/s requis → saccades. Le même contenu rangé finement (alternance
image/son paquet par paquet, comme FFmpeg) : **26 Mo d'avance d'un coup à
5 Mo/s**, puis 2,2-2,4 Mo/s, 95 demandes, sauts ±10/30 s immédiats — fluide
(confirmé à l'œil). Format MPEG-TS : fluide aussi, mais reprise après un saut
plus lente (2-4 s) → MP4 retenu.

**Solution** (`Dlna/Mp4Interleaver.cs`, `MediaContent.cs`) : sans recopier le
fichier, l'app sert une **vue réorganisée** : `ftyp` + `moov` réécrit (seules
les tables d'emplacement `stco`/`co64` et `stsc` changent : un échantillon
par bloc, rangés par ordre chronologique ; tailles, horodatages, images clés,
codecs inchangés) + `mdat` virtuel dont chaque morceau est lu à sa place dans
l'original. L'index est mis en tête (lecture immédiate, même pour un MP4 dont
le `moov` est à la fin).
- Vérifié sur le fichier réel : index recalculé en **0,7 s** (260 000
  morceaux, en-tête 3,1 Mo) ; empreintes FFmpeg (`-f framemd5`) **identiques
  paquet par paquet** à l'original sur les 5 premières minutes et de 50 min à
  la fin (avec saut).
- Pistes à taille fixe (PCM) : blocs d'origine conservés (sinon des millions
  d'entrées). Non réorganisés (envoyés tels quels) : MP4 fragmentés
  (`moof`), données externes (`dref`), `stz2`, fichiers incohérents.
- Les autres formats (MKV, AVI…) partent tels quels.

## Vidéos TS, MKV, AVI… : converties en MP4 (FFmpeg) — 18/11

- **Constat** (essais sur la 55A53FXVT) : un TS (H.264 864×488, pixels non
  carrés, enregistré depuis un site de streaming) et un MKV (VP9 4K + Opus)
  sont refusés (« fichier non pris en charge ») ; le **même contenu en MP4**
  se lit, **VP9 4K compris**, et aussi en WebM/MKV refait par FFmpeg. C'est
  l'enveloppe d'origine qui gêne le lecteur DLNA, pas les codecs.
- **Solution** (`Dlna/VideoRemuxer.cs`) : FFmpeg change l'enveloppe en MP4
  sans réencoder l'image (H.264, HEVC, VP9, MPEG-4/2 recopiés) ; son recopié
  (AAC, MP3, AC3, E-AC3) ou converti en AAC (Opus, Vorbis, DTS…). Autre codec
  d'image : message clair (réencodage en direct possible plus tard : la build
  contient les encodeurs H.264 matériels Intel/NVIDIA/AMD/Windows).
- **MKV refusés** (décision de l'utilisateur, 18/11) : message « utilisez une
  vidéo MP4 ou TS » à l'ajout. Ils se lisaient pourtant après conversion
  (≈ 2 min pour 3,3 Go en 4K) : pour les réactiver, retirer `.mkv` de
  `MediaTypes.Unsupported` et le remettre dans la liste des extensions.
  AVI, WMV, FLV : conversion gardée, **non vérifiée sur la TV** (pas de
  fichier d'essai).
- **Lire pendant la conversion : impossible avec cette TV** (essais du 18/11).
  Un flux sans taille (MP4 fragmenté ou MKV écrits au fil de l'eau) est
  refusé aussitôt ; avec une taille annoncée, la TV va d'abord lire la **fin
  du fichier** (index), absente tant que la conversion n'est pas finie, puis
  abandonne ; un MP4 fragmenté complet reste bloqué à 0:00. → **Préparation
  dès l'ajout à la liste**, en arrière-plan, une vidéo à la fois (« En
  attente », « Préparation 45 % », « Prête pour la TV ») ; une vidéo lancée
  avant d'être prête passe en tête et démarre seule à la fin ; Stop arrête
  l'attente (pas la préparation) ; retirer de la liste annule.
- Durées mesurées (disque D: ≈ 30 Mo/s) : film TS 714 Mo ≈ 25 s ; MKV 4K
  3,3 Go ≈ 1 min 30-2 min.
- Fichiers convertis : dossier temporaire effacé à la fermeture de l'écran de
  partage ; restes de plus de 6 h (plantage) effacés à l'ouverture suivante.
- FFmpeg 9.0.2 **LGPL v3** (build BtbN, DLL partagées) : `build/Get-FFmpeg.ps1`
  le récupère depuis la Release `outils-ffmpeg-9.0.2` du dépôt (copie figée,
  SHA-256 vérifié ; les builds datées de BtbN disparaissent), dans
  `third_party/ffmpeg/` (non versionné), copié dans `ffmpeg/` à côté de l'exe ;
  la compilation Release et le workflow le récupèrent s'il manque. Licence et
  sources : `THIRD-PARTY-NOTICES.md`. VLC n'apporterait rien de plus : il
  utilise les mêmes bibliothèques (libavcodec/libavformat) et n'envoie pas vers
  un lecteur DLNA.

## Photos : préparation (`PhotoPreparer.cs`, côté WPF)

La TV ignore l'orientation EXIF et ne lit ni HEIC ni WebP. Avant l'envoi, une
copie JPEG (qualité 90, dossier temporaire effacé à la fermeture) est faite si :
format HEIC/HEIF/WebP/TIFF/JXR, orientation ≠ normale, image plus grande que
3840 × 2160, ou fichier > 20 Mo. Sinon, l'original part tel quel.
HEIC : décodé par Windows (« Extensions d'image HEIF » du Microsoft Store) ;
sans elles, message explicite.

## Sécurité et réseau

- Seuls les fichiers publiés sont servis, sous un jeton aléatoire (le nom du
  fichier n'apparaît pas), et **uniquement à l'adresse IP de la TV** (403 pour
  les autres appareils). Les 3 derniers fichiers restent servis (la TV relit
  parfois l'avant-dernier), puis sont retirés.
- Port choisi par Windows ; adresse du PC = celle de l'interface qui mène à la
  TV.
- **Pare-feu** : l'installeur (mode administrateur) crée la règle entrante
  « VIDAA - Hisense (partage vers la TV) » : programme `HisenseTvRemote.exe`,
  TCP, réseau **privé**, **sous-réseau local** seulement ; supprimée à la
  désinstallation. Installation « pour moi seul » ou lancement depuis Visual
  Studio : Windows demande l'autorisation au premier partage (accepter pour
  les réseaux privés).
- Sur un réseau classé « Public », la règle ne s'applique pas : passer le
  réseau en « Privé » (Paramètres Windows → Réseau).
- **Installeur** (depuis 1.0.0.3) : case « Autoriser le partage vers la TV
  dans le pare-feu » (cochée par défaut). Installation pour tous : règle
  créée directement ; « pour moi seul » : confirmation administrateur (UAC)
  juste pour la règle, refus non bloquant. Vérification à la fin (règle
  présente et active pour l'exe installé, via HNetCfg.FwPolicy2) ; sinon
  message « l'application proposera de l'autoriser ». Réseau classé
  « Public » détecté (CurrentProfileTypes) : message pour passer en « Privé ».
  Désinstallation « pour moi seul » : la règle est retirée (UAC).
- **Application** : même contrôle à l'ouverture du partage ; bandeau adapté
  (réseau Public → bouton « Paramètres réseau », ms-settings:network-status ;
  règle absente → « Autoriser dans le pare-feu »).

## Points d'attention

- Première photo : ~15 s avant affichage (ouverture du lecteur de la TV).
- `Seek` exige que le serveur accepte les plages d'octets (`Range`) : géré
  (réponses 206, `Accept-Ranges`, `DLNA.ORG_OP=01`).
- Le lecteur DLNA de la TV est indépendant de la connexion MQTT : le partage
  fonctionne même si la télécommande n'est pas connectée, mais la TV doit
  être allumée.
- Tests : `DlnaTests.cs` (formats, plages, DIDL, description réelle de la TV,
  serveur HTTP sur la boucle locale, écran de partage avec un faux lecteur).
