# TVA du reçu : une seule source de calcul (taux effectif et remise)

Date : 2026-10-11 · Statut : en attente de relecture
Contexte : suite du sous-projet 2 du mode autonome (`2026-10-10-ipad-standalone-2-fiscal-design.md`), qui a reproduit volontairement des écarts du .NET pour la parité.

## Problème

`CheckoutPaymentService` calcule le reçu avec deux logiques différentes :

- **JSON de TVA** (`TaxBreakdownJson`) : somme de `OrderItem.TaxAmount` par `TaxRatePercent` de la ligne. Ne tient compte ni du **taux à emporter** (`GetEffectiveTaxRate`) ni de la **remise globale**.
- **HT** (`TotalHtAmount`) : `Order.CalculateTaxBreakdown()`, au taux effectif, avec la remise globale **en pourcentage seulement** (la remise en montant fixe est ignorée alors que le TTC la prend en compte).

Constat d'origine (historique git) : le calcul du reçu date des fondations (`72bfb26`) ; `GetEffectiveTaxRate` et `CalculateTaxBreakdown` sont arrivés avec la vente à emporter (`a7cd0f3`) sans que le reçu soit mis à jour. `TakeawayTaxCalculationTests` ne teste que `CalculateTaxBreakdown`, jamais le JSON du reçu.

Conséquences : pour une vente à emporter à 5,5 %, le HT est calculé à 5,5 % et la TVA à 10 %, donc `TTC − HT ≠ somme de la TVA du reçu`. Le rapport Z agrège ce JSON par taux (`NF525FiscalAuditService`) et le FEC en déduit le HT par taux (`FecExportService`) : la TVA est déclarée au mauvais taux.

## Objectif

Une seule source de vérité pour la ventilation fiscale d'une commande, utilisée par le total, le HT, le JSON de TVA et les reçus partiels, avec la garantie que pour chaque reçu `TTC − HT = somme de la TVA` et que les reçus d'une commande se somment exactement au total de la commande.

## Décisions (utilisateur)

- Aucun reçu réel n'a été émis (test/dev seulement) : **pas de mécanisme de transition** ni de version de règle. Les anciens reçus restent immuables et inchangés.
- Approche A : `Order.CalculateTaxBreakdown()` devient la source de vérité, au .NET puis portée à l'identique en Swift.
- **Les tests couvrent tous les cas, y compris les plus tordus**, et vérifient sous-total, montant de remise, total TTC, HT, taux de TVA, montant de TVA et totaux de chaque reçu (voir « Tests »).

## Règle de calcul (.NET)

