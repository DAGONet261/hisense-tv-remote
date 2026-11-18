# Certificat client VIDAA : nature, durée de vie, distribution

**Date : 2026-11-17** — question de l'utilisateur avant le futur setup et le
versionnage : « ce certificat peut-il changer ? peut-on le mettre dans les
fichiers du projet ? ».

## Ce qu'il est

- Certificat **et clé privée** que la TV exige pour le TLS mutuel (port 36669).
- Extrait de l'app officielle VIDAA (`res/raw/client_mobile_android.p12`,
  `res/3R.p12` dans les versions récentes), protégé par un mot de passe
  (dans `.env`, `VIDAA_P12_PASSWORD`).
- Lu le 17/11 : sujet `CN=VidaaAppAndroidV01, OU=multimedia, O=hh`, émis par
  `CN=RemoteCA` (Hisense, Qingdao), **valide du 2024-06-27 au 2034-06-25**.

## Peut-il changer ? Oui, même avant 2034

- Le « V01 » du nom suggère des versions : une mise à jour de l'app VIDAA
  peut livrer un nouveau certificat (le nom du fichier a déjà changé).
- Une mise à jour du firmware de la TV peut cesser d'accepter celui-ci
  (déjà vu : un certificat communautaire d'un modèle plus ancien est
  accepté en TLS mais refusé en MQTT sur la 55A53FXVT).
- Il expirera de toute façon le 2034-06-25.
→ C'est une **donnée de configuration remplaçable**, pas du code.

## Pourquoi ne pas le mettre dans le projet ni dans le setup

1. **Ce n'est pas le nôtre** : clé privée appartenant à Hisense, extraite
   de leur app. Pyvidaa refuse lui-même de la distribuer. L'embarquer dans un
   installeur = la redistribuer à chaque copie du setup.
2. **C'est un secret** (clé privée) : la règle du projet est « secrets
   uniquement dans `.env`, jamais ailleurs ». Une fois committé, un fichier
   reste dans l'historique Git même après suppression.
3. **Il peut changer** : figé dans une version de l'app, son remplacement
   obligerait à publier une nouvelle version ; importé, il se remplace en
   deux clics sans réinstaller.

## Ce qui est en place (et à reprendre dans le setup)

- L'app range le certificat par poste et par compte dans
  `%APPDATA%\HisenseTvRemote\certs\` (hors dossier d'installation, donc
  conservé lors des mises à jour et des désinstallations/réinstallations).
- **Import intégré** : « TV et réglages » → « Importer le certificat
  (.p12)… » + mot de passe (ou paire `.pem`/`.key`). Plus besoin de Python
  ni d'OpenSSL.
- Au démarrage, si le certificat manque, la barre d'état le signale.
- Pour le setup : prévoir une page facultative « Importer le certificat
  VIDAA » qui appelle le même import, ou laisser l'app le demander au
  premier lancement. Pour un déploiement interne sur plusieurs postes : le
  `.p12` peut être lu depuis un partage réseau protégé au moment de
  l'installation, sans jamais entrer dans le dépôt ni dans le paquet.

## Certificat intégré aux installeurs personnels (18/11/2026)

Demande de l'utilisateur : ne plus importer le `.p12` à chaque installation sur
ses postes. Le dépôt et les installeurs GitHub étant **publics**, le
certificat (clé privée d'Hisense) n'y va **jamais** ; il est intégré seulement
dans les installeurs compilés sur un poste qui l'a :

- Poser le fichier dans `certsidaa_client.p12` (dossier ignoré par Git) ; le
  mot de passe reste dans `.env` (`VIDAA_P12_PASSWORD`).
- La compilation (`src/Directory.Build.targets`, cible `HtrBundleCertificate`)
  les intègre à l'exe comme ressources ; message « installeur PERSONNEL : ne
  pas le partager ». Désactiver : `-p:HtrBundleCertificate=false`.
- Au démarrage (`BundledCertificate.cs` → `ClientCertificate.InstallBundled`) :
  installé dans `%APPDATA%\HisenseTvRemote\certs` s'il manque ; remplacé s'il
  est différent et au moins aussi récent (nouvelle version de l'app Android) ;
  jamais rétrogradé.
- **Nouveau certificat** (mise à jour de l'app Android) : remplacer
  `certsidaa_client.p12` et le mot de passe dans `.env`, recompiler,
  réinstaller.
- **Garde-fou** : le workflow GitHub refuse de publier un installeur qui
  contiendrait un certificat intégré. L'installeur **local** (`dist\`) le
  contient : ne pas le partager.
- Vérifié : intégré, installé au premier démarrage, rien refait au second,
  empreinte identique au certificat déjà en place.
