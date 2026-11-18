# SESSIONS.md — zones réservées par les sessions en cours

Règle complète : `CLAUDE.md` → « Sessions en parallèle ». En bref : avant de
modifier quoi que ce soit, ajouter sa ligne ici et l'envoyer sur `main` ;
à la fin, après fusion, retirer sa ligne.

Une zone = les fichiers ou dossiers que la session va modifier. Les fichiers
partagés (`PLAN.md`, `MAP.md`, `CLAUDE.md`, `.gitignore`, `*.csproj`,
`HisenseTvRemote.slnx`, `Resources/Theme.xaml`) ne se réservent pas : on ne
les modifie qu'au moment de fusionner.

| Sujet | Branche | Zone réservée (fichiers / dossiers) | Depuis | Par |
|---|---|---|---|---|
| Workflow GitHub de Release | `workflow-release` | `.github/workflows/release.yml` (nouveau), `build/New-ReleaseNotes.ps1` (nouveau), `build/Push-ReleaseTag.ps1` (remplace `build/Publish-GitHubRelease.ps1`), `build/Publish-Release.ps1`, `src/Directory.Build.targets`, `contexte/publication.md` | 2026-11-18 | Claude (session principale) |
