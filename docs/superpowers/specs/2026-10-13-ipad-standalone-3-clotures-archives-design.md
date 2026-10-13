# iPad autonome, sous-projet 3 : clôtures de période, archives, FEC

Suite de `2026-10-06-ipad-standalone-design.md` (ordre de construction, ligne 3) et de `2026-10-10-ipad-standalone-2-fiscal-design.md`.

## Objectif

Le mode autonome de l'iPad produit les mêmes pièces fiscales que le serveur .NET : clôtures mensuelles et annuelles, archives fiscales signées, export FEC, et l'iPad les vérifie et les partage. Critère de sortie (spec 2026-10-06) : totaux et signatures identiques au .NET.

Acquis avant ce sous-projet : reçus chaînés, JET, annulations, duplicata, rapport X, Z journalière (table V7, `DailyFiscalClosures`), sauvegarde chiffrée, section Fiscal visible pour un responsable, et le rejeu de la journée simulée (`LocalDaySimulationTests`).

## Hors périmètre

- Impression des clôtures, archives et duplicata (sous-projet 4).
- Écran de restauration de la base et écran d'annulation de reçu (sous-projet 5 : Back-office local).
- Attestation NF525 du mode autonome : démarche à mener, pas du code.
- Vérification d'une archive iPad par un serveur .NET : l'iPad signe dans sa propre chaîne.

## Décisions

- **Un seul poste : `T01`.** Pas de `POS_MAIN_TERM` ni de périmètre « tous les terminaux ».
- **Fuseau horaire.** Le .NET utilise `TimeZoneInfo.Local`. Le Swift utilise le `calendar` injecté dans `LocalPosAPI`. Les fixtures .NET fixent le fuseau (Europe/Paris) et publient les bornes UTC attendues, pour que les tests Swift ne dépendent pas du fuseau de la machine.
- **ZIP « stocké »** (sans compression), écrit à la main avec CRC32 : pas de dépendance, format lisible par tout outil. Ni octets ni SHA-256 identiques au .NET (horodatages et identifiants différents) : la parité porte sur les signatures de clôture, la forme du manifeste et la chaîne d'archives.
- **Contenu des cinq JSON de l'archive** : mêmes noms de propriétés que les entités .NET (`System.Text.Json`, PascalCase, indenté), pour qu'un auditeur lise les deux pareil.
- **FEC.** Le .NET ne filtre pas par poste (tous les reçus de la période). La référence est donc un scénario .NET à un seul poste ; le FEC Swift doit y être identique octet pour octet.
- **Migrations** : V8 (`FiscalPeriodClosures`) puis V9 (`FiscalArchives`), jamais de modification d'un script publié. Mêmes tables et colonnes que `AppDbContext`, avec triggers d'immuabilité comme V5/V7.

## Découpage en lots

Un lot = une branche, un fichier de suivi `docs/plans/ipad-standalone-3<x>-progress.md`, une revue. Ordre imposé.

### 3a. Clôtures de période

- Migration V8 : `FiscalPeriodClosures` (colonnes de `FiscalPeriodClosure`), index uniques `(TerminalId, PeriodType, PeriodKey)` et `(TerminalId, PeriodType, ClosureSequence)`, triggers `NoUpdate` / `NoDelete`.
- `LocalFiscalRepository` : `executePeriodClosure(type, key, manager, now)` et `periodClosures(type)`. Algorithme du .NET (`ExecutePeriodClosureAsync`) :
  - Mensuelle : bornes du mois dans le fuseau local, totaux = somme des Z journalières dont la période **commence** dans le mois.
  - Annuelle : bornes de l'exercice (`FiscalYearStartMonth/Day` des réglages). Si l'exercice commence le 1er, totaux = somme des 12 mensuelles ; sinon, somme des Z journalières.
  - Erreurs : `period_not_ended`, `period_already_closed`, `missing_daily_closures` (jours listés), `missing_monthly_closures` (mois listés).
  - Signature : `ComputePeriodClosureHash` portée dans `FiscalHashing`. Chaîne par `(poste, type)` depuis `GenesisHash`. Entrée JET `PERIOD_CLOSURE`.
