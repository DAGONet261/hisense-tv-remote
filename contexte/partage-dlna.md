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

## Points d'attention

- Première photo : ~15 s avant affichage (ouverture du lecteur de la TV).
- `Seek` exige que le serveur accepte les plages d'octets (`Range`) : géré
  (réponses 206, `Accept-Ranges`, `DLNA.ORG_OP=01`).
- Le lecteur DLNA de la TV est indépendant de la connexion MQTT : le partage
  fonctionne même si la télécommande n'est pas connectée, mais la TV doit
  être allumée.
- Tests : `DlnaTests.cs` (formats, plages, DIDL, description réelle de la TV,
  serveur HTTP sur la boucle locale, écran de partage avec un faux lecteur).
