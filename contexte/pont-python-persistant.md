# Pont Python persistant (`vidaa_bridge.py`)

> **Historique** — Python (`tv.exe`, pyvidaa, pont persistant) a été **retiré de l'application le 17/11/2026** :
> tout passe par le moteur C# natif. Note conservée pour mémoire (comportements observés, raisons des choix).


**Date : 2026-11-17** — réponse au retour utilisateur « ça marche mais c'est lent ».

## Mesure (pourquoi `tv.exe` est lent)

Pour une touche via `tv.exe` : ~2,1 s au total, dont

| Étape | Durée |
|---|---|
| Démarrage de `tv.exe` + import de pyvidaa | ~0,8 s |
| Création du client + connexion MQTT/TLS | ~0,3 s |
| **Envoi de la touche** | **~0 ms** |
| Pause fixe dans `cli.py` après l'envoi | 0,3 s |
| Déconnexion | ~0,6 s |

La TV n'est pas lente : c'est le cycle « lancer Python, se connecter, se
déconnecter » répété à chaque clic. En plus, la file d'attente (obligatoire,
voir `pyvidaa-comportement-reel.md`) additionne ces 2 s quand on clique vite.

## Solution

`src/HisenseTvRemote.Core/vidaa_bridge.py`, lancé une fois par l'app
(`TvBridge`), garde la connexion MQTT ouverte et reçoit les commandes en JSON
ligne à ligne sur stdin. Mesuré sur la TV : quelques dizaines de ms par
touche une fois connecté (première commande ~1 s, ~0,4 s après une
reconnexion).

- **Commandes via le pont** : `key`, `volume up|down|mute`, `app <nom>`,
  `source <nom>`. Tout le reste (listes, status, auth, config, discover,
  wake/on/off) passe encore par `tv.exe`.
- **Même client-id que `tv.exe`** : avant chaque `tv.exe` qui cible la TV, l'app
  envoie `release` au pont (il ferme sa connexion), dans la même file
  d'attente. La reconnexion automatique de paho est coupée dans le pont,
  sinon il volerait la connexion d'un `tv.exe` (ou du script PowerShell).
- **Connexion fermée après 60 s d'inactivité** : la suivante se reconnecte, ce
  qui vérifie que la TV est toujours là (une TV éteinte sans fermer la socket
  laisserait publier dans le vide).
- **`config.yaml` modifié** (IP retrouvée, alias renommé) : le pont s'en
  aperçoit (date de modification) et recrée ses clients.
- **Repli** : pont introuvable, planté ou muet pendant 20 s → la commande part
  par `tv.exe` ; après 3 échecs de suite le pont est abandonné pour la session.
  Une réponse négative du pont (TV injoignable…) n'est pas renvoyée par
  `tv.exe`, pour ne pas envoyer deux fois une touche.
- **Python utilisé** : `python.exe` de l'installation qui fournit `tv.exe`
  (`…\Python312\python.exe`), lancé avec `-I -u` ; réglable
  (`Settings.PythonPath`, `Settings.UseBridge = false` pour revenir à `tv.exe`).
- Dans le journal, ces commandes apparaissent avec le préfixe `[pont]`.
