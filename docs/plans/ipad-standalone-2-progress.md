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
| 9 Interface : retrait du bandeau non fiscal, réglages et bandeau de sauvegarde | fait | 418 | 393 | 50 | aucun |
