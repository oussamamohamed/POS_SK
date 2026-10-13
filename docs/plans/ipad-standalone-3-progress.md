# iPad standalone 3 — suivi d'exécution

Condition de sortie : la journée simulée (`day_scenario.json`) passe côté iPad. Conditions d'entrée : `ipad-standalone-2-progress.md`.

Base de référence : .NET 753 (68 + 428 + 257) · PosKit 420

| Tâche | Statut | .NET | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|---|
| Rapports X, Z et vérification des chaînes (commit b953d35) | fait | 753 | 420 | - | aucun |
| Rejeu de la journée simulée (`LocalDaySimulationTests`) : X/Z des 3 postes = grand livre .NET | fait | 753 | 423 | - | aucun |
| Refus d'annulation après Z (409, miroir de `void_after_closure`), écart relevé par le rejeu | fait | 753 | 423 | - | aucun |

Reste : FEC et export par partage, écrans d'annulation de reçu et de restauration, duplicata imprimés (sous-projet 4), attestation NF525.
Le rejeu compare X, Z, TVA par taux, règlements, nombre de reçus et grand total perpétuel, poste par poste (chaque poste du scénario est rejoué sur sa propre base `T01`).
