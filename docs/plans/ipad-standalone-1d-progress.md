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
| Correctifs de la revue finale | fait | 329 | I1, I2, I3 (reporté 1e), M2, M3, M4, M9 |

Conditions d'entrée du plan 1e (application SwiftUI : le mode devient sélectionnable) :
1. Choix « Serveur / Autonome » au premier lancement, mémorisé ; `AppEnvironment.makeModel` construit `AppModel(api: LocalPosAPI(path: LocalDatabaseLocation.defaultURL().path, seed: .blank))` en mode autonome.
2. **Une seule instance de `LocalPosAPI` par fichier**, détenue par `AppEnvironment` et jamais recréée tant que l'app tourne (la création d'une seconde instance sur le même fichier est interdite) ; la sauvegarde `backup(to:)` passe par cet acteur.
3. Écrans de première configuration branchés sur `needsSetup()` / `completeFirstRun(_:)` (responsable, PIN à 4 chiffres, établissement, SIRET) ; aucun compte de démonstration en production.
4. `TerminalSettings` pour le mode autonome : identité de poste `T01` (`LocalPosAPI.standaloneTerminalId`) sans écran d'appairage ni découverte Bonjour.
5. Bandeau « non fiscal » visible tant que le sous-projet 2 n'est pas livré (condition 5 du plan 1c).
6. Export de la sauvegarde par la feuille de partage iOS (Fichiers/iCloud) ; planification de la sauvegarde quotidienne.
7. XCUITest en mode autonome (`-UITestLocal` : `LocalPosAPI` en mémoire, `seed: .demo`) couvrant le parcours table complet — condition de sortie du sous-projet 1.
8. Clés de localisation (FR/EN/AR) des nouveaux écrans ; messages d'erreur de `Local/` laissés en français comme les autres.
9. Chiffrer l'export de sauvegarde avant de le proposer vers Fichiers/iCloud : le hash SHA-256 d'un PIN à 4 chiffres est cassé en millisecondes, un fichier `backup(to:)` livre tous les PIN (revue finale I3).
10. En production toujours `seed: .blank`, avec un test qui ouvre le fichier de production sans comptes de démonstration (le défaut `.demo` est réservé aux tests).
11. Bloquer toute l'app sur `needsSetup()` (sur base vierge `settings()` lève « Réglages du restaurant absents ») et aligner le prédicat de `needsSetup` (aucun responsable actif) avec la garde 409 de `completeFirstRun` (aucun compte).
12. `backup(to:)` peut laisser un fichier partiel (disque plein) et sa copie n'a pas de protection de fichier explicite : nettoyer en cas d'échec et protéger la copie.
13. Le verrouillage des PIN est global à l'iPad (n'importe qui peut bloquer tout le monde 30 s, comme le serveur) ; une connexion réussie ne vide plus le compteur (les échecs vieillissent seuls après 60 s) ; un verrou à plus de 30 s dans le futur est tenu pour expiré (horloge reculée).
14. Rappel pour le sous-projet 2 : ne pas signer du texte décimal relu tel quel (la base écrit « 20 », le .NET « 20.0 ») ; formater explicitement les montants signés.
Restent ouverts (sous-projet 2) : clôture d'une commande à solde nul, `chargeRoom` sans reçu `NF-…`, audit JET des dérogations Happy Hour et des annulations de mise en attente.
