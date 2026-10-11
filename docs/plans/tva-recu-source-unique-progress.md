# TVA du reçu à source unique — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-11-tva-recu-source-unique.md`

Base de référence (avant tâche 1) : PosKit 394 · UI 52 · .NET 418

| Tâche | Statut | .NET | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 Répartiteur `TaxAllocation`, ventilation `Order.CalculateTaxBreakdown`, amendement de la spec | fait | 443 | 394 (1 échec attendu : parité `fiscal_reference.json`, HT remise fixe 2576 -> 2318, à résoudre côté Swift) | - | aucun |
| 2 Reçu tiré de la ventilation (`ReceiptTaxCalculator`), scénarios partagés `FiscalScenarios`, invariants | fait | 591 | non relancé (FiscalReceiptMathTests rouges attendus jusqu à la tâche 4) | - | aucun |
| 3 Fixture enrichie (bloc `order` + groupes, `tenders` réduits au montant réglé) | fait | 591 | non relancé (rouge attendu jusqu à la tâche 4) | - | aucun |
