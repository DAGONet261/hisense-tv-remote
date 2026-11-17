# Comportement réel de pyvidaa 2.3.0 (`tv.exe`) face à la 55A53FXVT

> **Historique** — Python (`tv.exe`, pyvidaa, pont persistant) a été **retiré de l'application le 17/11/2026** :
> tout passe par le moteur C# natif. Note conservée pour mémoire (comportements observés, raisons des choix).


**Date : 2026-11-17** — observé en lançant `tv.exe` sur la vraie TV `salon` et
en lisant le code source du paquet (`site-packages/pyvidaa/cli.py`,
`config/loader.py`, `config/storage.py`). Ce qui suit diffère de ce qu'on
supposait avant les tests.

## Fichiers de données : le répertoire courant compte

- `config.yaml` (liste des TV) est **cherché** d'abord dans le répertoire
  courant, puis dans `~/.config/pyvidaa/config.yaml`… mais il est toujours
  **écrit** dans le répertoire courant (`save_config` → `./config.yaml`).
- `tokens.json` (jetons d'appairage) est lu et écrit **uniquement** dans le
  répertoire courant.
- Conséquence : la config du 16/11 (alias `salon`) avait été écrite dans un
  dossier quelconque et était introuvable le 17/11 ; il a fallu refaire
  `config add`. L'app et `livrables/Invoke-HisenseTv.ps1` lancent donc
  toujours `tv.exe` depuis `%USERPROFILE%\.config\pyvidaa` (réglable dans
  l'onglet Journal de l'app).

## Bugs de pyvidaa 2.3.0 (contournés, paquet non modifié)

| Commande | Problème | Contournement dans l'app |
|---|---|---|
| `config list`, `config show` | Plantent (`TypeError: unhashable type: 'dict'`) dès qu'une TV est configurée : `list_tvs()` renvoie des dicts, `cmd_config` les prend pour des ID. | L'app lit `config.yaml` elle-même (`TvConfigFile`). |
| `--tv X config set-alias Y` | Ne résout pas l'alias : si `X` est un alias, crée une **entrée fantôme** `X` sans host. | L'app passe l'**identifiant** (clé de `config.yaml`, l'IP d'ajout) ; les entrées sans host sont ignorées. |
| `config set-default X` | Affiche « Default TV set to » même si `X` n'existe pas. | Rien (sans conséquence). |

## Syntaxes et sorties réelles

- `source <nom|id|list>` : noms courts `hdmi1 hdmi2 tv av`, ou n'importe quel
  identifiant de `source list` (`HDMI3`, `AVS`, `MusicSharing`…). Sortie de
  `list` : `  Nom (id: ID)`.
- `app <nom|list>` : en plus de `netflix youtube amazon disney hulu`,
  **n'importe quelle app installée** par son nom (insensible à la casse, ex.
  `"VIDAA Art"`). La 55A53FXVT en liste 30.
- `volume up|down|mute|set|get [n]` ; `get` répond le plus souvent
  « Could not get volume » (code 0).
- `config list` (quand il marche) : `  <id> (<alias>)`.
- `auth pair` : affiche `Starting pairing…`, se connecte, demande le PIN à
  la TV, **puis seulement** écrit `Enter the PIN (or 'q' to cancel): ` sans
  retour à la ligne. `q` annule proprement (code 1). Un seul essai.
- `status` : **fonctionne** le 17/11 (`statetype: remote_launcher`, ou
  `name/url/appId` quand une app tourne) — contrairement au 16/11. Le
  changement de source n'y apparaît pas.
- Alias inconnu : trace Python complète sur stderr, la dernière ligne
  (`ValueError: TV 'x' not found…`) est le message utile.
- Les commandes TV marchent **sans appairage** (`auth status` : « No stored
  credentials ») : l'authentification dynamique (MAC + horodatage) suffit sur
  ce firmware.

## Une seule connexion MQTT à la fois

Tous les `tv.exe` utilisent le même client-id MQTT. Deux commandes
simultanées → la TV ferme la première connexion (`code 7: connection lost …
another client is probably using the same MQTT client id`). L'app met donc
en file d'attente les commandes qui ciblent une TV (≈ 2 s chacune, une
connexion MQTT complète par commande).

## L'IP de la TV change, et pyvidaa ne la retrouve pas

Le 17/11 la TV est passée de `192.168.88.4` à `192.168.88.19` en pleine
session. `tv.exe` échoue alors (`Failed to fetch protocol info: timed out`,
puis `Failed to connect to TV` au bout de ~38 s) ; il n'a **pas** de
redécouverte automatique, contrairement à ce qui avait été cru le 16/11.
L'app propose « Retrouver l'IP (via MAC) » : `discover` (qui remplit la
table ARP), recherche de la MAC enregistrée dans `arp -a`, puis correction
de la ligne `host:` de `config.yaml`. Vraie solution : réservation DHCP.

## Découverte

`discover` met ~12 s et trouve aussi `192.168.88.216`, qui ne répond pas en
MQTT (TV éteinte ou autre appareil du réseau).
