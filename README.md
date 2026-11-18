# VIDAA - Hisense : télécommande Windows pour TV Hisense VIDAA

> **Projet indépendant, non affilié à Hisense.** « Hisense » et « VIDAA » sont
> des marques de Hisense ; elles ne sont utilisées ici que pour désigner les
> téléviseurs compatibles.

Application Windows (C#/WPF, .NET 8) qui pilote une TV Hisense VIDAA **sur le
réseau local**, sans cloud ni compte : télécommande complète, applications,
sources, et **partage de vos photos, vidéos et musiques vers la TV**.

## Fonctions

- **Télécommande** : navigation, volume (molette), chaînes, lecture, touches
  de couleur, pavé numérique, raccourcis clavier (F1 : aide), mode souris
  plein écran (F11).
- **Applications** (avec leurs logos) et **sources** (TV, HDMI, AV…).
- **Plusieurs TV**, appairage par **code PIN**, jetons chiffrés (DPAPI).
- **Partager vers la TV** (DLNA) : photos et diaporama, vidéos et musique
  avec barre de progression ; MP4 lus de façon fluide ; TS et AVI préparés
  automatiquement (FFmpeg) ; les MKV ne sont pas pris en charge.
- Journal de toutes les commandes envoyées.

Non disponible : saisie de texte à distance (protocole de la TV non public).

## Installation

1. Téléchargez `VIDAA-Hisense-Setup-<version>.exe` depuis les
   [Releases](https://github.com/DAGONet261/hisense-tv-remote/releases)
   (Windows 10/11, 64 bits ; .NET inclus).
2. Le PC et la TV doivent être sur le **même réseau local** (réseau
   « privé » dans Windows).
3. Au premier lancement, « TV et réglages » : ajoutez la TV, importez le
   **certificat client VIDAA**, puis appairez-la avec le code PIN affiché.

**Certificat client** : la TV n'accepte que le certificat de l'application
mobile officielle VIDAA. Il appartient à Hisense : il **n'est pas fourni**
avec ce logiciel. La documentation du projet
[pyvidaa](https://github.com/warrenrees/pyvidaa) explique comment l'obtenir
depuis l'application que vous possédez.

## TV testée

Hisense 55A53FXVT (VIDAA, 2026). D'autres modèles VIDAA utilisant le même
protocole devraient fonctionner ; vos retours sont bienvenus (Issues).

## Compiler

Visual Studio 2026 ou le SDK .NET récent, solution `src/HisenseTvRemote.slnx` :

```powershell
powershell -ExecutionPolicy Bypass -File build\Get-FFmpeg.ps1   # FFmpeg LGPL (partage des TS/AVI)
dotnet build src\HisenseTvRemote.slnx
dotnet test src\HisenseTvRemote.Tests
```

Une compilation **Release** incrémente `version.txt`, crée l'installeur
(Inno Setup 7) et déclenche la Release GitHub (voir `contexte/publication.md`).
Notes internes du projet : `MAP.md`, `PLAN.md`, `contexte/`.

## Crédits et licences

- Code : licence **MIT** (`LICENSE`).
- Protocole porté de [pyvidaa](https://github.com/warrenrees/pyvidaa) (MIT) ;
  informations de [hisense_vidaa](https://github.com/stevene1919/hisense_vidaa)
  (MIT) ; [MQTTnet](https://github.com/dotnet/MQTTnet) (MIT) ;
  [FFmpeg](https://ffmpeg.org) (LGPL v3, livré séparément dans `ffmpeg/`).
  Détails : `THIRD-PARTY-NOTICES.md`.
