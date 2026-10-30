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
