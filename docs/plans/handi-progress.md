# Handi : suivi d'avancement

Branche : `feat/handi-redesign`. Modèle : https://codemode-store.vercel.app/items/handi/demo.html

Décisions validées par l'utilisateur :
- Réservation : nom, couverts, heure, table, note, **téléphone facultatif** (donnée personnelle : à protéger et purger).
- Écran Caisse : nouvelle palette et polices, **même disposition** (grille tactile inchangée).
- Coût de revient : champ **facultatif** dans la fiche produit ; sans coût, le plat est classé selon sa seule popularité et signalé « coût manquant ».
- Ordre : 1 habillage web, 2 analyse du menu, 3 réservations, 4 iPad (habillage + 2 pages, serveur et mode autonome).

Choix de l'assistant : polices Bricolage Grotesque et DM Mono auto-hébergées (`wwwroot/fonts`, réseau local sans CDN) ; thème clair papier/encre/tomate pour tout le web ; la cloche « need you » est reportée à l'étape 3 (elle a besoin des réservations en retard).

| Étape | Statut | .NET | PosKit | Web (Playwright) | Revue ouverte |
|---|---|---|---|---|---|
| 1 Habillage web | terminé | n/a | n/a | 174/176 (2 projets × 88) ; seul échec : `happy-hour-multiselect` US4, appel API sans UI, déjà présent avant l'étape | aucune |
| 2 Analyse du menu | à faire | | | | |
| 3 Réservations | à faire | | | | |
| 4 iPad | à faire | | | | |

Note : `checkout-payment.spec.ts` lance `sqlite3` sur un chemin OneDrive en dur (`DB_PATH`), sans délai maximal : le test peut se bloquer indéfiniment. Run de l'étape 1 faite avec un faux `sqlite3` dans le `PATH` (aucune modification du dépôt). À corriger séparément.
