# Saisie de texte à distance : ce qu'on sait (écoutes du 17/11/2026)

Objectif : taper au clavier du PC dans une zone de recherche de la TV
(YouTube, recherche VIDAA, TV Browser). **Pas encore résolu.**

## 1. La TV annonce le contenu des zones de saisie (confirmé)

Écoute de la TV pendant que l'utilisateur tapait avec la télécommande :

| Zone | Sujet diffusé par la TV | Contenu (texte complet à chaque lettre) |
|---|---|---|
| Recherche de l'accueil VIDAA | `/remoteapp/mobile/broadcast/ui_service/data/txtinputdata` | `{"vkb_status":1,"input_data":"Nightwish Ghost love score"}` |
| TV Browser (barre d'adresse) | `/remoteapp/mobile/broadcast/platform_service/actions/bwsinputdata` | `{"vkbstatus":1,"inputdata":"nigtwish ghost love score"}` ; `vkbstatus` 0 à la fermeture |
| **YouTube** | **rien** | YouTube a son propre clavier, hors du mécanisme VIDAA |

Autres signes : `ui_service/state` passe à `{"statetype":"input"}` quand un
champ VIDAA est actif. Côté VIDAA, l'annonce n'arrive qu'à la **première
lettre** (pas à l'ouverture du clavier) ; côté navigateur, dès l'ouverture.

→ Utilisable dès maintenant pour **lire** le texte saisi sur la TV et savoir
qu'un clavier est ouvert.

## 2. La TV filtre abonnements ET envois (liste blanche)

- Abonnements : jokers `#`, `/remoteapp/#` refusés. Sur 26 sujets essayés,
  6 acceptés : `ui_service/state`, `platform_service/actions/volumechange`,
  `platform_service/actions/tvsleep`, `ui_service/data/hotelmodechange`,
  `ui_service/data/txtinputdata`, `platform_service/actions/bwsinputdata`
  (tous sous `/remoteapp/mobile/broadcast/`). Les réponses adressées à notre
  identifiant client (`/remoteapp/mobile/<client>/…`) en dehors des sujets
  connus sont refusées.
- Envois : un message vers un sujet non autorisé est **ignoré sans erreur**
  (QoS 0). On ne peut donc détecter le bon sujet que par son effet — ici
  l'écho du champ, qui permet un test automatique.

## 3. Variantes d'envoi essayées — toutes sans effet

Test automatique (déclenché à l'ouverture du clavier, chaque variante avec
un repère unique, réussite = repère renvoyé dans l'écho) :

- Vague 1 (10 variantes) : `ui_service` / `platform_service`,
  `actions|data/txtinputdata|bwsinputdata`, formats
  `{"vkb_status":1,"input_data":…}`, `{"vkbstatus":1,"inputdata":…}`,
  `{"text":…,"action":"replace"}` (format de stevene1919/hisense_vidaa).
- Vague 2 (165 variantes) : services `ui_service`, `platform_service`,
  `remote_service`, `hiservice_cmd`, `ps_service` × actions
  `actions/txtinputdata`, `data/txtinputdata`, `actions/bwsinputdata`,
  `data/bwsinputdata`, `actions/inputdata`, `actions/textinput`,
  `actions/inputtext`, `actions/sendtext`, `actions/vkbinputdata`,
  `actions/keyboard`, `actions/input` × les 3 formats.

**Aucune acceptée.** L'APK VIDAA ne peut pas départager : son code est
chiffré (protection Baidu) et la partie Flutter est absente — on ne
contourne pas cette protection.

## 4. Pistes restantes

1. **Vérifier si l'app VIDAA officielle du téléphone sait taper sur cette
   TV.** Si non, la TV ne prend sans doute pas de texte à distance par ce
   canal, et chercher davantage est inutile.
2. **Repli : taper via le clavier affiché par la TV** (flèches + OK). Pour
   les zones VIDAA, l'écho permet de vérifier chaque lettre (boucle fermée) ;
   pour YouTube (pas d'écho), il faut connaître la disposition de son
   clavier.
3. **Lecture seule dès maintenant** : afficher dans l'app le texte en cours
   sur la TV et activer la saisie quand un clavier s'ouvre.
