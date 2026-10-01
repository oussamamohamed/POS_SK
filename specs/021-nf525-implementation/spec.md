# Spécification : NF525 — compléter la conformité

**Branche** : `021-nf525-implementation` (à créer)

**Créée** : 2026-09-30 — réécrite le même jour après revue

**Statut** : Brouillon

**Demande** : « je veux implémenter la spec nf525 dans mon système d'encaissement et l'enrichir avec les fonctionnalités qui manquent »

## Contexte : ce qui existe déjà (à ne pas refaire)

Le socle NF525 est en place. Cette spec ne couvre que les manques.

| Exigence | Existant |
|---|---|
| Reçus chaînés SHA-256 | `FiscalReceipt` (`PreviousSignatureHash`, `SignatureHash`), départ `GenesisHash`, un chaînage par terminal |
| Clôture journalière (Z) chaînée | `DailyFiscalClosure`, grand total perpétuel `PerpetualGrandTotalCents` |
| Rapport X | `GET /api/fiscal/x-report` (+ impression) |
| Export FEC | `GET /api/fiscal/fec` (`IFecExportService`) |
| Journal des événements (JET) | `TransactionJournalEntry` (UUIDv7) — **non chaîné, peu alimenté** (voir US2) |
| Vérification de chaîne | `ValidateAuditChainIntegrityAsync` — **reçus seulement, appelée nulle part** (voir US1) |
| Annulation | par avoir chaîné, depuis l'appareil émetteur ; refusée après Z |
| Hors ligne | l'iPad envoie via `/api/sync/receipt` ; **le serveur est le seul signataire** |

## Invariants (non négociables)

- **INV-1** : la formule du hash des reçus ne change pas : `SHA256(previousHash|terminalId|sequenceNumber|amountCents|timestampUtc:O|taxBreakdownJson)`. Toute nouvelle chaîne (JET, clôtures mensuelles/annuelles, archives) a sa propre formule, documentée, sans toucher aux chaînes existantes.
- **INV-2** : le serveur reste le seul signataire. Aucun client (web, iPad) ne calcule de signature fiscale.
- **INV-3** : les données fiscales (reçus, clôtures, JET, archives) sont en ajout seul : ni suppression, ni modification des champs signés.
- **INV-4** : les montants restent en centimes entiers ; le grand total perpétuel ne repart jamais à zéro (seuls les totaux de période repartent de zéro).

## Clarifications

### Session 2026-09-30
- Q: Faut-il supprimer le drapeau `IsVoid` (qui modifie le reçu) pour le déduire de l'existence d'un avoir, ou le garder comme exception non signée ? → A: Déduire l'annulation dynamiquement depuis un avoir lié (sans modifier le reçu d'origine).
- Q: L'exercice comptable pour les clôtures annuelles est-il toujours l'année civile, ou sa date de début doit-elle être paramétrable ? → A: Paramétrable (date de début d'exercice définissable par établissement).
- Q: L'objectif final est-il d'obtenir une certification NF525 officielle par un organisme accrédité ? → A: Oui, objectif de certification officielle (type INFOCERT) avec numéro de certificat obligatoire sur les tickets.
- Q: Quelle est la règle de conservation des archives fiscales et le périmètre des événements JET ? → A: Conservation 6 ans minimum (standard BOI-TVA), événements JET définis dans l'US2 suffisants.

## Scénarios utilisateur et tests *(obligatoire)*

### US1 — Vérifier l'intégrité des données fiscales (Priorité : P1)

En tant que gérant ou contrôleur, je lance depuis l'écran fiscal une vérification qui contrôle toutes les chaînes (reçus de chaque terminal, clôtures, JET) et me dit si elles sont intactes ou où elles cassent.

**Pourquoi cette priorité** : la vérification existe dans le code mais n'est accessible à personne ; c'est la preuve d'inaltérabilité exigée en contrôle.

**Test indépendant** : modifier un montant en base, lancer la vérification, constater l'alerte qui désigne l'enregistrement.

**Scénarios d'acceptation** :

1. **Étant donné** des chaînes intactes, **quand** je lance la vérification, **alors** le résultat est « intègre » avec, par chaîne, le nombre d'enregistrements contrôlés.
2. **Étant donné** un reçu dont un champ signé a été modifié, **quand** je lance la vérification, **alors** le résultat désigne le terminal, le numéro de séquence et le type de rupture (hash précédent ou signature).
3. **Étant donné** une rupture détectée, **quand** la vérification se termine, **alors** un événement est écrit dans le JET.

### US2 — Journal des événements (JET) chaîné et complet (Priorité : P1)

En tant que contrôleur, je dispose d'un journal chaîné des événements techniques et fiscaux du système, qui ne peut pas être modifié sans que la vérification le détecte.

**Pourquoi cette priorité** : aujourd'hui `EntryHash` est un identifiant aléatoire (`"JET_VOID_" + Guid`), pas un hash ; le journal n'est ni chaîné ni exhaustif.

**Test indépendant** : effectuer une connexion, une clôture Z et une modification de taux de TVA ; vérifier que chaque action produit une entrée chaînée à la précédente.

**Scénarios d'acceptation** :

1. **Étant donné** une nouvelle entrée JET, **quand** elle est enregistrée, **alors** elle porte un numéro de séquence et un hash chaîné à l'entrée précédente.
2. **Étant donné** chacun des événements suivants, **quand** il survient, **alors** une entrée JET est créée : démarrage et arrêt du serveur, connexion et échec de connexion, clôture (journalière, mensuelle, annuelle), annulation de ticket, réimpression (duplicata), export FEC, archivage, modification d'un paramètre fiscal (taux de TVA, identité de l'établissement), appairage et révocation d'un appareil, rupture de chaîne détectée.
3. **Étant donné** les entrées existantes non chaînées, **quand** la nouvelle chaîne démarre, **alors** elles restent lisibles et la chaîne commence après elles (pas de réécriture).

