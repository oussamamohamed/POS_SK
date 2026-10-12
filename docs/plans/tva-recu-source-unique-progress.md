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

## Finitions (branche `fix/finitions-tva-recu`)

Tous les findings mineurs du lot sont traités, sauf les deux points ci-dessous. .NET 736 (Domain 68, Infrastructure 428, Api 240 ; ces totaux incluent les fichiers de test non suivis déjà présents dans l'arbre de travail) · PosKit 411. Tests UI non relancés : aucun montant visible modifié (le rapport X de `testFiscalXAndZReports` vérifie un règlement unique, 14,50, inchangé).

| Finding | Résolution |
|---|---|
| Poids négatifs (`TaxAllocation.Allocate`, `OrderMath.allocate`) | Résolu : l'API n'a aucun usage légitime d'une ligne négative ; `POST /api/tables/{t}/items` renvoie 400 (`errors.order_item_invalid`) si quantité ≤ 0, prix < 0 ou supplément < 0 ; `LocalPosAPI.addItems` aligné (même condition, même message) ; garde défensive des deux côtés (poids négatif compté 0), test `[-1,-1,5]`. |
| M3 : libellé « Pourboire » sur tout excédent du FEC | Résolu : libellé neutre « Excédent de règlement / pourboire » conservé ; le surpaiement lui-même est désormais refusé (voir « Surpaiement et règlements ≤ 0 »). |
| M2 : absorbeur sans borne haute du repli FEC | Non traité volontairement. Le repli ne sert plus qu'aux JSON incohérents et aux bases antérieures à la colonne ; suppression à décider (voir rapport). |
| m3 Swift : valeur par défaut `htBreakdownJson = nil` | Résolu : propriété sans défaut, appelants explicites. |
| `InMemoryPosAPI` : TVA d'un reçu partiel selon l'ancienne règle | Résolu : alignée sur `FiscalReceiptMath.amounts`, test de parité `InMemoryReceiptParityTests`. |
| Pas de scénario « total nul + pourboire » | Résolu : deux scénarios ajoutés (comp et remise 100 %), fixture régénérée (142 scénarios), rejeu Swift vert. |
| `docs/fiscal.md` : FEC « barres verticales, virgule » | Résolu : tabulations et points, comme le code. |
| Spec : `isPartial` et « schéma (aucun) » | Résolu. |
| Surpaiement et règlements ≤ 0 | Résolu : `CheckoutPaymentService.ProcessPaymentTendersAsync` refuse (`IsSuccess = false`, rien d'écrit) un règlement ≤ 0, un total réglé supérieur au solde dû (pourboire inclus dans le dû ; le rendu monnaie passe par `Tendered`) et une liste vide hors commande à total nul. `checkout/pay`, `orders/counter/checkout` (qui renvoyait 200 sur échec) et `sync/receipt` (plus de ligne à prix négatif ni de commande orpheline) répondent 400. Les refus se décident avant toute écriture : `PaymentSettlementRules` (règles partagées avec le service) est évalué par le comptoir avant numéro de retrait, pourboire et avoir (l'avoir n'est émis qu'après le règlement enregistré), et par `checkout/pay` avant de créer une commande à la volée (règlements tous > 0). Une commande réglée ou annulée n'est plus encaissable (parité `isModifiable` de `LocalPosAPI.settle`). Une valeur de remise à plus de 2 décimales est refusée en 400 (`errors.discount_value_precision`), le web arrondit à la saisie. Parité iPad : `LocalPosAPI.settle` refusait déjà, même message « Échec de l'encaissement. ». |
| m1 : prix négatif à la modification d'un article | Résolu : `UpdateProductAsync` refuse comme `CreateProductAsync` ; les endpoints répondent 400 (`errors.product_price_negative`) ; `min="0"` sur `editProdPrice` ; `LocalPosAPI.createProduct` / `updateProduct` alignés. |
| m3 : test `errors.order_item_invalid` | Résolu : le test vérifie le texte localisé. |
| Client web : total TTC des tables en flottant | Résolu : `calculateTotalTtc` calcule en centimes entiers avec les arrondis du serveur (par ligne, puis remise de commande). |

## Findings ouverts restants

- Compte 468600 (pourboires et excédents de règlement) à valider par l'expert-comptable.
- Rejeu Swift de `day_scenario.json` à faire au sous-projet 3 (condition 8 de `ipad-standalone-2-progress.md`).
- FEC : séparateur décimal (point ou virgule) à valider avec Test Compta Demat / l'expert-comptable (la tabulation est certaine) ; `docs/fiscal.md` ne prétend plus à la conformité A.47 A-1.
