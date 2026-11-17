# Base SQLite portable de l'application

**Date : 2026-11-17** — demandée par l'utilisateur (« garder le refresh token et
toutes les données nécessaires dans une base SQLite portable »).

## Emplacement

- `HisenseTvRemote.db` **à côté de l'exécutable** si le dossier est
  inscriptible (mode portable : clé USB, dossier copié) ;
- sinon `%APPDATA%\HisenseTvRemote\HisenseTvRemote.db` (installation dans
  Program Files).
- Chemin affiché dans l'onglet Journal (« Base de l'app »). Exclu de Git
  (`*.db`, `*.db-wal`, `*.db-shm` dans `.gitignore`).

## Contenu (schéma v1, `PRAGMA user_version`)

| Table | Contenu |
|---|---|
| `tvs` | TV : id (IP d'ajout), host, port, alias (unique), nom, MAC Ethernet, MAC Wi-Fi, marque, mode d'auth, version de protocole, TV par défaut |
| `tokens` | Jetons d'appairage par host:port + MAC ; **access/refresh token chiffrés DPAPI** ; dates d'expiration, client_id et nom d'utilisateur MQTT |
| `settings` | Réglages de l'app (`app.settings`, JSON) ; marqueur d'import |
| `journal` | Historique des commandes (2 000 dernières), rechargé au démarrage (200 dernières affichées) |

## Sécurité des jetons

- Chiffrés avec **DPAPI, portée « utilisateur courant »**, plus une entropie
  propre à l'app. Vérifié sur la vraie base : les jetons n'y figurent pas
  en clair (blobs de 294 octets).
- Base copiée sur un autre poste ou ouverte sous un autre compte Windows :
  TV et réglages suivent, les jetons sont illisibles → traités comme absents,
  il suffit de refaire l'appairage par PIN. C'est voulu.
- Ce ne sont pas des secrets du projet (générés par la TV à l'usage) : ils
  n'ont pas leur place dans `.env` ; la règle « secrets uniquement dans
  `.env` » reste respectée pour les secrets de développement.
- `tokens.json` de pyvidaa (jetons **en clair**) a été **supprimé le 17/11**
  à la demande de l'utilisateur, après vérification que la base contenait
  le même jeton valide. Conséquence : en mode Python (case décochée), il
  faut refaire l'appairage, et pyvidaa recréera alors un `tokens.json` en
  clair. Le moteur natif avec base n'écrit jamais ce fichier.

## Import de l'existant (supprimé)

Un import unique depuis les fichiers de pyvidaa (`config.yaml`, `tokens.json`,
`settings.json`) a servi à la migration le 17/11, puis a été retiré avec
Python : la base est désormais la seule source.

## Sans base

Si la base ne peut pas être ouverte, l'app affiche un message et s'arrête
(plus de repli sur des fichiers depuis le retrait de Python).

## Code

`src/HisenseTvRemote.Core/Storage/` : `AppDatabase` (emplacement, schéma,
réglages, journal, DPAPI), `SqliteStores` (TV, jetons), `IStores`
(interfaces + implémentation `config.yaml`), `PyvidaaImport`.
Dépendances : Microsoft.Data.Sqlite 8.0.11,
System.Security.Cryptography.ProtectedData 8.0.0.