### US3 — Annulation sans modifier le reçu d'origine (Priorité : P1)

En tant que contrôleur, je constate qu'aucun reçu enregistré n'est jamais modifié : l'annulation se lit dans l'avoir, pas dans une mise à jour du reçu annulé.

**Pourquoi cette priorité** : aujourd'hui l'annulation fait `original.IsVoid = true` (`CheckoutPaymentService.cs:245`), une mise à jour d'un enregistrement fiscal, contraire à INV-3.

**Test indépendant** : annuler un reçu ; vérifier que la ligne du reçu d'origine est identique avant et après, et que le statut « annulé » est toujours affiché partout où il l'était.

**Scénarios d'acceptation** :

1. **Étant donné** un reçu annulé, **quand** je relis le reçu d'origine, **alors** aucun de ses champs n'a changé. L'état d'annulation est déduit dynamiquement de l'existence d'un avoir lié, sans modification du reçu d'origine.
2. **Étant donné** un reçu annulé, **quand** je consulte le rapport X, la Z ou le rapport d'annulations, **alors** les totaux sont identiques à ceux d'aujourd'hui.

### US4 — Clôtures mensuelle et annuelle (Priorité : P2)

En tant que gérant, je clôture le mois et l'exercice ; chaque clôture cumule les clôtures journalières de la période, est signée et chaînée.

**Pourquoi cette priorité** : NF525 exige des clôtures journalières, mensuelles et annuelles ; seule la journalière existe.

**Test indépendant** : faire trois Z sur un mois, clôturer le mois, vérifier que les totaux égalent la somme des trois Z.

**Scénarios d'acceptation** :

1. **Étant donné** les Z d'un mois, **quand** je clôture le mois, **alors** une clôture mensuelle est créée avec totaux TTC/HT, ventilation TVA, totaux par moyen de paiement, grand total perpétuel, signature chaînée à la clôture mensuelle précédente.
2. **Étant donné** une journée du mois sans Z, **quand** je clôture le mois, **alors** la clôture est refusée avec la liste des journées à clôturer.
3. **Étant donné** les clôtures mensuelles d'un exercice, **quand** je clôture l'exercice, **alors** une clôture annuelle est créée sur le même modèle. La date de début d'exercice est paramétrable par établissement.
4. **Étant donné** une période clôturée (mois ou exercice), **quand** je tente de la clôturer à nouveau, **alors** c'est refusé.

### US5 — Archivage signé et conservation (Priorité : P2)

En tant que gérant, je produis une archive signée d'une période clôturée (au moins l'exercice), que je peux vérifier plus tard et remettre à un contrôleur.

**Pourquoi cette priorité** : la condition « archivage » de NF525 n'est pas couverte ; les données doivent être conservées 6 ans.

**Test indépendant** : archiver un exercice, modifier un octet de l'archive, constater que la vérification échoue.

**Scénarios d'acceptation** :

1. **Étant donné** une période clôturée, **quand** je l'archive, **alors** un fichier contenant reçus, avoirs, clôtures et JET de la période est produit, avec une signature et son empreinte enregistrée côté serveur et dans le JET.
2. **Étant donné** une archive, **quand** je la vérifie, **alors** le système indique si elle est intacte.
3. **Étant donné** des données fiscales de moins de 6 ans, **quand** une purge ou une suppression est tentée, **alors** elle est refusée.

### US6 — Duplicatas (Priorité : P2)

En tant que contrôleur, je distingue un original d'une réimpression : toute réimpression d'un ticket ou d'une clôture porte la mention « DUPLICATA » et un numéro, et elle est journalisée.

**Pourquoi cette priorité** : seule la réimpression de la dernière clôture et le `retry` d’un job d’impression existent, sans aucune marque de duplicata ; il n’existe pas de réimpression de ticket.

**Test indépendant** : réimprimer deux fois le même ticket ; vérifier « DUPLICATA n°1 » puis « n°2 » et deux entrées JET.

**Scénarios d'acceptation** :

1. **Étant donné** un ticket ou une clôture déjà imprimé, **quand** je le réimprime, **alors** le document porte « DUPLICATA » et un numéro de duplicata croissant par document.
2. **Étant donné** une réimpression, **quand** elle est demandée, **alors** une entrée JET est créée (document, numéro de duplicata, opérateur).

### US7 — Mentions obligatoires paramétrables (Priorité : P3)