- API : `LocalPosAPI.periodClosure(type:key:)` et `periodClosures(type:)`, réservées aux responsables, erreurs au même format que le serveur (code + message).
- **Preuve** : un test .NET génère `period_closure_reference.json` (Z journalières, puis mensuelle et annuelle, avec signatures) ; les tests Swift rejouent les Z puis comparent chaque clôture de période (totaux, JSON des taxes et des règlements, perpétuel, nombre de Z, signature).

### 3b. Archives fiscales

- Migration V9 : `FiscalArchives`, index uniques sur `PeriodClosureId` et `ArchiveSequence`, triggers d'immuabilité.
- `ZipStoreWriter` (CRC32, en-têtes locaux et répertoire central, sans compression) et lecture du manifeste pour la vérification.
- `createArchive(periodClosureId)` : sélection identique au .NET (reçus du poste dans `[début ; fin]`, Z journalières, clôtures de période incluant les mensuelles pour une annuelle, JET dans la période), cinq fichiers JSON, `manifest.json` avec les SHA-256 de chaque fichier, nom `archive-{poste}-{M|A}-{clé}.zip`, fichier écrit dans un répertoire d'archives de l'app. Signature `ComputeArchiveHash`, chaîne globale, JET `ARCHIVE_CREATED`. Une seule archive par clôture (`archive_exists`).
- `archiveFile(id)` et `verifyArchive(data, fileName)` : mêmes résultats que le .NET (`unknown_archive`, `hash_mismatch`, `chain_break`, ou valide).
- **Preuve** : la signature d'archive calculée en Swift égale le .NET pour les mêmes entrées (vecteurs générés par le .NET) ; le ZIP écrit est relu par un lecteur ZIP indépendant (`/usr/bin/unzip -t` dans un test) et son manifeste annonce les bons hashs ; falsifier un octet donne `hash_mismatch`.

### 3c. FEC

- Port de `FecExportService` en `LocalFecExporter` : même découpage en écritures, mêmes comptes (dont 468600 pour l'excédent de règlement), même répartition de la TVA et du HT, nom de fichier et encodage identiques. JET `FEC_EXPORTED`.
- **Preuve** : un test .NET à un seul poste écrit `fec_reference.json` (reçus du scénario et FEC attendu) ; le FEC Swift est identique octet pour octet.

### 3d. Vérification et interface

- `verifyAllChains` couvre en plus `FiscalPeriodClosures` (par poste et type) et `FiscalArchives` (globale) ; mêmes codes d'échec que le .NET.
- Section Fiscal en mode autonome : liste et création des clôtures mensuelle et annuelle (avec les erreurs métier lisibles), liste et création des archives, export FEC (dates, SIREN, société), `ShareLink` pour l'archive et le FEC. Chaînes de l'interface en FR / EN / AR comme le reste de l'app.
- **Preuve** : tests PosKit pour `verifyAllChains` (falsification SQL contournant un trigger sur une copie de base) et XCUITest en `-UITestLocal` : créer une clôture, créer une archive, exporter le FEC, voir la vérification verte.

## Risques

- Écart de fuseau et de bornes de mois : couvert par des fixtures aux bornes (changement d'heure d'octobre et de mars, 1er jour du mois).
- Parité JSON de l'archive : seule la forme est comparée, pas les octets. Les écarts éventuels sont consignés dans `CLAUDE.md`.
- Taille des gros exports en mémoire : acceptable pour un restaurant (quelques milliers de reçus par mois) ; la limite est notée dans le code (`ponytail:`).

## Règle de suivi .NET / Swift

Toute modification de `ExecutePeriodClosureAsync`, `FiscalArchiveService`, `FecExportService` ou de `FiscalHashing` impose de régénérer les fixtures (`period_closure_reference.json`, `fec_reference.json`) et de reporter le changement dans `Local/`. À ajouter à `CLAUDE.md` à la fin du lot 3d.
