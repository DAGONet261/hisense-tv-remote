# SPEC.md — HisenseTvRemote (app Windows WPF/C#)

## 1. Objectif

Créer une application Windows (C#/WPF, cohérente avec `VaisonetConnecteurLauncher`)
pour piloter la TV Hisense 55A53FXVT (VIDAA) en réseau local, en couvrant **toutes**
les fonctionnalités que le protocole permet déjà, pas juste une télécommande
basique.

## 2. Architecture retenue

> **Mise à jour 17/11/2026** : l'app utilise un **moteur natif C#** (portage de
> pyvidaa, MQTT direct) et **Python a été entièrement retiré** — voir
> `contexte/portage-natif-csharp.md`. La section ci-dessous (enveloppe
> `tv.exe`) décrit l'architecture d'origine, conservée pour mémoire. La
> limite « non portable sans Python » (§5) ne s'applique plus : seul le
> certificat client reste à importer sur chaque poste.

**Enveloppe autour de l'outil Python existant** (`tv.exe`, du paquet `pyvidaa`),
appelé en sous-processus depuis l'app WPF via `System.Diagnostics.Process`.

Raison : l'authentification de ce firmware VIDAA récent nécessite un certificat
client extrait de l'app officielle + un algorithme d'identifiants dynamiques
(horodatage). Cette logique est déjà résolue et fonctionnelle côté Python ;
la réimplémenter en C# serait un travail de reverse engineering redondant,
pour un bénéfice incertain. Ne pas repartir de zéro sur ce point.

Détails techniques de l'environnement : voir `CLAUDE.local.md` (chemins exacts,
versions, certificat).

## 3. Fonctionnalités à couvrir

### 3.1 Télécommande complète

Toutes les touches listées par `tv.exe keys` :

| Catégorie | Touches |
|---|---|
| Power | `power` |
| Navigation | `up`, `down`, `left`, `right`, `ok` |
| Menu | `menu`, `home`, `returns` (« Retour »), `exit` |
| Volume | `volumeup`, `volumedown`, `mute` |
| Lecture | `play`, `pause`, `stop`, `back` (« Rembobiner » : `KEY_BACK` rembobine, vérifié le 17/11) |
| Chiffres | `0` à `9` |
| Chaînes | `channelup`, `channeldown` |
| Couleurs | `red`, `green`, `yellow`, `blue` |
| Autres | `channeldot`, `forwards`, `info`, `leftmousekeys`, `mute_long_press`, `ok_long_press`, `subtitle`, `uddleftmousekeys`, `uduleftmousekeys`, `voicedown`, `voiceup`, `zoomin`, `zoomout` |

Appel : `tv.exe --tv <alias> key <nom_touche>`
(le volume a aussi ses propres sous-commandes : `tv.exe --tv <alias> volume up|down`)

### 3.2 Applications

`tv.exe --tv <alias> app <nom>` avec `<nom>` dans :
`netflix`, `youtube`, `amazon`, `disney`, `hulu`, ou `list` (liste les apps
disponibles sur la TV — utile pour enrichir les raccourcis plus tard).

### 3.3 Sources / entrées

Commande `source` (alias `input`) : `tv.exe --tv <alias> source <hdmi1|hdmi2|tv|av|list>`,
ou un identifiant renvoyé par `source list` (ex. `HDMI3`, `AVS`) — vérifié le 17/11.

### 3.4 Multi-TV

Gérer plusieurs téléviseurs/alias via les sous-commandes `config` :
- `tv.exe config show` — afficher toutes les TV configurées
- `tv.exe config list` — lister les identifiants
- `tv.exe config add <ip> --alias <nom>` — ajouter une TV
- `tv.exe config set-default <alias>`
- `tv.exe --tv <id> config set-alias <nouveau>` (syntaxe réelle, vérifiée le 17/11 ;
  `config list/show` et `set-alias` ont des bugs dans pyvidaa 2.3.0 — voir
  `contexte/pyvidaa-comportement-reel.md`)

Et la découverte réseau :
- `tv.exe discover` (alias `scan`) — trouver les TV sur le réseau local

L'app doit permettre d'ajouter une nouvelle TV, de lancer la découverte, et
de basculer entre TV configurées.

### 3.5 Appairage / authentification

Sous-commandes `auth` :
- `tv.exe --tv <alias> auth status` — état du jeton
- `tv.exe --tv <alias> auth pair` — **interactif** : affiche un code PIN sur
  l'écran de la TV, puis attend la saisie de ce code en entrée standard.
  Depuis l'app WPF : lire le PIN demandé sur stdout, l'afficher dans l'UI,
  laisser l'utilisateur le saisir dans un champ, puis l'écrire sur stdin du
  sous-processus.
- `tv.exe --tv <alias> auth refresh`
- `tv.exe --tv <alias> auth clear`

L'app doit permettre d'appairer une nouvelle TV depuis l'interface (dialogue
avec champ PIN), pas seulement en ligne de commande.

### 3.6 État en direct

`tv.exe --tv <alias> status`

⚠️ Ne fonctionne pas de façon fiable sur ce modèle/firmware (voir
`CLAUDE.local.md`). L'app doit afficher le résultat tel quel (y compris
l'échec) sans le traiter comme une erreur bloquante.

### 3.7 Allumage / extinction

- `tv.exe --tv <alias> wake` — Wake-on-LAN
- `tv.exe --tv <alias> on` — wake + attente
- `tv.exe --tv <alias> off` — vérifie l'état puis éteint

### 3.8 Journal des commandes

Panneau de log dans l'UI affichant, pour chaque action : la commande exacte
envoyée à `tv.exe`, son code de sortie, sa sortie standard et d'erreur.

### 3.9 Hors scope v1 (à garder en tête)

- `tv.exe --tv <alias> monitor` — surveiller les messages MQTT bruts
- `tv.exe --tv <alias> sniff` — capturer tout ce que la TV publie

## 4. Suggestion de structure de projet C#

```
HisenseTvRemote/
├── HisenseTvRemote.csproj       (WPF, net8.0-windows)
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / .xaml.cs    (TabControl : Télécommande / Apps-Sources / TV / Journal)
├── TvController.cs               (wrapper Process async autour de tv.exe)
├── Models/
│   └── TvProfile.cs
├── Settings.cs                   (chemin vers tv.exe, configurable)
```

`TvController` doit :
- localiser `tv.exe` (chemin connu, avec repli sur recherche dans le PATH)
- exécuter les commandes de façon asynchrone (`Process` + redirection stdout/stderr)
- gérer le cas particulier `auth pair` (processus longue durée, écriture sur stdin)
- ne jamais bloquer l'UI thread

## 5. Limite connue

L'app n'est **pas portable telle quelle** sur un poste sans Python 3.12 +
`pyvidaa` installés (voir `CLAUDE.local.md`). Amélioration future possible :
packager `tv.exe` (et son venv) en exécutable autonome avec PyInstaller.
