# Recherche : décisions techniques NF525

Chaque décision part du code existant (état au 2026-09-30, `main` après la PR #5).

## R1 — Formules des nouvelles chaînes (INV-1)

**Décision** : SHA-256, hexadécimal minuscule, champs séparés par `|`, dates en `:O` (ISO 8601 aller-retour, UTC), départ au `GenesisHash` existant. Les formules des reçus et des Z ne changent pas.

| Chaîne | Formule |
|---|---|
| JET | `SHA256(previousHash\|chainSequence\|eventType\|occurredAtUtc:O\|terminalId\|operatorId\|payloadJson)` |
| Clôture de période | `SHA256(previousHash\|terminalId\|periodType\|periodKey\|totalTtcCents\|totalHtCents\|taxesJson\|tendersJson\|perpetualGrandTotalCents\|periodEndUtc:O)` |
| Archive | `SHA256(previousHash\|periodType\|periodKey\|fileSha256\|createdAtUtc:O)` |

`periodKey` = `2026-09` (mois) ou `2026` (exercice, année de début). Les JSON sont sérialisés avec `System.Text.Json` par défaut sur des dictionnaires triés par clé (ordre stable).

**Justification** : même primitive que l'existant, pas de clé à gérer. **HMAC rejeté** : il faudrait stocker et protéger une clé, et la vérification par un tiers deviendrait impossible sans elle.

## R2 — Une seule chaîne JET, côté serveur

**Décision** : une chaîne JET unique (le terminal est un champ, pas une chaîne séparée). Numéro `ChainSequence` attribué dans une transaction `Serializable` (`ExecuteInTransactionAsync`, déjà utilisé pour les reçus et les Z).

**Entrées existantes** : non chaînées (`EntryHash` aléatoire). Elles gardent `ChainSequence = NULL` ; la chaîne commence à la première entrée nouvelle avec `PreviousHash = GenesisHash`. La vérification ignore les entrées sans `ChainSequence` et les compte à part (« héritées »).

**Transaction** : quand l'événement accompagne une écriture fiscale (annulation, clôture, archive), l'entrée JET est ajoutée dans la même transaction. Sinon (connexion, paramètre), dans sa propre transaction ; un échec d'écriture JET est journalisé (`[LoggerMessage]`) sans bloquer la connexion.

## R3 — Clôtures de période par terminal

**Décision** : la clôture mensuelle et annuelle suit la même portée que la Z : **par terminal**, en cumulant les `DailyFiscalClosure` de ce terminal dont `PeriodEndUtc` tombe dans la période (heure locale du serveur). Chaîne propre par terminal et par type (mois, exercice).

**Justification** : la Z du terminal principal couvre déjà tous les terminaux ; cumuler entre terminaux compterait deux fois les reçus des iPad.

**Refus** : période non terminée ; période déjà clôturée ; pour le mois, jour du mois ayant eu des reçus sur ce terminal sans Z couvrant ce jour ; pour l'exercice, mois sans clôture mensuelle.

**Grand total perpétuel** : repris de la dernière Z de la période (jamais recalculé ni remis à zéro).

## R4 — Annulation sans modifier le reçu (US3, clarification : état déduit de l'avoir)

**Décision** : l'annulation n'écrit plus `original.IsVoid = true` (`CheckoutPaymentService.cs:231`). « Annulé » = il existe un reçu dont `VoidedReceiptId == original.Id`.

- Remplacer chaque lecture de `IsVoid` par ce prédicat : `CheckoutPaymentService` (garde de double annulation, reçus antérieurs), `CheckoutEndpoints.cs:100`, `NF525FiscalAuditService.cs:347`, `FecExportService.cs:89`, `FinancialDashboardService.cs:44`, `ReportPrintDataService.cs:33`.
- **Totaux identiques** : chaque requête garde exactement sa sémantique actuelle (seul l'original est exclu, l'avoir reste inclus là où il l'est). Tests de non-régression avant/après sur X, Z, rapports, tableau de bord, FEC.
- **Colonne `IsVoid`** : conservée en base (SQLite ne la supprime pas simplement) et retirée du modèle d'écriture ; les lignes historiques à `1` sont ignorées. La propriété n'est plus lue.
- Index `(VoidedReceiptId)` pour le prédicat.

`FinancialDashboardService` excluait l'original mais comptait l'avoir négatif (double soustraction) : corrigé sur la branche `fix/tableau-de-bord-annulations` (filtre `VoidedReceiptId == null`). Sa sémantique à conserver : ni l'original annulé ni l'avoir.

## R5 — Garde anti-modification (INV-3)

**Décision** : `SaveChangesInterceptor` enregistré sur `AppDbContext` ; refuse (`InvalidOperationException`) tout `Modified` ou `Deleted` sur `FiscalReceipt`, `PaymentTender`, `DailyFiscalClosure`, `FiscalPeriodClosure`, `FiscalArchive`, et sur `TransactionJournalEntry` dès qu'elle a un `ChainSequence`. Actif aussi en `Testing`.

**Prérequis** : R4 terminé (sinon l'annulation échoue). Recenser au préalable toute autre écriture sur ces entités (`grep` des affectations) et la traiter.

**Triggers SQLite rejetés** : le bloc de schéma de `Program.cs` peut les créer, mais ils ne s'appliqueraient pas en `Testing` (InMemory) ; la vérification (US1) reste le filet contre une modification hors application.

## R6 — Duplicatas (US6)

**Décision** : un duplicata est un `PrintJob` portant `DuplicateOfDocumentId` (reçu ou clôture) et `DuplicateNumber` (max + 1 pour ce document, attribué en transaction). Le document imprimé porte « DUPLICATA n°N ». Chaque duplicata écrit une entrée JET.

Points d'entrée : `POST /api/fiscal/latest-closure/print` (déjà existant, devient un duplicata), nouvelle route de réimpression de ticket, et `POST /api/print-jobs/{id}/retry` quand le job d'origine est `Done` (un job en échec relancé n'est pas un duplicata).

## R7 — Identité et exercice (US7, clarifications)

**Décision** : étendre le singleton `RestaurantSettings` (déjà exposé par `GET/PUT /api/settings`) ; pas de nouvelle entité. `TicketDocumentBuilder` lit ces valeurs au lieu de l'en-tête en dur (l.17).

- Exercice : `FiscalYearStartMonth` (1–12) et `FiscalYearStartDay` (1–28), pour éviter les dates impossibles (30 février).
- Numéro de certificat : **facultatif en saisie** ; tant qu'il est vide, le ticket imprime la version du logiciel seule et l'écran fiscal affiche « certification en cours ». Il devient obligatoire à l'impression une fois renseigné (clarification : objectif de certification).
- Version du logiciel : `AssemblyInformationalVersion` de l'API.
- Toute modification de ces champs écrit une entrée JET.
- Modification de l'exercice refusée s'il existe déjà une clôture annuelle (cohérence des périodes).

## R8 — Archives (US5)

**Décision** : archive = fichier ZIP contenant `receipts.json`, `closures.json`, `period-closures.json`, `journal.json` et `manifest.json` (période, comptes, SHA-256 de chaque fichier). Le serveur calcule le SHA-256 du ZIP et enregistre une ligne `FiscalArchive` chaînée (R1) + une entrée JET. Fichier stocké sous `Archives:Path` (configuration, défaut `archives/` à côté de la base).

**Vérification** : recalcul du SHA-256 du fichier fourni (ou stocké) et comparaison avec la ligne `FiscalArchive`, puis vérification de la chaîne des archives.

**Portée** : archive d'une clôture mensuelle ou annuelle existante uniquement.

**Purge** : aucune route de suppression n'existe ; l'intercepteur (R5) refuse tout `Deleted`. Pas de fonction de purge dans ce lot.

**Signature asymétrique non retenue** : elle demande une gestion de clé. Si les exigences officielles INFOCERT ne sont pas obtenues dans T002, repli nominal sur la formule de chaînage SHA-256 R1 (`SHA256(previousHash|periodType|periodKey|fileSha256|createdAtUtc:O)`). T061 n'est pas bloqué.

## R9 — Références réglementaires (vérifiées le 2026-09-30)

- **Justificatif de conformité** : la loi de finances 2025 (art. 43) réservait la preuve à un certificat d'organisme accrédité, avec obligation au 1er septembre 2026 ; **la loi de finances 2026 (art. 125) rétablit l'attestation individuelle de l'éditeur** (source : Legifiscal). La certification (clarification : objectif INFOCERT) reste un choix commercial, pas une obligation légale : le numéro de certificat reste donc facultatif (R7).
- **Codes d'événements JET** : le référentiel NF525 (exigence R19) fixe une liste codée d'événements, obligatoires (« X ») ou conditionnels (« C »). La table complète de correspondance (codes 01 à 91) est consignée dans `spec.md` (US2) et reprise dans `JournalEventTypes`.
- **Conservation** : 6 ans (droit de communication, LPF art. L102 B) — cohérent avec la clarification du 2026-09-30.

Sources :
- https://www.legifiscal.fr/actualites-fiscales/4460-loi-finances-2026-retablissement-auto-certification-logiciels-caisse.html
- https://www.legifiscal.fr/actualites-fiscales/4098-fin-auto-certification-logiciels-caisse-report-1er-septembre-2025.html
- https://support.retailforce.cloud/hc/en-gb/articles/7076661762961-EventCode-mapping-Technical-Event-Log-NF525-R19
