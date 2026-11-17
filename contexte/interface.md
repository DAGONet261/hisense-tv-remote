# Interface (refonte du 17/11/2026)

Demandée par l'utilisateur (« trop basique »), style choisi : **télécommande
sombre**. Skill appliqué : `.claude/skills/wpf` (MVVM, ressources, threading).

## Organisation

- **Fenêtre principale** (`MainWindow`) : télécommande à gauche (rangée
  Marche/Arrêt · Muet · Accueil · Retour, pavé directionnel rond avec OK,
  bascules Volume/Chaîne encadrant Infos · Sous-titres · Menu · Quitter,
  Lecture, touches de couleur, puis repliables : pavé numérique, plus de
  touches, alimentation) ; à droite la grille des **applications avec leurs
  logos** et les **sources** ; en haut le choix de la TV et les
  boutons « TV et réglages » et « Journal ».
- **TV et réglages** (`TvWindow`) : TV active (liste, défaut, état, infos,
  renommer), réseau (recherche, retrouver l'IP, ajout), appairage,
  certificat client (état, import `.p12` ou PEM). Sortie complète à droite.
- **Journal** (`JournalWindow`) : historique (base), détail, copie, vidage.

## Règles de présentation

- **Chaque bouton a un nom français visible** (libellé sous le pictogramme)
  et un **nom accessible** (`AutomationProperties.Name`) — vérifié : 0 bouton
  sans nom sur 70. Le nom technique de la touche reste en info-bulle.
  Libellés centralisés dans `KeyCatalog.Labels`.
- Couleurs, formes et styles uniquement dans `Resources/Theme.xaml` ;
  pictogrammes : police système *Segoe Fluent Icons / Segoe MDL2 Assets*.

## Molette de la souris (volume)

- Molette **au-dessus de la bascule Volume** → volume ; **Ctrl + molette**
  n'importe où dans la fenêtre → volume (sans Ctrl, la molette fait défiler
  les listes, comme d'habitude).
- 1 cran (120) = 1 pas ; les fractions de cran des pavés tactiles sont
  cumulées. Le niveau affiché bouge tout de suite, puis la TV le confirme
  (une seule relecture par rafale). Validé sur la TV le 17/11 (8 → 10 → 8).

## Raccourcis clavier

- Table unique : `KeyboardShortcuts` (Core) — sert à la fois à l'exécution et
  à l'aide **F1** (fenêtre `ShortcutsWindow`, générée depuis la table).
- Flèches = navigation, Entrée = OK, Échap / Retour arrière = Retour,
  Origine = Accueil, Fin = Quitter, F2 = Menu, Page préc./suiv. = chaînes,
  Ctrl+↑/↓ ou +/− du pavé = volume, Ctrl+M = muet, Espace = lecture/pause
  (alterne, en commençant par pause), Ctrl + « + »/« − » = zoom, F3 = infos,
  F4 = sous-titres, Ctrl+1…4 = couleurs, touches multimédia, F5 =
  actualiser, Ctrl+J = journal, Ctrl+T = TV et réglages.
- **Lettres et chiffres seuls laissés libres** (saisie de texte et pavé
  numérique à venir). **Pas de raccourci Marche/Arrêt** (risque d'éteindre
  par erreur).
- Le zoom est reconnu par le **caractère** produit (AZERTY : « - » est sur la
  touche 6), via `MapVirtualKey`, pas par la position de la touche.
- Interceptés en `PreviewKeyDown` sur la fenêtre principale, avant les
  boutons (sinon Entrée/Espace cliqueraient le bouton ayant le focus).
- Validés sur la TV le 17/11 (Ctrl+↑/↓ : volume 13 → 14 → 13 ; Ctrl −/+ : zoom).

## Saisie de texte (recherches)

- Champ « Saisie de texte » en haut de la fenêtre. **Taper n'importe où**
  (lettre, rangée des chiffres, symbole ; Maj/Verr. Maj normaux) démarre une
  nouvelle saisie : le champ prend le focus.
- Texte envoyé **en entier** (`replace`) **à chaque lettre** (demande de
  l'utilisateur : « comme un clavier branché sur la TV ») ; si des lettres
  arrivent pendant un envoi, seul le dernier état part ensuite.
- ⚠ Le 17/11 rien n'apparaît dans la recherche YouTube : le format du
  message est à trouver (voir PLAN.md).
- **Entrée ou Échap** terminent la saisie (envoi final s'il y a du nouveau) :
  les flèches pilotent de nouveau la TV. Entrée n'envoie pas « OK » (risque
  d'appuyer sur une touche du clavier affiché par la TV).
- Pendant la saisie, les raccourcis sans Ctrl sont suspendus ; ceux avec
  Ctrl (volume, zoom…) restent actifs. Bouton ✕ = effacer ici et sur la TV.
- Pavé numérique hors saisie = chiffres 0-9 de la télécommande.
- Protocole : voir `portage-natif-csharp.md` (sujets `txtinputdata` et
  `bwsinputdata`).

## Mode souris (plein écran)

- Bouton « Mode souris » ou **F11** ; **Échap** (ou perte du focus) pour
  sortir. Curseur caché et ramené au centre après chaque mouvement (la souris
  ne bute jamais sur un bord) ; la position réellement atteinte sert de
  référence (précaution contre la mise à l'échelle Windows).
- Tests automatiques du mode souris : à faire **sans toucher la souris** —
  les « Haut/Bas » parasites du 17/11 venaient de la souris de l'utilisateur.
- **Sensibilité réglable** : Maj + molette (± 10 px par cran, de 30 à 300),
  affichée à l'écran et retenue en base (`Settings.MouseStepPixels`).
  Calibration du 17/11 sans toucher la souris : 200 unités de souris ≈ 3 pas à
  90 px (l'accélération du pointeur de Windows allonge les mouvements),
  2 pas à 120 px ; aucune touche parasite. Réglé à 120 px.
- Mouvement → navigation (1 pas / N px, axe dominant, `PointerToKeys`) ;
  clic gauche = OK ; clic droit / bouton « précédent » = Retour ; clic
  molette = Accueil ; bouton « suivant » = avance rapide ; molette =
  haut/bas ; Ctrl + molette = volume ; les autres raccourcis restent actifs.
- Le **vrai pointeur libre** de la TV n'est pas disponible : protocole
  inconnu (ni pyvidaa ni hisense_vidaa ne l'implémentent).

## Diagnostic

- « TV et réglages » → **Écouter la TV pendant 60 s** : la TV refuse les
  abonnements joker (`#`), on s'abonne un par un aux sujets connus et
  plausibles et on enregistre tout ce qui arrive.
- **Message brut** : envoi d'un sujet/contenu libre (`{client}` remplacé),
  pour les essais de protocole.

## Logos des applications

- Fournis **par la TV** : champ `httpIcon` de `app list`, de la forme
  `data:image/png;base64,iconDownloadhttps://img.vidaahub.com/…` (le préfixe
  dit PNG mais certains sont des **JPEG**). Téléchargés une fois dans
  `icons/` à côté de la base (`IconCache`), PNG/JPEG/GIF/BMP acceptés.
- Les **sources n'ont pas d'icône** (`httpIcon` vide) ; on affiche leur
  `displayname`, déjà en français (« Partage de musique »).

## Architecture

- `RemoteViewModel` (Core, CommunityToolkit.Mvvm) porte la logique de l'écran
  principal ; testé sans WPF (`ViewModelTests`). Les commandes de touches
  acceptent l'exécution concurrente (TvController sérialise les envois).
- `AppServices` regroupe base, réglages, contrôleur, ViewModel et journal.
- `TvWindow` et `JournalWindow` gardent du code-behind (formulaires simples,
  repris de l'ancienne interface) ; à passer en ViewModels si elles grossissent.
