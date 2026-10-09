# iPad standalone 1e — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-09-ipad-standalone-1e-application-mode-autonome.md`

Base de référence (avant tâche 1) : PosKit 329 · .NET et web inchangés (aucun fichier .NET ni web modifié)

| Tâche | Statut | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|
| 1 Briques PosKit | fait | 335 | - | aucun |
| 2 Environnement, aiguillage, choix du mode | fait | 335 | SessionUITests + PairingUITests verts | aucun |
| 3 Première configuration | fait | 335 | - (tâche 4) | aucun |
| 4 Tests UI et documentation | fait | 335 | 47 (StandaloneUITests 6/6), tous verts | aucun |

Sous-projet 1 « Socle local » : terminé (conditions de sortie : parcours table complet en mode autonome, `testFullTableJourneyWorksOnTheLocalBackend`).

Conditions d'entrée du sous-projet 2 (fiscal NF525) :
1. Remplacer `NonFiscalPayments` par `FiscalReceipts`/`PaymentTenders` signés (migration dédiée) ; retirer le bandeau « non fiscal » et rendre la section Fiscal visible quand les clôtures existent (sous-projet 3).
2. Clôture d'une commande à solde nul (100 % offerte ou remisée) et `chargeRoom` sans reçu `NF-…` (conditions 6 et 7 du plan 1c).
3. Sauvegarde : export de la base **chiffré** avant tout envoi vers Fichiers/iCloud (le hash SHA-256 d'un PIN à 4 chiffres est cassé en millisecondes), sauvegarde quotidienne automatique, protection de fichier de la copie, fichier partiel en cas de disque plein (`backup(to:)`).
4. Audit JET des dérogations Happy Hour et des annulations de mise en attente.
5. Ne pas signer du texte décimal relu tel quel de la base (la base écrit « 20 », le .NET « 20.0 »).
6. Écran de réglages de la base (emplacement, version de schéma, sauvegardes) si le besoin se confirme ; basculement de mode sans réinstallation seulement avec la synchronisation.
