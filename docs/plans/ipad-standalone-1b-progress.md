# iPad standalone 1b — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-07-ipad-standalone-1b-commandes-cuisine.md`

Base de référence (avant tâche 1) : PosKit 203 · .NET et web inchangés (aucun fichier .NET ni web modifié)

| Tâche | Statut | PosKit | Findings de revue ouverts |
|---|---|---|---|
| 1 Migration v2 et dépôts | fait | 208 | aucun |
| 2 Commandes de salle | fait | 219 | aucun |
| 3 Envoi cuisine et bons | fait | 227 | aucun |
| 4 Transfert et fusion | fait | 233 | aucun |
| 5 Remises et gratuités | fait | 240 | aucun bloquant (voir différés) |

Tests : PosKit 244 (240 + 4 tests de la vague de correctifs finale) · .NET build OK (aucun fichier .NET modifié) · web inchangé.

Findings de revue différés (connus) :
- Expiration du verrouillage PIN (plan 1a) non portée.
- Atomicité sous panne disque réelle : couverte par les transactions, non simulée.
- Normalisation `T01 ↔ T1` non portée.
- `removeDiscount` sur commande inconnue : 404 (le .NET lève une exception non gérée), écart assumé.

Vague de correctifs finale (revue de la branche) :
- `addItems` ne fusionne plus une ligne identique dans une ligne offerte ou remisée (le produit ajouté restait gratuit).
- La fusion de tables est refusée si l'une des deux commandes porte une remise globale (elle disparaissait ou s'étendait en silence) ; remettre la remise à zéro puis fusionner.
- `compItem` refuse une ligne déjà offerte (pas de second enregistrement d'audit).
- Tests de lecture directe de la base : journal de transfert et statut « annulée » de la commande fusionnée.

Conditions d'entrée du plan 1c (issues de la revue finale, non bloquantes pour 1b) :
1. Garde de statut (`open`, `sentToKitchen`, `billRequested`) sur `applyDiscount`, `removeDiscount` et `setDestination` dès que des commandes payées existent (aujourd'hui une commande annulée par fusion accepte encore une remise).
2. Éligibilité titre-restaurant par ligne : `IsFoodVoucherEligible` est figé à 1 (alcool compris) car `OrderLine` et `OrderItemInput` n'ont pas ce champ.
3. Les bons cuisine gardent l'ancienne table et l'ancienne commande après un transfert ou une fusion (à traiter avec l'impression, sous-projet 4).
4. Reprendre aussi les conditions d'entrée consignées dans `docs/plans/ipad-standalone-1a-progress.md` (verrouillage PIN persistant, pas de comptes de démo en production, instance SQLite unique avec `busy_timeout`).