En tant que gérant, je renseigne l'identité de mon établissement ; les tickets l'impriment avec la version du logiciel.

**Pourquoi cette priorité** : l'en-tête des tickets est codé en dur avec une identité de démonstration (`TicketDocumentBuilder.cs:17` : raison sociale, adresse, SIRET, TVA intracommunautaire).

**Test indépendant** : modifier la raison sociale dans la gestion, imprimer un ticket, lire la nouvelle raison sociale.

**Scénarios d'acceptation** :

1. **Étant donné** l'identité saisie (raison sociale, adresse, SIRET, n° TVA), **quand** un ticket ou un rapport est imprimé, **alors** elle y figure.
2. **Étant donné** un ticket, **quand** il est imprimé, **alors** il porte la version du logiciel et, dès qu'il est renseigné, le numéro de certificat officiel (INFOCERT). Tant que la certification n'est pas obtenue, le numéro est vide et l'écran fiscal affiche « certification en cours » ; une fois renseigné, il est imprimé sur chaque ticket et rapport.
3. **Étant donné** une modification de l'identité, **quand** elle est enregistrée, **alors** une entrée JET est créée.

### Cas limites

- Vérification lancée pendant un encaissement : elle porte sur un instantané cohérent et ne bloque pas la caisse.
- Chaîne JET ou clôture vide (premier démarrage) : la vérification répond « intègre, 0 enregistrement ».
- Clôture mensuelle demandée alors qu'une Z du mois est refusée pour commandes en cours : la clôture mensuelle est refusée aussi.
- Duplicata d'un reçu annulé : autorisé, marqué duplicata ; l'avoir reste visible.
- Changement d'heure ou fuseau : les périodes (jour, mois, exercice) se calculent en heure locale du serveur, les horodatages restent en UTC.

## Exigences *(obligatoire)*

### Exigences fonctionnelles

- **FR-001** : Le système DOIT permettre à un gérant ou administrateur de vérifier les chaînes des reçus (par terminal), des clôtures et du JET, et d'en afficher le résultat détaillé.
- **FR-002** : Le système DOIT chaîner chaque entrée JET (séquence + hash de l'entrée précédente) et journaliser les événements listés en US2.
- **FR-003** : Le système NE DOIT modifier aucun champ d'un reçu enregistré lors d'une annulation.
- **FR-004** : Le système DOIT fournir des clôtures mensuelles et annuelles cumulatives, signées et chaînées, refusées si une journée de la période n'est pas clôturée.
- **FR-005** : Le système DOIT produire et vérifier des archives signées de périodes clôturées, et refuser toute suppression de données fiscales de moins de 6 ans.
- **FR-006** : Le système DOIT marquer et numéroter chaque réimpression comme duplicata et la journaliser.
- **FR-007** : Le système DOIT imprimer l'identité de l'établissement saisie dans la gestion et la version du logiciel sur les tickets et rapports.
- **FR-008** : Chaque nouvelle fonction DOIT exister sur le client web et sur l'iPad (parité), avec chaînes en/fr/ar.

### Entités clés

- **Entrée JET** (extension de `TransactionJournalEntry`) : séquence, type d'événement, horodatage UTC, opérateur, terminal, données, hash précédent, hash.
- **Clôture de période** : type (mois, exercice), bornes, totaux TTC/HT, ventilation TVA, totaux par moyen de paiement, grand total perpétuel, hash précédent, signature, auteur.
- **Archive** : période, date, empreinte, signature, auteur.
- **Duplicata** : document d'origine (reçu ou clôture), numéro, date, opérateur.
- **Identité de l'établissement** : raison sociale, adresse, SIRET, n° TVA, n° de certificat (facultatif).

## Critères de succès *(obligatoire)*

- **SC-001** : Une modification directe en base d'un reçu, d'une clôture ou d'une entrée JET est détectée par la vérification dans 100 % des cas de test.
- **SC-002** : La vérification complète d'une année de données (≈ 100 000 reçus) se termine en moins de 30 secondes.
- **SC-003** : La somme des Z d'un mois égale la clôture mensuelle au centime ; idem pour les mois et l'exercice.
- **SC-004** : Une archive altérée d'un seul octet est rejetée à la vérification.
- **SC-005** : Toute réimpression est identifiable comme duplicata sur papier et dans le JET.
- **SC-006** : L'export FEC existant passe l'outil de la DGFiP « Test Compta Demat » sur un mois de données de test (contrôle de l'existant, sans nouvelle fonction).

## Hors périmètre

- Nouveau calcul de signature côté iPad ou mode hors ligne signant localement (INV-2).
- Changement de base de données, de framework ou de style d'API.
- Refonte de l'export FEC (déjà présent ; seul SC-006 le contrôle).

## Hypothèses

- Les rôles gérant et administrateur existants suffisent pour la vérification, les clôtures de période et l'archivage.
- La conservation se fait sur le serveur du restaurant ; la sauvegarde externe reste à la charge de l'exploitant.
- Les références réglementaires sont confirmées : durée de conservation de 6 ans minimum (BOI-TVA) et les événements listés dans l'US2 sont suffisants.
