# Conformité Fiscale NF525 (Loi Anti-Fraude TVA)

Documentation d'architecture, formules cryptographiques et procédures opérationnelles relatives à la conformité NF525 de **POS_SK**.

---

## 1. Cadre Réglementaire & Piliers NF525

Conformément à l'article 286, I-3° bis du Code Général des Impôts (CGI), le logiciel de caisse garantit les quatre conditions d'inaltérabilité, de sécurisation, de conservation et d'archivage des données d'encaissement.

- **Inaltérabilité (R1)** : toute écriture enregistrée ne peut être ni modifiée ni supprimée. Les corrections sont tracées exclusivement par contre-passation (tickets d'annulation).
- **Sécurisation (R2)** : chaque donnée fiscale est scellée par calcul d'empreinte cryptographique SHA-256 enchaînée au document précédent.
- **Conservation (R3)** : toutes les données fiscales, clôtures et journaux d'audit sont conservés en ligne avec accès immédiat en lecture et contrôle d'intégrité.
- **Archivage (R4)** : génération d'archives périodiques autonomes (fichiers ZIP horodatés, scellés, chaînés entre eux et vérifiables de manière indépendante).

---

## 2. Chaînes Cryptographiques & Formules de Signature

Toutes les chaînes utilisent l'algorithme **SHA-256** calculé sur une chaîne UTF-8 avec le séparateur tube (`|`).
Le hachage initial de genèse (`GenesisHash`) pour le premier élément d'une chaîne est composé du préfixe `GENESIS_` suivi de 64 zéros :
`GENESIS_0000000000000000000000000000000000000000000000000000000000000000`

### 2.1 Chaîne des Reçus Fiscaux (`FiscalReceipt`)
- **Portée** : partitionnée par terminal (`terminalId`).
- **Formule** :
  ```
  SHA256(previousHash|terminalId|sequenceNumber|amountCents|timestampUtc:O|taxBreakdownJson)
  ```
- **Format de sortie** : hexadécimal majuscule (`Convert.ToHexString`).
- **Détails des champs** :
  - `previousHash` : signature du ticket précédent sur ce terminal (ou `GenesisHash`).
  - `terminalId` : identifiant du terminal émetteur (`T01`, `T02`, etc.).
  - `sequenceNumber` : séquence incrémentale continue 1, 2, 3... sans rupture.
  - `amountCents` : montant TTC en centimes entiers (`long`).
  - `timestampUtc:O` : horodatage ISO 8601 UTC au format standard (`yyyy-MM-ddTHH:mm:ss.fffffffZ`).
  - `taxBreakdownJson` : dictionnaire des montants de TVA sérialisé en JSON canonique.

### 2.2 Chaîne des Clôtures Journalières Z (`DailyFiscalClosure`)
- **Portée** : partitionnée par terminal (`terminalId`).
- **Formule** :
  ```
  SHA256(previousHash|terminalId|closureSequence|totalSalesTtcCents|periodEndUtc:O|taxesSummaryJson)
  ```
- **Format de sortie** : hexadécimal majuscule.
- **Détails des champs** :
  - `previousHash` : signature de la clôture Z précédente sur ce terminal.
  - `closureSequence` : séquence de clôture journalière Z1, Z2, Z3...
  - `totalSalesTtcCents` : chiffre d'affaires TTC de la journée sur ce terminal.
  - `periodEndUtc:O` : date et heure de clôture effective.
  - `taxesSummaryJson` : ventilation de la TVA du Z.

### 2.3 Chaîne du Journal des Événements Techniques (JET - `TechnicalJournalEntry`)
- **Portée** : globale au restaurant (chaîne unique tous terminaux confondus).
- **Formule** :
  ```
  SHA256(previousHash|chainSequence|eventType|occurredAtUtc:O|terminalId|operatorId|payloadJson)
  ```
- **Format de sortie** : hexadécimal minuscule (`Convert.ToHexStringLower`).
- **Événements tracés** :
  - `Z_CLOSURE` : exécution d'un rapport Z journalier.
  - `PERIOD_CLOSURE` : clôture mensuelle ou annuelle.
  - `RECEIPT_VOIDED` : annulation de reçu par contre-passation.
  - `DUPLICATE_PRINTED` : réimpression d'un duplicata (ticket de caisse, clôture Z scellée ou job d'impression ; le champ `documentType` du payload distingue le type).
  - `DEVICE_PAIRED`, `DEVICE_REVOKED` : appairage et révocation de terminaux iPad.
  - `FISCAL_SETTINGS_CHANGED` : modification de l'identité légale (SIREN, raison sociale, exercice fiscal).
  - `FEC_EXPORTED` : génération d'un export comptable FEC.
  - `ARCHIVE_CREATED`, `ARCHIVE_EXPORTED` : création et extraction d'archives fiscales scellées.

