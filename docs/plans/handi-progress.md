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
| 2 Analyse du menu | terminé | 811/812 (69 + 296 + 446/447) ; seul échec : `ReportAccuracyTests.FinancialDashboard_DiscountedOrders…`, déjà présent avant l'étape (service du tableau de bord non touché) | n/a | 178/180 (2 projets × 90) ; seul échec : `happy-hour-multiselect` US4, déjà présent | aucune |
| 3 Réservations | terminé | 829/830 (69 + 301 + 459/460) ; seul échec : `ReportAccuracyTests.FinancialDashboard_DiscountedOrders…`, déjà présent | n/a | 180/182 (2 projets × 91) ; seul échec : `happy-hour-multiselect` US4, déjà présent | aucune |
| 4 iPad | terminé | n/a | 720 unitaires PosKit (+ PosPrinters) ; 67 tests UI, 0 échec | n/a | aucune |

Note : `checkout-payment.spec.ts` lance `sqlite3` sur un chemin OneDrive en dur (`DB_PATH`), sans délai maximal : le test peut se bloquer indéfiniment. Run de l'étape 1 faite avec un faux `sqlite3` dans le `PATH` (aucune modification du dépôt). À corriger séparément.

Étape 2 : `Product.Cost` (Money?, colonne `Cost INTEGER NULL` ajoutée dans `Program.cs`), `GET /api/menu-analysis` (responsable/admin), service `MenuAnalysisService` (classes Kasavana-Smith, énumération `MenuClass` sérialisée en nombre : Star 0, Plowhorse 1, Puzzle 2, Dog 3), coût servi seulement aux responsables sur `GET /api/catalog/products`, `PUT` : coût absent = inchangé, `clearCost: true` = effacé. Reste pour l'étape 4 : `cost` / `clearCost` dans les modèles et l'écran Admin iPad, mode autonome (`LocalPosAPI`, nouvelle migration).

Étape 3 : voir `docs/reservations.md`. Web : page Réservations, repère « Réservée hh:mm » sur le plan de salle, pastille de navigation (retards + liste d'attente). Pas faite : la cloche « need you » de l'en-tête. Reste pour l'étape 4 : écran Réservations iPad (serveur et mode autonome : table `Reservations`, migration V11, purge du téléphone), abonnement à `OnReservationsChanged`.

Étape 4 : habillage (Theme, polices, apparence claire par défaut) ; PosKit : `Reservation*`, `MenuAnalysis*`, `ReservationRules`, `MenuAnalysisCalculator`, `Product.cost`, `ProductDraft.cost/hadCost`, `PosAPI` (6 méthodes), HTTP / mémoire / autonome (migration V11, `LocalReservationRepository`, `LocalPosAPI+Reservations`), `BookingsStore`, `MenuAnalysisStore`, `RealtimeEvent.reservationsChanged` ; écrans `BookingsScreen`, `BookingSheet`, `MenuAnalysisScreen`, repère « Réservée » du plan de salle, champ coût de l'éditeur d'article. Non fait : cloche « need you » de l'en-tête (web et iPad), aperçu dans l'éditeur du coût par défaut du serveur pour un non-responsable, synchronisation des réservations entre l'iPad autonome et un serveur (inexistante par conception).

Points à faire valider : durées de conservation du téléphone (2 jours) et des lignes (90 jours) ; classement `Cost` mesuré sur le HT avec le coût actuel de l'article (pas d'historique des coûts) ; la remise de commande est répartie au prorata sur les lignes pour le CA HT.

Correctifs de tests : `ReportAccuracyTests.FinancialDashboard_DiscountedOrders…` (bornes en UTC : `now.Date` était relu en heure locale) et `happy-hour-multiselect` US4 (création et suppression de créneaux avec le jeton du PIN 1234 : `RequireManagerOrAdmin`). Les deux échecs « déjà présents » des étapes 1 à 3 sont donc corrigés. Le `DB_PATH` OneDrive de `checkout-payment.spec.ts` est remplacé par le chemin du dépôt (`src/RestaurantPos.Api/restaurantpos.db`) avec un délai de 5 s sur `sqlite3`, qui ne peut plus bloquer la suite.

## Suite : écarts de la comparaison avec la cible

Fait après la comparaison : (1) cloche « à traiter » et pastilles Salle, Cuisine, Réservations (web et iPad) ; (2) réservation terminée au paiement ; (3) analyse du menu : filtre de famille recalculé côté serveur (`categoryId`), tri, survol, fiche du plat, graduations et quadrants ; (4) plan de salle dessiné avec états de service, tuiles et volet de détail, cuisine avec tuiles, barre vers le temps cible, étiquette de retard, compteurs de poste, « à cuire » et « récemment sortis » (web et iPad) ; (5) page Encaissements : tuiles, courbe horaire contre la semaine passée, paiements, serveurs, lien vers la fiscalité (web, iPad, mode autonome). Limites assumées : temps cible de cuisine en constante (12 min), plan à disposition automatique (pas d'éditeur de plan), statistiques de cuisine sur les 50 derniers bons, pas de navigation par hash ni d'animations de compteurs.

Décomptes après cette suite : .NET 836 (69 + 464 + 303) sans échec ; Playwright 194 sur 194 ; PosKit 733 + PosPrinters 41 ; tests UI iPad 72 sans échec en suite complète, plus 3 nouveaux (plan, cuisine, encaissements) passés séparément. Revue ouverte : aucune.

## Enrichissement de la page Encaissements

Fait : périodes (aujourd'hui, hier, 7 jours, 30 jours, date) et comparaison (automatique, semaine passée, période précédente) ; cumul ; pourboires, remises, offerts et annulations ; familles, 5 meilleurs plats et TVA par taux ; tickets de la période avec duplicata ; journal du jour en cours seulement. Web, iPad (serveur) et iPad autonome (`LocalPosAPI+Takings`). Contrat décrit dans `CLAUDE.md`. Une semaine locale qui traverse un changement d'heure mesure 7 jours ± 1 h : une heure de tolérance dans le calcul du nombre de jours (.NET et Swift, test ajouté).

Décomptes : .NET 842 (69 + 468 + 305) sans échec ; PosKit 737 + PosPrinters 41 sans échec ; tests UI iPad 75 (74 à la suite complète, le 75e corrigé puis `BookingsUITests` 11 sur 11) ; Playwright 97 sur 98 sur le projet iPad Pro 11, seul échec `hotel-room-charge` : la chambre 101 de la base de test a épuisé son crédit (299 € sur 300 €) après des exécutions répétées, sans lien avec cette étape.

Fixtures capturées : `takings.json`, `takings_receipts.json`, `takings_journal.json` (les ventes du jour viennent des specs Playwright). Revue ouverte : aucune. À valider : le journal .NET n'enregistre pas les ventes, seulement les événements fiscaux et de session ; la répartition par jour se fait en heures UTC écoulées depuis le début de la période (un reçu proche de minuit un jour de changement d'heure peut tomber dans le jour voisin).