1. **Groupes** : lignes regroupées par taux effectif (`GetEffectiveTaxRate` : taux à emporter si destination à emporter et taux défini, sinon taux de la ligne). Le TTC d'une ligne reste `OrderItem.CalculateTotalTtc()` (gratuité, remise ligne, suppléments).
2. **Remise de commande** : la remise (pourcentage ou montant fixe, bornée à zéro) est répartie entre les groupes au prorata du TTC de chaque groupe, selon la **méthode du plus fort reste** (somme exacte, aucune part négative, restes égaux départagés par l'ordre des groupes), qui remplace aussi la règle « dernier groupe prend le reste » de `OrderMath.totals` côté iPad (elle pouvait produire une part négative). Les groupes sont ordonnés par **taux effectif croissant**.
3. **Découpe** : pour chaque groupe, base HT et TVA par `TaxBreakdownItem.Calculate(taux, part)` (TVA = part − base).
4. **Totaux** : `TotalHt` = somme des bases ; `Σ TVA = TotalTtc − TotalHt`.
5. **Reçu** : `TaxBreakdownJson` = TVA par taux effectif (clés = taux effectifs, en ordre croissant des taux), `TotalHtAmount` = somme des bases, `TotalTtcAmount` = montant fiscal encaissé.

## Règlements partiels et pourboire

- La détection du pourboire et du règlement partiel (`fiscalPaid`, `isPartial`, `priorFiscalTtc`) ne change pas.
- La part d'un reçu de montant `X` est `Allocate(X, parts de la commande par taux)` (plus fort reste), sans rejouer l'historique des reçus. Pour chaque reçu, `TTC − HT = Σ TVA` exactement ; `Σ TTC des reçus = TTC de la commande` exactement ; par taux, la somme des reçus s'écarte du total de la commande d'au plus 1 centime par reçu (arrondi de la TVA ticket par ticket).
- Annulation : inchangée (valeurs négatées).

## Périmètre

Inclus :
1. .NET : `Order.CalculateTaxBreakdown`, `CheckoutPaymentService` (reçu, partiel), cohérence de `TotalHt`.
2. Swift : `FiscalReceiptMath` (même règle, s'appuie sur `OrderMath.totals`) et parité de `OrderMath.totals` avec la nouvelle ventilation .NET.
3. Fixture `fiscal_reference.json` régénérée, enrichie (voir « Tests »).
4. Retrait des deux écarts documentés dans `CLAUDE.md`, mise à jour de `docs/fiscal.md` si la formule y est décrite, mise à jour des tests existants qui figeaient les anciens montants (`ReportAccuracyTests`, `FecExportServiceTests`, `FiscalArchive…`, `PeriodClosure…`, etc.).

5. Test bout en bout d'une **journée simulée** avec vérification de X et Z (voir « Journée simulée »).

Hors périmètre (à traiter séparément) :
- Rejeu Swift de la journée simulée (attend les rapports X/Z de l'iPad, sous-projet 3).
- Remise globale de type `Comp` : le .NET l'ignore dans `CalculateTotalTtc` alors que l'iPad la met à zéro. À constater et rapporter, sans le changer ici.
- Tout changement de format de signature ou de schéma (aucun).
- Client web (`app.js`) : seulement s'il recalcule la ventilation (à vérifier dans le plan).

## Tests

Principe : un **générateur .NET** produit la fixture à partir du code réel (`Order`, `CheckoutPaymentService`), le Swift la rejoue. Chaque cas vérifie les grandeurs suivantes, à la fois **côté commande** et **côté reçu**.

**Grandeurs vérifiées pour la commande** (.NET et `OrderMath.totals` Swift) : sous-total TTC, montant de la remise (`sous-total − total`), total TTC, HT total, TVA totale, et pour chaque taux effectif : clé du taux, part TTC, base HT, montant de TVA (et `base + TVA = part`).

**Grandeurs vérifiées pour chaque reçu** : TTC, HT, JSON de TVA (clés, montants, ordre), rendu monnaie, solde restant ; et pour les annulations les valeurs négatées.

**Invariants** (assertions explicites, sur tous les cas) :
1. Reçu : `TTC − HT = Σ TVA`.
2. Commande : `Σ parts = TotalTtc`, `Σ bases = TotalHt`, `Σ TVA = TotalTtc − TotalHt`.
3. Somme des reçus (hors annulés) d'une commande réglée : `Σ TTC` exact, et écart par taux ≤ 1 centime par reçu.
4. Aucun montant négatif pour un reçu de vente ; annulation = exacte négation de l'original.
5. Taux : un reçu à emporter à 5,5 % a une clé `5.5` dans son JSON (jamais le taux de base).
6. Parité : Swift = .NET sur chaque grandeur.

**Matrice de cas** (combinaisons, y compris compliquées) :
- Destination : sur place, à emporter, avec et sans taux à emporter défini par ligne.
- Taux : un, deux, trois, quatre taux mélangés (0 %, 2,1 %, 5,5 %, 10 %, 20 %), mêmes taux en groupes de lignes différentes, ligne à taux de base et ligne à taux à emporter dans la même commande.
- Lignes : quantité > 1, suppléments, remise ligne (10 %, 33,33 %, 100 %), gratuité, ligne à 0, ligne de 1 centime, lignes à prix impairs qui créent des arrondis au demi-centime.
- Remise de commande : aucune ; pourcentage (10 %, 33,33 %, 100 %) ; montant fixe inférieur au total, égal au total, supérieur au total (borné à zéro) ; remise de commande combinée à des remises de ligne et des gratuités ; remise sur commande à plusieurs taux (répartition au plus fort reste).
- Pourboire : aucun, présent avec règlement unique, présent avec règlement partiel puis solde, pourboire seul sur le dernier règlement.
- Règlements : un seul ; deux, trois et cinq règlements égaux avec reste de division ; règlements inégaux ; règlements mixtes (espèces avec rendu monnaie, carte, titre-restaurant, carte cadeau) ; règlement partiel laissant un solde (commande non soldée) ; règlement à zéro (commande offerte).
- Annulations : annulation d'un reçu simple, d'un des reçus d'un règlement fractionné, d'un reçu à emporter à taux réduit, d'un reçu avec remise.
- Cas limites : commande à total nul, un seul centime, total très élevé, parts qui arrondissent différemment selon l'ordre des groupes.
- Un jeu **aléatoire déterministe** (graine fixe, plusieurs centaines de commandes générées par le test) rejoue les invariants 1 à 6, pour couvrir ce que la matrice n'énumère pas ; en cas d'échec, la graine et la commande fautive sont affichées.

**Tests unitaires .NET supplémentaires** : `Order.CalculateTaxBreakdown` seul (toutes les remises), reçu du `CheckoutPaymentService` (invariants ci-dessus), Z et FEC sur une commande à emporter (la TVA est bien déclarée au taux réduit, HT par taux exact : le reçu persiste le HT par taux dans la colonne non signée `HtBreakdownJson`, que le FEC reprend telle quelle pour ventiler les comptes 706 ; les reçus historiques, `NULL`, gardent le repli par déduction depuis la TVA, approximatif de quelques centimes entre taux bas).

## Journée simulée (test bout en bout, X et Z)

Un test d'intégration rejoue une **journée complète** sur l'API réelle (`PosApiApplicationFactory`, base en mémoire) et vérifie que les rapports **X** (aperçu) et **Z** (clôture scellée) correspondent à la réalité de la journée et sont cohérents entre eux.

**Acteurs et volume**
- Plusieurs serveurs (comptes de service distincts, plus un responsable et un administrateur) répartis sur **deux ou trois terminaux appairés** (`T01`, `T02`, `T03`) : X et Z sont calculés par terminal (le rapport ne ventile pas par serveur), on vérifie donc chaque terminal **et** le cumul de la salle.
- Quelques dizaines de commandes, générées de façon déterministe (graine fixe, journal des opérations affiché en cas d'échec).

**Opérations rejouées** (toutes par les endpoints publics, jamais en écrivant directement dans la base) :
- Commandes **sur place** (tables, couverts, ajout de lignes, envoi cuisine, règlements fractionnés) et **à emporter / comptoir** (numéro de retrait, vente directe), avec taux de TVA mélangés et taux réduits à emporter.
- Remises de ligne et de commande (pourcentage et montant fixe), gratuités, suppléments, pourboires.
- Moyens de paiement mixtes : espèces avec rendu monnaie, carte, titre-restaurant, carte cadeau, facturation chambre.
- Mises en attente puis rappel, mise en attente annulée avec PIN responsable.
- Annulations de reçus (reçu simple, reçu d'un règlement fractionné, reçu à emporter), duplicata de reçu.
- Un rapport **X** pris en cours de journée (plusieurs fois, sur chaque terminal) pour vérifier qu'il ne modifie rien.

**Oracle indépendant** : le test tient son propre grand livre, calculé **à partir des entrées** (lignes, taux, remises, règlements) et non à partir des reçus : TTC, HT, TVA par taux, total par moyen de paiement (montants encaissés nets du rendu monnaie), nombre de reçus et d'annulations, par terminal. Il sert de « réalité » à laquelle X et Z sont comparés.

**Vérifications**
1. **X en cours de journée** : égal au grand livre à cet instant, par terminal ; deux X consécutifs sans opération sont identiques ; X ne crée ni séquence de clôture ni écriture fiscale.
2. **Z refusé tant qu'une commande reste ouverte** (`409 has_open_orders`), puis accepté une fois toutes les commandes soldées ou annulées.
3. **Z = grand livre** par terminal : TTC, HT, nombre de reçus (annulations comprises), TVA par taux (clé et montant), total par moyen de paiement ; `TTC − HT = Σ TVA` ; Σ des moyens de paiement = TTC ; les annulations réduisent exactement les totaux.
4. **Cohérence X ↔ Z** : le X pris juste avant le Z a les mêmes chiffres que le Z ; après le Z, un X repart de zéro (nouvelle période) et ne reprend pas les ventes déjà scellées.
5. **Cumuls** : Σ des Z des terminaux = grand livre de la salle ; le **grand total perpétuel** du Z du jour augmente exactement du TTC de la journée ; une **deuxième journée** (quelques opérations puis un second Z) ne double pas la première et chaîne correctement (`ClosureSequence`, hash précédent).
6. **Intégrité** : vérification des chaînes (`verify-chains`) vide après la journée ; le journal contient une entrée `Z_CLOSURE` par clôture, `RECEIPT_VOIDED` par annulation, `DUPLICATE_PRINTED` par duplicata, et l'audit de mise en attente annulée ; le FEC de la journée est équilibré au centime et sa TVA par taux est celle du grand livre.
7. **Reproductibilité** : la graine est fixe ; un second passage donne les mêmes totaux (hors identifiants et horodatages).

**Réutilisation** : le scénario de la journée est décrit comme des **données** (suite d'opérations + totaux attendus) et non comme du code, pour que le mode autonome de l'iPad puisse le rejouer quand ses rapports X et Z existeront (sous-projet 3). Ce rejeu Swift est hors périmètre de ce correctif mais **obligatoire** : il est inscrit comme condition d'entrée et de sortie du sous-projet 3 dans `docs/plans/ipad-standalone-2-progress.md` (condition 8). Le format des données du scénario (suite d'opérations, grand livre attendu par terminal) doit donc être conçu dès maintenant pour être lisible par le Swift, par exemple en JSON dans `Tests/PosKitTests/Fixtures`, et non enfermé dans du code C#.

## Risques

- **Montants des reçus à emporter et des reçus avec remise** : ils changent (c'est le but). Les tests existants qui figeaient les anciens montants doivent être révisés un par un et non régénérés à l'aveugle : chaque changement de valeur est justifié par l'invariant.
- **Attestation NF525** : une modification de la règle de calcul des reçus relève de la documentation de conformité ; à signaler avant toute mise en production.
- **Divergence iPad serveur/autonome** : le serveur .NET reste la référence ; toute évolution ultérieure impose de régénérer la fixture (règle déjà inscrite dans `CLAUDE.md`).
