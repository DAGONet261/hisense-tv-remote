# Ce qu'on sait du protocole VIDAA (MQTT local)

**Date : 2026-11-16** — reconstruit ce soir à partir de sources communautaires
(non documenté officiellement par Hisense pour ce qui suit).

## Transport

- MQTT v3.1.1 sur TLS mutuel, port **36669**.
- Vérification du nom d'hôte désactivée côté client (le certificat serveur
  de la TV est émis pour `CN=127.0.0.1`, pas pour son IP réelle).
- Autorité `RemoteCA` (auto-signée), valide jusqu'en 2043.

## Authentification — deux couches

1. **TLS mutuel** : la TV exige un certificat client. Celui du dépôt
   communautaire `d3nd3/Hisense-mqtt-keyfiles` (prévu pour un modèle plus
   ancien, u7qf) a été accepté côté TLS mais rejeté côté MQTT (`CONNACK 5`,
   "not authorised") sur ce firmware. Le certificat officiel, extrait de
   l'APK de l'app VIDAA, a aussi été accepté côté TLS mais pas suffisant
   seul.
2. **Identifiants MQTT dynamiques** : sur les firmwares récents, les
   identifiants statiques (`hisenseservice` / `multimqttservice`) ne
   suffisent plus. Un schéma d'identifiants dérivés (device UUID +
   horodatage) est nécessaire. C'est ce que `pyvidaa` (`tv.exe`) implémente
   avec son option `--auth-mode {auto,dynamic,static}` — c'est la pièce qui
   a débloqué tout ce soir.

## Appairage par PIN

Flux observé/documenté par la communauté :
1. Connexion MQTT (TLS mutuel + identifiants).
2. Publication sur `/remoteapp/tv/ui_service/{client_id}/actions/vidaa_app_connect`.
3. La TV affiche un code à 4 chiffres sur son écran et publie sur
   `…/ui_service/data/authentication`.
4. Le client publie `{"authNum":"XXXX"}` sur
   `…/ui_service/actions/authenticationcode` dans les ~60 secondes.
5. La TV renvoie un jeton d'accès (30 jours) et un jeton de rafraîchissement
   (365 jours) sur `…/ui_service/data/tokenissuance`.

`pyvidaa` gère tout ce flux via `tv.exe auth pair`.

## Récupération du certificat officiel

- APK de l'app VIDAA → fichier `.p12` dans `res/raw/client_mobile_android.p12`
  (ou `res/3R.p12` sur les builds plus récents, nom obscurci).
- Mot de passe du conteneur : dans `.env` (`VIDAA_P12_PASSWORD`) (trouvé via
  une analyse communautaire du protocole, jamais confirmé officiellement).
- Conversion en PEM : nécessite une bibliothèque qui gère l'ancien format de
  chiffrement PKCS12 (RC2/3DES) — OpenSSL 3.x en CLI a besoin du provider
  "legacy" (souvent absent), la bibliothèque Python `cryptography` s'en
  passe.

## Limites connues

- Hisense ne propose aucun contrôle IP tiers officiel (confirmé par leur
  support sur un cas communautaire similaire).
- `status`/`gettvstate` ne répond pas de façon fiable sur toutes les TV —
  pas forcément un signe d'échec de l'authentification.
