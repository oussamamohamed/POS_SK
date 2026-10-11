# TVA du reçu à source unique — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-11-tva-recu-source-unique.md`

Base de référence (avant tâche 1) : PosKit 394 · UI 52 · .NET 418

| Tâche | Statut | .NET | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 Répartiteur `TaxAllocation`, ventilation `Order.CalculateTaxBreakdown`, amendement de la spec | fait | 443 | 394 (1 échec attendu : parité `fiscal_reference.json`, HT remise fixe 2576 -> 2318, à résoudre côté Swift) | - | aucun |
| 2 Reçu tiré de la ventilation (`ReceiptTaxCalculator`), scénarios partagés `FiscalScenarios`, invariants | fait | 591 | non relancé (FiscalReceiptMathTests rouges attendus jusqu à la tâche 4) | - | aucun |
| 3 Fixture enrichie (bloc `order` + groupes, `tenders` réduits au montant réglé) | fait | 591 | non relancé (rouge attendu jusqu à la tâche 4) | - | aucun |
| 4 Swift : `OrderMath.allocate`, `totals` et `FiscalReceiptMath` alignés | fait | non touché | 400 (toutes vertes) | - | aucun |
| 5 Journée simulée : modèle, grand livre indépendant, harnais d API, journées minimale et riche X/Z | fait | 601 | non touché | - | aucun |
| 6 Journée simulée complète, X et Z comparés au grand livre, deux journées, intégrité des chaînes, FEC | fait | 617 | non touché | - | aucun (défaut FEC traité en 6bis) |
| 6bis FEC équilibré par écriture, HT des annulations et du taux 0 % conservés, pourboire en 468600 | fait | 624 | non touché | - | aucun |
| 6ter HT par taux persisté (`HtBreakdownJson` non signée), FEC exact avec repli legacy et contrôle de cohérence | fait | 636 | 405 | - | aucun bloquant (voir ci-dessous) |
| 7 Documentation, rappel du rejeu iPad et vérification finale | fait | 636 | 405 | 52 (verts) | voir ci-dessous |

## Totaux finaux

Statut : terminé. .NET 636 (Domain 47, Infrastructure 412, Api 177) · PosKit 405 · Tests UI 52 (verts).

## Findings ouverts restants (non bloquants)

- M2 : borne haute de l'absorbeur du FEC (reçus legacy sans `HtBreakdownJson` seulement).
- M3 : libellé « Pourboire » appliqué à tout excédent du FEC.
- m3 Swift : valeur par défaut `htBreakdownJson = nil` acceptée.
- `InMemoryPosAPI.swift:396` (double de test / `-UITestMode`) : répartition de TVA d'un reçu partiel selon l'ancienne règle ; hors périmètre.
- Recommandations mineures de la revue de la tâche 2 : `tenders` de la fixture reconstruit à part, ordre des clés JSON reposant sur l'ordre d'insertion du `Dictionary`, pas de scénario combinant total nul et pourboire.
- Compte 468600 (pourboires) à valider par l'expert-comptable.
- Écart pré-existant : `docs/fiscal.md` décrit un FEC « barres verticales, virgule décimale » alors que le code écrit des tabulations et des points.
- Rejeu Swift de `day_scenario.json` à faire au sous-projet 3 (condition 8 de `ipad-standalone-2-progress.md`).
