# Décision d'architecture : enveloppe Python plutôt que réimplémentation C#

**Date : 2026-11-16**

Choix fait explicitement par l'utilisateur, entre deux options proposées :
1. Enveloppe autour du script Python existant (`tv.exe`) — **retenu**
2. Réimplémentation native du client MQTT/TLS + algorithme d'identifiants
   dynamiques directement en C#

Raison : l'option 2 demanderait de porter une logique reconstruite
empiriquement ce soir (reverse engineering non documenté officiellement),
pour un gain incertain. L'option 1 réutilise un outil déjà fonctionnel et
validé (voir `../CLAUDE.local.md` pour les détails techniques).

À revisiter si un jour la dépendance Python devient un vrai problème
(déploiement sur un poste sans Python, lenteur du sous-processus, etc.).

## Mise à jour du 2026-11-17 : moteur natif C# retenu par défaut

L'option 2 a finalement été réalisée, à la demande de l'utilisateur, une fois
levé le risque qui l'avait fait écarter : pyvidaa est sous licence MIT et
lisible, le portage est une traduction vérifiable (test sur le vecteur
logcat de l'app officielle), et le protocole a été validé sur la vraie TV
entre-temps. Gains mesurés : touche en ~ms au lieu de ~2 s, plus besoin de
Python. L'enveloppe `tv.exe` reste disponible (case « Moteur natif » de
l'onglet Journal, `Settings.UseNativeEngine`) et sert de repli pour les
commandes non portées (`monitor`, `sniff`). Détail : `portage-natif-csharp.md`.

## Mise à jour du 2026-11-17 (soir) : Python retiré

À la demande de l'utilisateur, une fois toutes les fonctions portées en C# et
validées sur la TV : plus de `tv.exe`, de pont Python ni de lecture des
fichiers de pyvidaa. Vérifié avant retrait que pyvidaa n'apportait plus rien
(aucun sujet MQTT pour la souris ou la saisie de texte ; ses outils d'écoute
`monitor`/`sniff` se réécrivent en C# si besoin). Le certificat client se
convertit désormais dans l'app (import du `.p12`), sans Python ni OpenSSL.

