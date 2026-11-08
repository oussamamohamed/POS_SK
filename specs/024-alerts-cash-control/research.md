# Recherche : Alertes de pilotage et contrôle de caisse

## D1. Définition du « service » pour les seuils
- **Décision** : un service est la période entre deux clôtures Z du poste concerné (la journée fiscale). La clé de période d'une alerte est `terminalId + numéro du dernier Z`.
- **Raison** : le serveur n'a pas de fuseau horaire de gestion fiable (les encaissements utilisent le décalage envoyé par le client) ; le Z existe sur les trois supports et borne naturellement la journée. Sans Z depuis longtemps, la période reste ouverte et un seuil quotidien devient cumulatif : à signaler dans l'écran.
- **Écart avec la spec** : « par service » sans distinguer midi et soir. Une découpe midi/soir est hors lot ; elle demanderait des horaires de service dans les réglages.
- **Alternatives** : jour civil local (rejetée : fuseau) ; plages horaires configurables (reportée).

## D2. Calcul de l'espèce attendue
- **Décision** : attendu = fond d'ouverture + Σ(règlements `Cash` du poste, de `OpenedAtUtc` à l'instant de clôture, montant moins rendu) + Σ(entrées) − Σ(sorties). Les avoirs ont des règlements négatifs et diminuent donc l'attendu en espèces. L'écart = compté − attendu.
- **Raison** : `PaymentTender` porte déjà `Method`, `Amount` et `ChangeGiven`. Aucun nouveau capteur de vente. Les reçus à plusieurs règlements ne comptent que leur part espèces.
- **Attention** : `Amount` est la part imputée au reçu ; à vérifier en tâche de test que `Amount` n'inclut pas le rendu (si `Tendered` est la remise du client, alors `Amount` est le net).
- **Alternatives** : lire les montants des rapports X (rejetée : dépend de la période Z et pas du poste de caisse).

## D3. Lien session de caisse, poste et Z
- **Décision** : une session par poste, ouverte par l'opérateur appairé, clôturée par un comptage. Le rapport Z existant ne change pas ; le DTO du rapport Z (non signé) gagne les champs facultatifs `cashFloat`, `cashCounted`, `cashVariance` lus de la dernière session clôturée de la période, sans entrer dans aucune signature.
- **Raison** : respecte la contrainte « ne pas modifier les chaînes ». L'impression du Z ne change pas dans ce lot (sinon : régénération de `ticket_document_reference.json`).
- **Alternatives** : stocker le comptage dans le Z (rejetée : toucherait un objet signé).

## D4. Immutabilité
- **Décision** : `CashSessions` (ouverture), `CashMovements` et `CashCounts` (clôture, un enregistrement par session) en ajout seulement par déclencheurs SQL, serveur et iPad. `Alerts` est mutable pour son état et sa note de traitement, avec `ResolvedAtUtc` et l'opérateur conservés.
- **Raison** : une session n'est donc jamais « mise à jour » ; la clôture est une insertion. Les alertes sont un outil de travail, pas une pièce comptable.

## D5. Évaluation des alertes
- **Décision** : service synchrone `AlertService.EvaluateAsync(terminalId, operatorId, kind)` appelé après le `COMMIT` des annulations, remises, offerts et clôtures de caisse. L'alerte porte une clé d'unicité `ruleType|operatorId|periodKey` (index unique) : insérer deux fois est sans effet. Un échec d'évaluation est journalisé et ne remonte jamais à la vente.
- **Raison** : même discipline que `PrintDispatcher` ; l'unicité en base rend l'idempotence indépendante des appels simultanés.
- **Alternatives** : tâche planifiée périodique (rejetée : alerte tardive) ; calcul à la lecture seulement (rejetée : pas d'historique ni d'état « traitée »).

## D6. Types de seuil du premier lot
- **Décision** : `cash_variance` (centimes absolus), `voids_count` (nombre d'avoirs par opérateur et par période), `discount_percent` (remise unique supérieure à X % du sous-total de la commande), `comps_total` (somme des offerts par opérateur et par période). Les seuils sont désactivés par défaut avec des valeurs proposées (10 €, 3, 30 %, 50 €).
- **Raison** : couvre les cas de la spec sans nouveau modèle de données.

## D7. Tendances sans persistance
- **Décision** : `InsightsService` s'appuie sur `FinancialDashboardService.GetTakingsAsync` avec comparaison à `lastWeek` et la référence cumulée à la même heure. Cartes retournées : chiffre d'affaires cumulé, ticket moyen, panier par couvert, part à emporter. Seuil d'écart par défaut : 15 % (réglable, `trendThresholdPercent`). Aucune carte si moins de 3 jours distincts ont des reçus sur les 28 derniers jours.
- **Raison** : aucun stockage ; réutilise un calcul déjà éprouvé, avec sa tolérance d'une heure sur les changements d'heure.
- **Alternatives** : persistance dans `Alerts` (rejetée : se périme en quelques heures).

## D8. Résumé quotidien
- **Décision** : `GET /api/dashboard/daily-summary?date=` assemble les totaux du dernier Z du poste (donc identiques), les pourboires par serveur, la session de caisse et les alertes ouvertes de la période. Premier lot : affichage, impression par le navigateur et partage de texte sur l'iPad. L'envoi par courriel est reporté.
- **Raison** : ne dépend d'aucun fournisseur d'envoi.

## D9. Autorisations
- **Décision** : ouvrir, sortir de l'argent et compter exigent un appareil appairé et un opérateur authentifié, mais seul le responsable lit les historiques, les seuils, les alertes et les tendances. Un serveur lit seulement sa session en cours. `RequireManagerOrAdmin` pour toutes les autres routes.
- **Raison** : FR-013.

## D10. Parité .NET / Swift
- **Décision** : `cash_reference.json` (scénarios d'espèces avec rendu, avoirs, mouvements et seuils) généré par un test .NET avec `WRITE_CASH_FIXTURE=1` et rejoué côté Swift. La journée simulée gagne une ouverture de caisse, une sortie, un comptage avec écart imposé et des annulations dépassant un seuil ; l'oracle donne l'espèce attendue et la liste d'alertes (SC-002, SC-003).

## D11. Temps réel et mode autonome
- **Décision** : `OnAlertsChanged` sur `/hubs/pos`, abonné côté web et iPad pour rafraîchir la cloche. En mode autonome, l'évaluation se fait localement et la cloche se rafraîchit au retour de chaque opération.
