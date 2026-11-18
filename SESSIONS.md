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
| Certificat intégré aux installeurs personnels | `certificat-integre` | `src/HisenseTvRemote.Core/Vidaa/ClientCertificate.cs`, `src/HisenseTvRemote/BundledCertificate.cs` (nouveau), `src/HisenseTvRemote/MainWindow.xaml.cs`, `src/Directory.Build.targets`, `.github/workflows/release.yml`, `src/HisenseTvRemote.Tests/CoreTests.cs`, `contexte/certificat-client.md`, `contexte/publication.md` | 2026-11-18 | Claude (session principale) |
