# iPad standalone 1d — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-08-ipad-standalone-1d-imprimantes-happy-hour-durcissement.md`

Base de référence (avant tâche 1) : PosKit 288 · .NET et web inchangés (aucun fichier .NET ni web modifié)

| Tâche | Statut | PosKit | Findings de revue ouverts |
|---|---|---|---|
| 1 Migration v4, horloge, PIN persistants | fait | 294 | |
| 2 Terminal fixe T01 | fait | 295 | |
| 3 Durcissement SQLite et sauvegarde | fait | 301 | |
| 4 Configuration des imprimantes | fait | 306 | |
| 5 Happy Hour : plannings et règles | fait | 313 | |
| 6 Happy Hour : statut, tarifs, dérogations | fait | 319 | |
| 7 Réseau, base vierge, première configuration | fait | 326 | |

Conditions d'entrée du plan 1e (application SwiftUI : le mode devient sélectionnable) :
1. Choix « Serveur / Autonome » au premier lancement, mémorisé ; `AppEnvironment.makeModel` construit `AppModel(api: LocalPosAPI(path: LocalDatabaseLocation.defaultURL().path, seed: .blank))` en mode autonome.
2. **Une seule instance de `LocalPosAPI` par fichier**, détenue par `AppEnvironment` et jamais recréée tant que l'app tourne (la création d'une seconde instance sur le même fichier est interdite) ; la sauvegarde `backup(to:)` passe par cet acteur.
3. Écrans de première configuration branchés sur `needsSetup()` / `completeFirstRun(_:)` (responsable, PIN à 4 chiffres, établissement, SIRET) ; aucun compte de démonstration en production.
4. `TerminalSettings` pour le mode autonome : identité de poste `T01` (`LocalPosAPI.standaloneTerminalId`) sans écran d'appairage ni découverte Bonjour.
5. Bandeau « non fiscal » visible tant que le sous-projet 2 n'est pas livré (condition 5 du plan 1c).
6. Export de la sauvegarde par la feuille de partage iOS (Fichiers/iCloud) ; planification de la sauvegarde quotidienne.
7. XCUITest en mode autonome (`-UITestLocal` : `LocalPosAPI` en mémoire, `seed: .demo`) couvrant le parcours table complet — condition de sortie du sous-projet 1.
8. Clés de localisation (FR/EN/AR) des nouveaux écrans ; messages d'erreur de `Local/` laissés en français comme les autres.
Restent ouverts (sous-projet 2) : clôture d'une commande à solde nul, `chargeRoom` sans reçu `NF-…`, audit JET des dérogations Happy Hour et des annulations de mise en attente.
