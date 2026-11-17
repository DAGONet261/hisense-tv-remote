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
