# Envoyer un lien YouTube à la TV (DIAL + API Lounge) — 18/11/2026

Équivalent du bouton « caster » de l'app YouTube mobile : on colle un lien de
vidéo sur le PC, YouTube s'ouvre sur la TV et la lit. Contourne en partie
l'absence de saisie de texte (on cherche sur le PC, la TV lit).

## Découverte

La TV annonce en SSDP `urn:dial-multiscreen-org:service:dial:1`
(`http://<tv>:3366/dd.xml`, en-tête `Application-URL:
http://<tv>:3367/apps/`). Applications DIAL : **YouTube** (403 sans en-tête
`Origin`, 200 avec `Origin: package:com.google.android.youtube`), **Netflix**,
**AmazonInstantVideo** ; pas de Disney+, Prime Video (nom moderne), Plex,
Spotify. Port 7000 (AirPlay) ouvert aussi, non exploité.

## Ce qui marche (vérifié devant la TV)

1. DIAL : `GET /apps/YouTube` (avec l'`Origin`) → `<state>running</state>` et
   `<additionalData><screenId>…`. Si arrêté : `POST /apps/YouTube` (201), puis
   le screenId apparaît en quelques secondes.
2. `POST https://www.youtube.com/api/lounge/pairing/get_lounge_token_batch`
   (`screen_ids=<screenId>`) → `loungeToken`.
3. `POST …/lounge/bc/bind?RID=1&VER=8&CVER=1` (en-tête
   `X-YouTube-LoungeId-Token`, `device=REMOTE_CONTROL`, `name`, `id`,
   `mdx-version=3`, `pairing_type=cast`, `app=android-phone-13.14.55`) →
   `["c","<SID>",…]` et `["S","<gsessionid>"]`.
4. `POST …/bc/bind?SID=…&gsessionid=…&RID=2&VER=8&CVER=1` :
   `req0__sc=setPlaylist`, `req0_videoId=<id>`, `req0_currentTime=0`,
   `req0_currentIndex=-1`, `req0_listId=`, `req0_audioOnly=false`, `count=1`
   → la vidéo démarre. `req0__sc=addVideo` : ajout à la file.

## Pièges rencontrés

- **DIAL seul ne suffit pas** : `POST /apps/YouTube` avec `v=<id>` ouvre
  YouTube sur le choix du profil, sans lancer la vidéo.
- **Tirets bas** : la commande prend deux tirets bas (`req0__sc`), ses
  paramètres **un seul** (`req0_videoId`). Avec deux, le lecteur s'ouvre sur
  un écran noir, sans vidéo.
- **Une seule session** : une nouvelle session (bind) par envoi, avec le même
  identifiant d'appareil, n'est acceptée que la première fois (la suite est
  ignorée). On garde la session et on numérote les demandes : `req0`, `req1`,
  `req2`… (RID +1 à chaque fois) ; session refusée (fermée par YouTube, TV
  redémarrée) → nouvelle session et second essai. Vérifié : lecture, ajout à
  la file, puis nouvelle lecture dans la même session.
- Nécessite Internet sur le PC (serveurs YouTube) ; le jeton de salon est
  gardé une heure en mémoire.

## Code

`Core/YouTube/YouTubeCaster.cs` (DIAL, Lounge, reconnaissance des liens :
watch, youtu.be, Shorts, live, embed, music.youtube.com, identifiant seul),
`YouTubeWindow.xaml(.cs)` (bouton **YouTube**, **Ctrl + Y** ; lien du
presse-papiers proposé d'office), tests `YouTubeTests.cs`.

## Pistes

- Netflix et Amazon par DIAL (lancement de l'app ; contenu précis non étudié).
- Commandes Lounge supplémentaires (pause, avance, volume YouTube) si utile.
