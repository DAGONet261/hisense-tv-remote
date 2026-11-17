# CLAUDE.md

> ⚠️ Les sections « Qui tu es », « Ce que fait ce projet » et « Comment je
> dois travailler avec toi » sont **en attente de remplissage** (5 questions,
> une à la fois). La section « Règles à respecter » est, elle, en vigueur.

## Qui tu es

_À remplir._

## Ce que fait ce projet

_À remplir._

## Comment je dois travailler avec toi

_À remplir._

## Règles à respecter

### Sessions en parallèle — « une session = une branche dans son propre dossier, et une zone réservée »

Validée par l'utilisateur le 17/11/2026. Plusieurs sessions (Claude Code ou
humain) peuvent travailler en même temps sans se gêner si chacune suit ceci :

1. **Sa propre branche, dans son propre dossier.** Jamais de travail direct
   sur `main`. Créer un dossier de travail séparé :
   `git worktree add ../hisense-tv-remote-<sujet> -b <sujet>`
   (ex. `<sujet>` = `saisie-texte`), et y travailler.
2. **Réserver sa zone avant de modifier quoi que ce soit** : ajouter une ligne
   dans `SESSIONS.md` (sujet, branche, fichiers/dossiers concernés, date) et
   l'enregistrer sur `main` (`git pull`, modifier, `git commit`, `git push`)
   avant de commencer. Si la zone voulue est déjà réservée par une autre
   session : ne pas y toucher, demander à l'utilisateur.
3. **Fichiers partagés** (`PLAN.md`, `MAP.md`, `CLAUDE.md`, `.gitignore`,
   `*.csproj`, `HisenseTvRemote.slnx`, `Resources/Theme.xaml`) : ne les
   modifier **qu'au moment de fusionner**, une session à la fois.
4. **Fusionner dans `main` seulement quand la compilation et les tests
   passent** (`dotnet build src/HisenseTvRemote.slnx` puis
   `dotnet test src/HisenseTvRemote.Tests`) ; faire `git pull` sur `main`
   juste avant, fusionner, `git push`, puis **libérer la zone** dans
   `SESSIONS.md` et supprimer le dossier de travail
   (`git worktree remove ../hisense-tv-remote-<sujet>`).

### Secrets et contenus tiers

- Secrets **uniquement dans `.env`** (jamais committé) ; vérifier avant chaque
  envoi qu'aucun secret, certificat (`*.p12`, `*.pem`, `*.key`…), base
  (`*.db`) ni contenu de l'APK VIDAA (`sources/`) n'est suivi par Git
  (`.gitignore` les exclut déjà).
