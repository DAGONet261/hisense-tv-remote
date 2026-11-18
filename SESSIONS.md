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
| Partager vers la TV (DLNA) | `partage-dlna` | `src/HisenseTvRemote.Core/Dlna/` (nouveau), `src/HisenseTvRemote.Core/ViewModels/ShareViewModel.cs` (nouveau), `src/HisenseTvRemote.Core/TvController.cs`, `src/HisenseTvRemote.Core/KeyboardShortcuts.cs`, `src/HisenseTvRemote/ShareWindow.xaml(.cs)` (nouveau), `src/HisenseTvRemote/PhotoPreparer.cs` (nouveau), `src/HisenseTvRemote/MainWindow.xaml(.cs)`, `src/HisenseTvRemote.Tests/DlnaTests.cs` (nouveau), `installer/VidaaHisense.iss`, `contexte/partage-dlna.md` (nouveau) | 2026-11-18 | Claude (session principale) |
