# Capacités déclarées par la TV (requêtes `capability`, `gettvinfo`, `getdeviceinfo`)

**Date : 2026-11-17** — interrogation en lecture seule via pyvidaa
(`get_capability`, `get_tv_info`, `get_device_info`).

## Ce que la TV annonce

- Modèle `55A53FXVT` (pas 55A6H), firmware `V0000.09.09O.P1015`, puce
  `MTK9602`, plateforme lecteur `VD9602O_GL`, résolution `4k`,
  navigateur `odin`, UI `os_t.baseui.2.0.N0224`.
- `screen_capture_supported: 0`, `audio_capture_supported: 0` : **pas de
  capture d'écran ni d'audio** sur ce modèle/firmware. Le protocole VIDAA
  prévoit ces fonctions, mais elles sont désactivées ici → impossible
  d'afficher l'image de la TV sur le PC par ce canal (ni via pyvidaa, ni via
  un portage C#).
- `is_support_mouse: 1` : pilotage façon pointeur/souris possible (touches
  `leftmousekeys`, `uddleftmousekeys`…) — piste de fonction future.
- `is_support_long_press: 1` (touches `*_long_press`).
- `fake_sleep: 1` : veille « légère » — la TV peut rester joignable en MQTT
  écran éteint.
- Deux interfaces réseau (`eth0` et `wlan0`, MAC distinctes) ; l'IP
  rapportée (`.4`) différait de celle par laquelle on la joignait (`.19`).

## Alternatives pour voir l'image (hors protocole)

- Boîtier de capture HDMI USB + répartiteur, pour les sources externes
  (pas les apps intégrées ; contenus HDCP noirs).
- Webcam pointée sur l'écran.
- Sens inverse (PC → TV) : Miracast/Win+K, non vérifié sur ce modèle.

## Touches de lecture vérifiées (17/11/2026, lecteur USB et YouTube)

- **Rembobiner = `KEY_BACKS`** (avec un S, comme `KEY_FORWARDS`). Essai
  chronométré dans le lecteur USB, avec témoin : `KEY_BACKS` recule (x2,
  x4…), `KEY_REWIND`, `KEY_BACKWARDS`, `KEY_FASTBACKWARD`, `KEY_REW`,
  `KEY_BACKWARD`, `KEY_FASTBACK` ne font rien.
- **`KEY_BACK` ne rembobine pas**, contrairement à ce que supposent pyvidaa
  et les intégrations GitHub (et à notre note du 17/11 au matin) ; effet
  inconnu, gardé dans « autres touches ».
- `KEY_FORWARDS` avance (x2, x4, x8…) ; `KEY_PLAY`, `KEY_PAUSE`, `KEY_STOP`
  fonctionnent. `KEY_LEFT` recule aussi dans le lecteur USB, mais l'app
  utilise la touche dédiée.
- La TV accepte tout nom de touche sans répondre (QoS 0, pas d'accusé) :
  seul l'œil devant l'écran départage. Ne pas faire ces essais avec l'app
  ouverte : même identifiant MQTT, la TV coupe l'une des deux connexions.

## Partage vers la TV (DLNA) — vérifié le 17/11

- Lecteur DLNA standard : SSDP `MediaRenderer:1`, description
  `http://<tv>:38400/MediaServer/rendererdevicedesc.xml`, nom « TV Salon ».
  Services `AVTransport` (`/upnp/control/mingusavtr`), `RenderingControl`
  (`/upnp/control/mingusrcr`), `ConnectionManager`
  (`/upnp/control/minguscmr`). Port 18400 fermé sur ce modèle.
- Formats : photos JPEG, PNG, GIF, BMP, MPO ; vidéos MP4, MKV, WebM, AVI,
  MOV, MPEG-TS, WMV… ; audio MP3, FLAC, AAC, OGG, WAV, WMA.
- **Photo affichée avec succès** : PC = petit serveur HTTP, puis
  `SetAVTransportURI` (avec métadonnées DIDL-Lite) + `Play`. La TV vient
  chercher le fichier ~15 s après (ouverture de son lecteur). Il faut
  autoriser le port du PC dans le pare-feu Windows (réseau privé).

## DIAL (« caster » YouTube) — vérifié le 18/11

- SSDP `urn:dial-multiscreen-org:service:dial:1`, `http://<tv>:3366/dd.xml`,
  applications sur `http://<tv>:3367/apps/` : YouTube (avec l'en-tête
  `Origin: package:com.google.android.youtube`), Netflix, AmazonInstantVideo.
- Lecture d'une vidéo YouTube précise : voir `youtube-dial.md`.
