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
| Release, version et setup Inno | `release-setup` | `version.txt`, `build/`, `installer/`, `src/Directory.Build.props`, `src/Directory.Build.targets`, `src/HisenseTvRemote/Resources/VidaaHisense.ico`, titres des fenêtres (`MainWindow.xaml(.cs)`, `App.xaml.cs`), `contexte/publication.md` | 2026-11-17 20:15 | Claude (session principale) |
