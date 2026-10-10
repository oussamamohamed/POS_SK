# iPad standalone, sous-projet 2 : fiscal NF525 et sauvegarde chiffrée

Date : 2026-10-10 · Statut : implémentée (plan 2026-10-10-ipad-standalone-2-fiscal-nf525)
Spec parente : `2026-10-06-ipad-standalone-design.md` (sous-projet 2 de l'ordre de construction).

## Objectif

Le mode autonome de l'iPad émet de vrais reçus fiscaux NF525 sur le terminal `T01`, avec les mêmes signatures que le backend .NET, et protège la chaîne par une sauvegarde chiffrée. À la fin du sous-projet, le bandeau « non fiscal » disparaît.

## Périmètre

Inclus :
1. Remplacement de `NonFiscalPayments` par `FiscalReceipts` et `PaymentTenders` signés, chaînés pour `T01`.
2. Annulation par contre-passation, duplicata, journal technique (JET), triggers d'immuabilité.
3. Clôture d'une commande à solde nul et `chargeRoom` avec reçu fiscal valide (conditions 6 et 7 du plan 1c). Le .NET n'écrit pas de reçu pour une facturation chambre ; l'iPad en écrit un (règlement « chambre ») : écart assumé.
- **Parité des montants** : TVA et HT du reçu reproduisent `CheckoutPaymentService`, y compris ses écarts actuels (JSON de TVA sans taux à emporter ni remise globale, HT sans remise en montant fixe) ; les corriger est une décision séparée des deux côtés.
4. Audit JET des dérogations Happy Hour et des annulations de mise en attente.
5. Règle de signature des décimaux : on signe toujours la valeur recalculée depuis les centimes, jamais le texte relu de la base (« 20 » côté Swift, « 20.0 » côté .NET).
6. Sauvegarde chiffrée : export manuel, sauvegarde quotidienne automatique, restauration (fonction et tests).

Hors périmètre (sous-projet 3 ou suivants) :
- Clôtures Z, clôtures de période, archive, FEC, section Fiscal visible dans l'interface.
- Écran complet de réglages de la base (emplacement, version de schéma). Seuls la phrase secrète, le dossier de sauvegarde et l'état de la dernière sauvegarde sont ajoutés.
- Impression (sous-projet 4) et back-office local (sous-projet 5).
- Basculement de mode sans réinstallation.

## Constat sur le backend .NET

- Les signatures sont du SHA-256 simple, sans clé. La clé Keychain de la spec parente ne signe donc rien. Elle ne sert qu'à la sauvegarde (phrase secrète, voir plus bas).
- Formule des reçus : `SHA256(previousHash|terminalId|sequenceNumber|amountCents|timestampUtc:O|taxBreakdownJson)`, hexadécimal **majuscule**. JET : `SHA256(previousHash|chainSequence|eventType|occurredAtUtc:O|terminalId|operatorId|payloadJson)`, hexadécimal **minuscule** (`FiscalHashing.cs`).
- Piège de parité : `{timestampUtc:O}` sur un `DateTimeOffset` produit `yyyy-MM-ddTHH:mm:ss.fffffffzzz` avec `+00:00`, et non `Z` comme l'écrit `docs/fiscal.md`. Le format réel est fixé par les fixtures .NET, pas par la documentation. Idem pour l'ordre des clés et les décimales du JSON de TVA.

## Architecture (`ios/Packages/PosKit/Sources/PosKit/Local/`)

| Unité | Rôle | Dépend de |
|---|---|---|
| `FiscalHashing` | SHA-256 (CryptoKit), casse hex par chaîne, formatage de l'horodatage et JSON canonique de TVA identiques au .NET | rien |
| `LocalFiscalRepository` | Écrit en **une transaction SQLite** le reçu, ses `PaymentTenders` et l'entrée JET. Lit la fin de chaîne `T01` (dernier hash, `MAX(SequenceNumber)`) dans la même transaction | `SQLiteDatabase`, `FiscalHashing` |
| `LocalPosAPI+Payments`, `+CounterCheckout`, clôture de commande | Appellent `LocalFiscalRepository` à la place de `LocalPaymentRepository` | `LocalFiscalRepository` |
| `LocalBackupCrypto` | Dérivation de clé et AES-GCM, format de fichier | CryptoKit |
| `LocalBackupService` | Copie cohérente, chiffrement, rotation, état de la dernière sauvegarde, restauration | `LocalBackupCrypto`, `backup(to:)` existant |

`LocalPaymentRepository` est supprimé avec `NonFiscalPayments`. Le terminal reste toujours `T01`, comme le serveur qui ignore le terminal envoyé par le client.

## Données : migration V5 (nouveau script, jamais de modification d'un script publié)

- Création de `FiscalReceipts`, `PaymentTenders` et `JournalEntries` (nom du `DbSet` du JET) avec les mêmes colonnes que `AppDbContext`, y compris `VoidedReceiptId`. Il n'y a pas de colonne `DuplicateNumber` sur les reçus : comme le .NET, le numéro de duplicata se déduit du JET (nombre d'entrées `DUPLICATE_PRINTED` du document + 1). `IsVoid` est conservée pour la parité du schéma et jamais écrite.
- Triggers `BEFORE UPDATE` et `BEFORE DELETE` sur ces trois tables : `RAISE(ABORT, 'NF525 INV-3: ...')`, mêmes messages que `FiscalImmutabilityInterceptor`. Les entrées JET héritées sans `ChainSequence` ne sont pas concernées côté Swift (aucune n'existe).
- Suppression de `NonFiscalPayments` (décision de l'utilisateur : aucune base réelle à préserver, les paiements provisoires sont perdus). Les chaînes démarrent à `GenesisHash`.
- `LocalCounters` et les tables de verrouillage PIN sont inchangés.
- Les tables des clôtures Z, de période et des archives sont créées au sous-projet 3.

## Règles métier

- **Encaissement** : un reçu par paiement soldant ou encaissant, `sequenceNumber` continu à partir de 1, montants TTC et HT en centimes entiers (arrondi identique à `Money.FromDecimal`), tenders par moyen de paiement (espèces avec rendu monnaie, carte, ticket restaurant, carte cadeau, chambre).
- **Annulation** : nouveau reçu signé, montants TTC et bases de TVA négatifs, lié par `VoidedReceiptId`, plus entrée JET `RECEIPT_VOIDED`. Refusée si le reçu n'est pas de `T01`, s'il est lui-même une annulation, s'il est déjà annulé, ou s'il porte un règlement « chambre » (409 : le folio de la chambre ne serait pas contre-passé ; la contre-passation du folio est une fonctionnalité future).
- **Duplicata** : numérote le duplicata d'après le JET, impose la mention « DUPLICATA n°N » et crée une entrée JET `DUPLICATE_PRINTED` avec `printQueued` dans le payload, même sans imprimante.
- **Solde nul et chambre** : commande 100 % offerte ou remisée, et `chargeRoom` : un reçu fiscal est émis, sans numéro `NF-…` incohérent (conditions 6 et 7 du plan 1c).
- **JET** : en plus de `RECEIPT_VOIDED` et `DUPLICATE_PRINTED`, les dérogations Happy Hour et les annulations de mise en attente sont journalisées. Les événements de sauvegarde sont aussi journalisés (réussite et échec).
- **Décimaux** : toute valeur signée est formatée depuis les centimes. Aucune relecture de texte décimal de la base n'entre dans un hash.

## Sauvegarde chiffrée

- **Format** : en-tête (magic, version, sel, itérations), puis le contenu de la base chiffré en AES-GCM. Clé dérivée de la phrase secrète par PBKDF2-SHA256 avec sel aléatoire. Une mauvaise phrase ou un fichier altéré lève une erreur explicite, jamais une base corrompue.
- **Écriture** : copie cohérente de la base (`backup(to:)`), chiffrement vers un fichier temporaire, puis renommage. Un disque plein ne laisse aucun fichier partiel qui ressemble à une sauvegarde valide. Protection `NSFileProtectionComplete` sur la copie.
- **Phrase secrète** : saisie une fois dans Réglages avec confirmation et avertissement « phrase perdue = sauvegarde illisible ». Mémorisée dans le Keychain (`ThisDeviceOnly`) pour la sauvegarde automatique. L'export manuel la redemande pour qu'une restauration sur un autre iPad reste possible.
- **Sauvegarde quotidienne** : au passage au premier plan, si la dernière date de plus de 24 h, écriture d'un fichier daté dans le dossier choisi (Fichiers ou iCloud Drive, signet sécurisé). Les 14 dernières sont conservées. Un échec n'empêche jamais d'encaisser : bannière « sauvegarde en retard » et entrée JET.
- **Restauration** : fonction `restore` écrite et testée, sans écran dédié. Elle ne remplace la base qu'après vérification des chaînes.
- La clé de signature n'existe pas (voir constat) : rien de tel n'est exporté. Les PIN restent hachés dans la base, qui est de toute façon chiffrée à l'export.

## Interface

- Le bandeau « non fiscal » est remplacé par un bandeau de rappel de sauvegarde (jamais sauvegardé, ou plus de 24 h).
- La section Fiscal reste masquée en mode autonome jusqu'au sous-projet 3.
- Réglages : phrase de sauvegarde, dossier, bouton d'export manuel, état de la dernière sauvegarde, bannière de retard.
- Toutes les chaînes d'interface sont ajoutées en FR, EN et AR dans `Localizable.xcstrings`.

## Tests

1. **Parité .NET (condition de sortie)** : un générateur .NET dans `tests/` produit `fiscal_reference.json` (vente simple, multi-TVA, paiement mixte, annulation, duplicata, entrées JET) avec horodatages fixés. Le test Swift rejoue la séquence sur `T01` et compare chaque hash, séquence et JSON de TVA. Le fichier est stocké dans `Tests/PosKitTests/Fixtures`.
2. **Immuabilité** : `UPDATE` et `DELETE` sur chaque table fiscale rejetés avec le message attendu.
3. **Atomicité** : un échec simulé à l'écriture du JET annule le reçu et ses tenders.
4. **Concurrence** : deux encaissements simultanés produisent des séquences consécutives sans trou.
5. **Cas limites** : solde nul, `chargeRoom`, annulation depuis un autre terminal, double annulation, annulation d'une annulation, duplicata sans imprimante.
6. **Migration V5** : une base V4 avec lignes `NonFiscalPayments` migre, la table disparaît, les chaînes démarrent à `GenesisHash`.
7. **Sauvegarde** : aller-retour chiffré, mauvaise phrase, fichier altéré ou tronqué, rotation à 14, échec d'écriture sans fichier partiel.
8. **UI** (`-UITestLocal`) : encaissement d'une table sans bandeau « non fiscal », configuration de la phrase et état de la dernière sauvegarde.
9. **Suivi** : `CLAUDE.md` consigne que toute modification fiscale .NET impose de régénérer `fiscal_reference.json` et de reporter le changement dans `Local/` (nouvelle migration).

Base de référence avant la première tâche : PosKit 335, UI 47. Suivi dans `docs/plans/ipad-standalone-2-progress.md`.

## Risques

- **Format d'horodatage ou de JSON de TVA divergent** : détecté par le test de parité. Si le .NET change, régénérer les fixtures.
- **Attestation NF525** : doit couvrir le mode autonome. À vérifier avant tout usage réel, y compris la sauvegarde.
- **Phrase secrète perdue** : sauvegarde illisible, d'où l'avertissement explicite et l'absence de porte dérobée.
- **iPad perdu sans sauvegarde récente** : la chaîne est perdue. La bannière de retard et la sauvegarde quotidienne réduisent le risque sans l'éliminer.
- **Terminal unique** : un reçu ne peut être annulé que par `T01`. Les reçus d'un autre terminal ne sont pas importés (pas de synchronisation, hors périmètre).
