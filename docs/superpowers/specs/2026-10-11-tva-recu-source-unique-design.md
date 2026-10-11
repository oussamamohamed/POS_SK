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
2. **Remise de commande** : la remise (pourcentage ou montant fixe, bornée à zéro) est répartie entre les groupes au prorata du TTC de chaque groupe, le **dernier groupe prenant le reste**, pour que la somme des parts soit exactement `Order.TotalTtc`. C'est la règle déjà appliquée par `OrderMath.totals` côté iPad.
3. **Découpe** : pour chaque groupe, base HT et TVA par `TaxBreakdownItem.Calculate(taux, part)` (TVA = part − base).
4. **Totaux** : `TotalHt` = somme des bases ; `Σ TVA = TotalTtc − TotalHt`.
5. **Reçu** : `TaxBreakdownJson` = TVA par taux effectif (clés et ordre d'apparition inchangés), `TotalHtAmount` = somme des bases, `TotalTtcAmount` = montant fiscal encaissé.

## Règlements partiels et pourboire

- La détection du pourboire et du règlement partiel (`fiscalPaid`, `isPartial`, `priorFiscalTtc`) ne change pas.
- Remplacement de l'échelle par ratio (arrondi au pair) par une **répartition cumulée** : la part du reçu pour un taux est `part(P + X) − part(P)`, où `P` = TTC fiscal des reçus précédents, `X` = TTC fiscal de ce reçu, et `part(t)` répartit `t` sur les taux au prorata du TTC des groupes. Conséquence recherchée : la somme des reçus d'une commande est **exactement** la ventilation de la commande, par taux, et pour chaque reçu `TTC − HT = Σ TVA`.
- Le plan vérifie par tests aléatoires que la répartition cumulée ne produit ni montant négatif sur un reçu de vente, ni écart de somme. Si la méthode choisie échoue, le repli est une répartition proportionnelle par reçu avec un écart cumulé borné à 1 centime par reçu et par taux, et la spec sera amendée avant d'avancer.
- Annulation : inchangée (valeurs négatées).

## Périmètre

Inclus :
1. .NET : `Order.CalculateTaxBreakdown`, `CheckoutPaymentService` (reçu, partiel), cohérence de `TotalHt`.
2. Swift : `FiscalReceiptMath` (même règle, s'appuie sur `OrderMath.totals`) et parité de `OrderMath.totals` avec la nouvelle ventilation .NET.
3. Fixture `fiscal_reference.json` régénérée, enrichie (voir « Tests »).
4. Retrait des deux écarts documentés dans `CLAUDE.md`, mise à jour de `docs/fiscal.md` si la formule y est décrite, mise à jour des tests existants qui figeaient les anciens montants (`ReportAccuracyTests`, `FecExportServiceTests`, `FiscalArchive…`, `PeriodClosure…`, etc.).

Hors périmètre (à traiter séparément) :
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
3. Somme des reçus (hors annulés) d'une commande réglée = ventilation de la commande, par taux, **exactement**.
4. Aucun montant négatif pour un reçu de vente ; annulation = exacte négation de l'original.
5. Taux : un reçu à emporter à 5,5 % a une clé `5.5` dans son JSON (jamais le taux de base).
6. Parité : Swift = .NET sur chaque grandeur.

**Matrice de cas** (combinaisons, y compris compliquées) :
- Destination : sur place, à emporter, avec et sans taux à emporter défini par ligne.
- Taux : un, deux, trois, quatre taux mélangés (0 %, 2,1 %, 5,5 %, 10 %, 20 %), mêmes taux en groupes de lignes différentes, ligne à taux de base et ligne à taux à emporter dans la même commande.
- Lignes : quantité > 1, suppléments, remise ligne (10 %, 33,33 %, 100 %), gratuité, ligne à 0, ligne de 1 centime, lignes à prix impairs qui créent des arrondis au demi-centime.
- Remise de commande : aucune ; pourcentage (10 %, 33,33 %, 100 %) ; montant fixe inférieur au total, égal au total, supérieur au total (borné à zéro) ; remise de commande combinée à des remises de ligne et des gratuités ; remise sur commande à plusieurs taux (répartition et reste sur le dernier groupe).
- Pourboire : aucun, présent avec règlement unique, présent avec règlement partiel puis solde, pourboire seul sur le dernier règlement.
- Règlements : un seul ; deux, trois et cinq règlements égaux avec reste de division ; règlements inégaux ; règlements mixtes (espèces avec rendu monnaie, carte, titre-restaurant, carte cadeau) ; règlement partiel laissant un solde (commande non soldée) ; règlement à zéro (commande offerte).
- Annulations : annulation d'un reçu simple, d'un des reçus d'un règlement fractionné, d'un reçu à emporter à taux réduit, d'un reçu avec remise.
- Cas limites : commande à total nul, un seul centime, total très élevé, parts qui arrondissent différemment selon l'ordre des groupes.
- Un jeu **aléatoire déterministe** (graine fixe, plusieurs centaines de commandes générées par le test) rejoue les invariants 1 à 6, pour couvrir ce que la matrice n'énumère pas ; en cas d'échec, la graine et la commande fautive sont affichées.

**Tests unitaires .NET supplémentaires** : `Order.CalculateTaxBreakdown` seul (toutes les remises), reçu du `CheckoutPaymentService` (invariants ci-dessus), Z et FEC sur une commande à emporter (la TVA est bien déclarée au taux réduit, HT par taux exact).

## Risques

- **Montants des reçus à emporter et des reçus avec remise** : ils changent (c'est le but). Les tests existants qui figeaient les anciens montants doivent être révisés un par un et non régénérés à l'aveugle : chaque changement de valeur est justifié par l'invariant.
- **Répartition cumulée** : peut produire des cas d'arrondi inattendus ; couverte par la matrice et le jeu aléatoire, avec le repli décrit plus haut.
- **Attestation NF525** : une modification de la règle de calcul des reçus relève de la documentation de conformité ; à signaler avant toute mise en production.
- **Divergence iPad serveur/autonome** : le serveur .NET reste la référence ; toute évolution ultérieure impose de régénérer la fixture (règle déjà inscrite dans `CLAUDE.md`).