### 2.4 Chaîne des Clôtures de Période (`FiscalPeriodClosure`)
- **Portée** : partitionnée par terminal (`terminalId`) et type (`Monthly` = 1, `Annual` = 2).
- **Formule** :
  ```
  SHA256(previousHash|terminalId|periodType|periodKey|totalTtcCents|totalHtCents|taxesJson|tendersJson|perpetualGrandTotalCents|periodEndUtc:O)
  ```
- **Format de sortie** : hexadécimal minuscule.
- **Règles métier** :
  - `periodKey` : `YYYY-MM` pour mensuelle, `YYYY` pour annuelle (ou clé d'exercice décalé).
  - Total calculé à partir de la somme exacte au centime des clôtures Z de la période.
  - Grand total perpétuel repris de la dernière clôture journalière Z.
  - Répétition interdite (code erreur HTTP 409 `period_already_closed`).

### 2.5 Chaîne des Archives Fiscales Scellées (`FiscalArchive`)
- **Portée** : globale au restaurant.
- **Formule (R1 Archive Chaining)** :
  ```
  SHA256(previousHash|periodType|periodKey|fileSha256|createdAtUtc:O)
  ```
- **Format de sortie** : hexadécimal minuscule.
- **Détails des champs** :
  - `fileSha256` : empreinte SHA-256 du fichier ZIP d'archive complet stocké sur disque.
  - `createdAtUtc:O` : date et heure de génération de l'archive.

---

## 3. Inaltérabilité & Sécurité des Données

### 3.1 Intercepteur EF Core (`FiscalImmutabilityInterceptor`)
Pour interdire toute tentative logicielle de modification directe ou de suppression des enregistrements fiscaux, un intercepteur EF Core est enregistré au niveau du pipeline de persistance :
- `SavingChangesAsync` inspecte le `ChangeTracker` d'Entity Framework.
- Toute entité de type `FiscalReceipt`, `DailyFiscalClosure`, `TechnicalJournalEntry`, `FiscalPeriodClosure` ou `FiscalArchive` dont le statut est `EntityState.Modified` ou `EntityState.Deleted` lève immédiatement une `InvalidOperationException` dont le message nomme l'entité (par ex. « NF525 INV-3: Fiscal receipts are append-only and immutable. »). `PaymentTender` et les entrées JET chaînées sont protégés de la même façon ; les entrées JET héritées (sans `ChainSequence`) restent modifiables.

### 3.2 Gestion des Annulations (Contre-passation)
Aucun ticket n'est jamais supprimé physiquement :
1. Un reçu original reste intact dans la base et dans sa chaîne.
2. L'annulation crée un nouveau `FiscalReceipt` de type annulation (liée à l'original par `VoidedReceiptId` ; `IsVoid` est obsolète et n'est plus écrit), signé dans la chaîne courante du terminal émetteur avec un montant TTC et des bases TVA négatives.
3. Un événement JET `RECEIPT_VOIDED` est automatiquement journalisé dans la transaction.

---

## 4. Procédures Opérationnelles

### 4.1 Clôture Journalière (Z)
1. **Contrôle préalable** : aucune commande ne doit être en cours sur les tables ou au comptoir. Si des commandes restent ouvertes, le Z est refusé (`409 has_open_orders`).
2. **Rapport X (Aperçu)** : consultation à tout moment sans scellement ni incrémentation de séquence.
3. **Exécution du Z** :
   - Calcul des cumuls de la journée (TTC, HT, ventilation TVA, ventilation règlements).
   - Mise à jour du Grand Total perpétuel (cumul continu depuis la mise en service).
   - Scellement du Z avec la signature de chaîne.
   - Enregistrement dans le JET (`Z_CLOSURE`).
   - Impression automatique du ticket Z scellé sur imprimante thermique.

### 4.2 Clôtures Périodiques (Mois & Année)
1. Sélectionner le type (Mensuelle ou Annuelle) et la période (`YYYY-MM` ou `YYYY`).
2. Le système vérifie que la période est échue et que toutes les clôtures journalières sous-jacentes sont présentes sans rupture.
3. Validation du cumul exact et scellement dans la chaîne `period_closures`.

### 4.3 Duplicatas de Tickets et Rapports
1. Toute réimpression d'un ticket de caisse ou d'une clôture Z requiert l'action explicite d'un opérateur.
2. Le système incrémente le compteur de duplicata propre au document (`DuplicateNumber`).
3. Le document imprimé (image ESC/POS et texte) comporte obligatoirement la mention en tête :
   `DUPLICATA n°N` (gras, grande taille)
4. Un événement `DUPLICATE_PRINTED` est consigné dans le JET, même si aucune imprimante n'est disponible (`printQueued = false` dans le payload).

### 4.4 Archivage et Conservation (6 ans)
1. Les archives sont générées à partir des clôtures de période (`POST /api/fiscal/archives`).
2. Chaque archive est un fichier ZIP contenant :
   - `manifest.json` : métadonnées de l'archive, versions, date d'extraction, empreinte des fichiers internes.
   - `receipts.json` : ensemble des tickets fiscaux de la période avec leurs signatures.
   - `closures.json` : ensemble des clôtures journalières Z de la période.
   - `period-closures.json` : la clôture de période de référence.
   - `journal.json` : extrait du journal JET correspondant à la période.
3. Le fichier ZIP est scellé avec son empreinte SHA-256 et consigné dans la table `FiscalArchives` sous le répertoire configuré (`Archives:Path`, par défaut `archives/`).
4. Toute extraction/téléchargement trace un événement `ARCHIVE_EXPORTED` dans le JET.

---

## 5. Procédures de Contrôle & Audit

### 5.1 Vérification de l'Intégrité des Chaînes
Accessible depuis l'écran Fiscal Web et iPad :
1. Clic sur **« Vérifier les chaînes »** (`POST /api/fiscal/verify-chains`).
2. Le service `FiscalChainVerificationService` recharge l'intégralité des enregistrements et recalcule pas à pas chaque signature :
   - Chaîne des reçus par terminal (`receipts`).
   - Chaîne des clôtures journalières par terminal (`z_closures`).
   - Chaîne du journal technique JET (`jet`).
   - Chaîne des clôtures de période (`period_closures`).
   - Chaîne des archives (`archives`).
3. En cas d'anomalie, l'audit identifie précisément le document altéré, la séquence et la nature du problème :
   - `sequence_gap` : numéro de séquence manquant.
   - `previous_hash_mismatch` : liaison rompue avec le document précédent.
   - `signature_mismatch` : données du document altérées.

### 5.2 Vérification Externe d'une Archive ZIP
En cas de contrôle fiscal ou d'audit indépendant :
1. Téléverser le fichier d'archive ZIP sur l'endpoint `POST /api/fiscal/archives/verify`.
2. Le serveur calcule l'empreinte SHA-256 du fichier reçu, la compare à la base d'archives enregistrée et vérifie l'intégrité de la chaîne cryptographique des archives.
3. Statuts possibles :
   - `isValid: true` : archive intègre et authentique.
   - `hash_mismatch` : le fichier ZIP a été corrompu ou altéré depuis sa création.
   - `unknown_archive` : archive non reconnue par le système.
   - `chain_break` : rupture dans la chronologie des archives.

### 5.3 Export FEC (Fichier des Écritures Comptables)
- Conforme aux prescriptions de l'article A.47 A-1 du Livre des Procédures Fiscales.
- Délimité par barres verticales (`|`), sans séparateur de milliers, virgule pour décimales.
- Équilibré au centime entre Débit et Crédit.
- Nom normalisé : `<SIREN>FEC<YYYYMMDD>.txt`.
- Validable par l'outil officiel de la DGFiP **« Test Compta Demat »**.
