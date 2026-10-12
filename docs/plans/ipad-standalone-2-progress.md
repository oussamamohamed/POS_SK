# iPad standalone 2 — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-10-ipad-standalone-2-fiscal-nf525.md`

Base de référence (avant tâche 1) : PosKit 335 · UI 47 · .NET 417

| Tâche | Statut | .NET | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 FiscalHashing et vecteurs .NET | fait | 418 | 342 | - | aucun |
| 2 FiscalReceiptMath (montants, TVA, HT) et scénarios .NET | fait | 418 | 344 | - | aucun |
| 3 Migration V5 (tables fiscales, triggers) | fait | 418 | 348 | - | aucun |
| 4 LocalFiscalRepository (reçus chaînés, annulation, duplicata, JET, vérification) | fait | 418 | 360 | - | aucun |
| 5 Encaissement, chambre, annulation et duplicata sur la chaîne fiscale | fait | 418 | 369 | - | aucun |
| 6 Audit JET : dérogations Happy Hour et annulations de mise en attente | fait | 418 | 372 | - | aucun |
| 7 Sauvegarde chiffrée (format, écriture, rotation, restauration) | fait | 418 | 386 | - | aucun |
| 8 Sauvegarde dans LocalPosAPI (export, programmée, état, Keychain) | fait | 418 | 393 | - | aucun |
| 9 Interface : retrait du bandeau non fiscal, réglages et bandeau de sauvegarde | fait | 418 | 394 | 52 | aucun |
| 10 Documentation, règle de suivi .NET/Swift et vérification finale | fait | 418 | 394 | 52 | aucun |

Sous-projet 2 « Fiscal NF525 » : terminé (condition de sortie : `FiscalHashingTests` et `FiscalReceiptMathTests` rejouent `fiscal_reference.json`, généré par le .NET).

Conditions d'entrée du sous-projet 3 :
1. Tables `DailyFiscalClosures`, `FiscalPeriodClosures`, `FiscalArchives` (migration V6) avec leurs triggers, et extension de `verifyChains` aux chaînes restantes.
2. Section Fiscal visible en mode autonome (retirer le masquage de `SidebarRail`), rapports X et Z, FEC, export via le partage.
3. Impression des duplicata : `reprintReceipt` ne fait que numéroter et journaliser (`printQueued: false`) tant que le sous-projet 4 n'existe pas.
4. Interface d'annulation de reçu : `voidReceipt` n'a pas d'écran (le client serveur n'en a pas non plus). Un reçu portant un règlement « chambre » ne peut pas être annulé (409) : la contre-passation du folio de la chambre est une fonctionnalité future.
5. Écran de réglages de la base et restauration depuis l'interface : `LocalBackupService.restore` existe et est testée, sans écran.
6. Attestation NF525 à faire couvrir le mode autonome avant tout usage réel.
7. Décider de corriger ou non les écarts .NET reproduits pour la parité (TVA et HT du reçu), des deux côtés. **Décidé le 2026-10-11 : on corrige** (branche `fix/tva-recu-source-unique`, spec `docs/superpowers/specs/2026-10-11-tva-recu-source-unique-design.md`).
8. **À NE PAS OUBLIER : rejouer côté iPad la « journée simulée »** de la spec `2026-10-11-tva-recu-source-unique-design.md` (plusieurs serveurs, commandes sur place et à emporter, remises, pourboires, règlements mixtes, annulations, duplicata, X en cours de journée, Z final) dès que les rapports X et Z du mode autonome existent. Le scénario est décrit comme des données (opérations + totaux attendus du grand livre indépendant) pour pouvoir être rejoué en Swift et comparé aux mêmes totaux que le .NET. Le scénario est dans `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/day_scenario.json` (opérations + grand livre attendu par terminal). Condition de sortie du sous-projet 3 : ce test passe côté iPad.
