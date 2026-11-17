# hisense-tv-remote

Application Windows (C#/WPF) pour piloter la TV Hisense 55A53FXVT (VIDAA) en
réseau local, via le protocole MQTT local reconstruit le 16/11/2026.

## Pour s'orienter

- **`CLAUDE.md`** — qui travaille sur ce projet, comment, et les règles à
  respecter (en cours de remplissage).
- **`CLAUDE.local.md`** — détails techniques propres à ce poste (chemins,
  versions, certificat). Ne pas partager hors de cette machine.
- **`SPEC.md`** — ce qu'on veut obtenir.
- **`PLAN.md`** — les étapes.
- **`MAP.md`** — la carte complète du projet, tenue à jour à chaque ajout.
- **`contexte/`** — ce qu'on a appris, une note par sujet.
- **`sources/`** — documents bruts déposés par l'utilisateur.
- **`livrables/`** — ce qui a été produit (scripts, futurs exports de l'app).
- **`scripts/`** — automatisations du projet (vide pour l'instant).

## Démarrage rapide

Application 100 % C#/WPF (.NET 8) : elle parle directement à la TV en MQTT
(moteur natif, sans Python). Ouvrir `src/HisenseTvRemote.slnx`. Au premier
lancement, importer le certificat client VIDAA (« TV et réglages ») — voir
`contexte/certificat-client.md`. Étapes suivantes : `PLAN.md`.
