# Feature Specification: Rappel & Restauration du Contenu de Table (Table Order Recall & Cart Hydration)

**Feature Branch**: `008-table-order-recall`  
**Created**: 2026-08-30  
**Status**: Ready for Planning  
**Input**: User description: "quand je rapelle une table le contenue ne s'affiche pas"  

---

## Executive Summary

Dans un environnement de restauration active, les serveurs et caissiers naviguent continuellement entre les tables pour prendre des commandes échelonnées (apéritifs, puis entrées, plats, desserts, cafés) ou pour procéder à l'addition et à l'encaissement. 

Cette spécification définit le mécanisme de **rappel instantané et de restauration intégrale du contenu d'une table (Order Recall & Cart Hydration)** : dès qu'un opérateur sélectionne une table occupée depuis le plan de salle 2D, la liste des tables ou la saisie directe du numéro de table, le terminal de vente doit recharger immédiatement l'ensemble des articles commandés, leurs quantités, modificateurs (cuissons, sauces), sous-totaux, statuts d'envoi en cuisine, ainsi que le serveur responsable et le nombre de convives.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Rappel Automatique et Affichage des Articles d'une Table Occupée (Priority: P1) 🎯 MVP

En tant que serveur ou caissier, lorsque je clique sur une table occupée dans le plan de salle ou que je saisis son numéro, je veux que la totalité des articles déjà commandés sur cette table s'affiche instantanément dans le panier de caisse avec leurs prix et totaux exacts, afin de pouvoir consulter la note, ajouter de nouveaux articles ou procéder à l'encaissement.

**Why this priority**: C'est le cœur du problème rapporté par l'utilisateur. Sans affichage du contenu lors du rappel de table, le serveur ne peut ni consulter la note en cours, ni envoyer la suite de commande, ni encaisser les bons montants.

**Independent Test**:
1. Ouvrir la table `T2` avec 3 couverts.
2. Ajouter 2 `Salade César Poulet` et 1 `Burger Gourmet Rossini`.
3. Cliquer sur "Envoyer Cuisine" (ou sauvegarder la commande).
4. Basculer vers le plan de salle et sélectionner la table `T1` (vide) puis revenir sur `T2`.
5. **Résultat attendu** : Le panier de vente affiche immédiatement les 2 Salades et le Burger, le total exact (38.50 €), la ventilation de TVA et le badge Table T2.

**Acceptance Scenarios**:
1. **Given** une table `T2` ayant une commande active avec 3 articles, **When** l'opérateur clique sur la table `T2` dans le plan de salle, **Then** l'écran de vente se charge avec le badge `Table T2`, la liste des 3 articles avec leurs quantités et options, et le montant total calculé.
2. **Given** une table `T4` libre (sans commande active), **When** l'opérateur clique dessus, **Then** un formulaire rapide demande le nombre de convives et ouvre un panier neuf prêt à la saisie.
3. **Given** une table occupée, **When** elle est rappelée, **Then** les articles déjà envoyés en cuisine sont visuellement distincts (badge "En Cuisine" ou statut verrouillé) des nouveaux articles en cours de saisie non encore transmis.

---

### User Story 2 - Ajout Incrémental et Persistance Échelonnée (Priority: P1)

En tant que serveur, lorsque je rappelle une table ayant déjà des plats en cours, je veux pouvoir ajouter les desserts et cafés sans écraser ni dupliquer la commande existante, et envoyer uniquement les nouveaux articles en cuisine.

**Why this priority**: Le service en restaurant se déroule par séquences (boissons/entrées ➔ plats chauds ➔ desserts/cafés). La persistance doit agréger les lignes sans perte ni écrasement.

**Independent Test**:
1. Rappeler la table `T2` avec ses entrées/plats déjà enregistrés.
2. Ajouter 2 `Tiramisu Spéculos` et 2 `Expresso Pur Arabica`.
3. Cliquer sur "Envoyer Cuisine".
4. Re-rappeler la table `T2` : les 4 articles précédents et les 4 nouveaux sont réunis dans la commande active de la table.
5. Sur l'écran KDS / Cuisine, seul le ticket des desserts/cafés est envoyé à la station Dessert/Bar.

**Acceptance Scenarios**:
1. **Given** une commande active sur `T2`, **When** l'opérateur ajoute de nouvelles lignes d'articles et valide, **Then** ces lignes sont enregistrées dans la commande de `T2` et persistées localement et sur le serveur central.
2. **Given** une table rappelée, **When** l'opérateur supprime un article non encore envoyé en cuisine, **Then** la ligne est retirée sans impacter les lignes déjà en préparation.

---

### User Story 3 - Transfert de Table et Fusion avec Conservation du Contenu (Priority: P2)

En tant que manager ou serveur, lorsque des clients changent de table (ex: de la terrasse `T2` vers l'intérieur `T5`), je veux que la totalité des articles et de l'historique de commande soit transférée vers la nouvelle table lors de son rappel.

**Why this priority**: Les changements de placement sont fréquents en salle. Le rappel de la table cible doit refléter immédiatement le contenu transféré.

**Independent Test**:
1. Transférer la table `T2` (contenant 38.50 € de commande) vers `T5`.
2. Rappeler `T5` dans le plan de salle.
3. **Résultat attendu** : `T5` affiche les 38.50 € d'articles, `T2` redevient libre avec un panier vide.

**Acceptance Scenarios**:
1. **Given** `T2` occupée et `T5` libre, **When** un transfert est validé, **Then** `T5` prend le statut Occupé avec l'OrderId de `T2`, et `T2` redevient Libre.
2. **Given** `T5` après transfert, **When** l'opérateur clique sur `T5`, **Then** l'écran de vente charge fidèlement l'ensemble des articles transférés.

---

### User Story 4 - Règlements Partiels et Encaissement de Table Rappelée (Priority: P2)

En tant que caissier, lorsque je rappelle une table pour encaissement, je veux que le solde restant dû et le détail de chaque article soient immédiatement exploitables pour le paiement direct ou le Split Bill (partage de note).

**Why this priority**: L'encaissement est la finalité du cycle de commande ; il doit s'appuyer sur l'état exact restauré de la table.

**Independent Test**:
1. Rappeler une table avec 50.00 € de commande.
2. Cliquer sur "Encaisser". Le montant restant à payer affiche exactement 50.00 €.
3. Régler 20.00 € en espèces. Le solde restant passe à 30.00 €.
4. Rappeler la table : le solde et les paiements partiels enregistrés sont préservés.

---

### Edge Cases

- **Changement simultané de table / Conflit multi-terminaux :** Si un serveur sur iPad A ajoute un article sur `T2` et qu'un second serveur sur iPad B rappelle la même table `T2`, le système doit synchroniser et afficher l'état consolidé le plus récent.
- **Rappel d'une table avec commande clôturée/encaissée :** Si une table vient d'être payée en totalité, le rappel d'une nouvelle vente sur cette table réinitialise un nouveau panier vierge sans conserver les anciennes lignes archivées.
- **Perte de réseau / Mode Hors-Ligne (Offline-First) :** Le rappel de table et la restitution du contenu doivent fonctionner de manière 100% autonome depuis la base SQLite locale locale sans dépendance obligatoire à une connexion active au serveur.
- **Articles avec modificateurs complexes (cuissons, suppléments) :** La restauration du panier doit recharger non seulement l'article parent mais également l'intégralité de ses options sélectionnées (ex: "Cuisson : Saignant", "Sauce : Poivre Vert + 1.50 €").

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Le système DOIT associer chaque table occupée à un identifiant unique de commande active (`ActiveOrderId`).
- **FR-002**: Lors de la sélection/rappel d'une table (via plan de salle 2D, liste ou saisie numérique), le système DOIT interroger la commande active et charger immédiatement dans l'état de l'écran de caisse l'ensemble des lignes d'articles (`OrderLines`).
- **FR-003**: Chaque ligne d'article restaurée DOIT contenir son intitulé, son prix unitaire, sa quantité, son taux de TVA, ses modificateurs/instructions et son statut de production (`SentToKitchen`, `Pending`).
- **FR-004**: Le sous-total HT, le montant de chaque tranche de TVA (5.5%, 10%, 20%) et le total TTC DOIVENT être recalculés et affichés dès la fin de la restauration du panier.
- **FR-005**: L'envoi en cuisine d'une table rappelée DOIT expédier uniquement les lignes d'articles marquées comme nouvelles (`Pending`) et mettre à jour leur statut en `SentToKitchen`.
- **FR-006**: L'API REST et le service de persistance locale DOIVENT exposer un point d'accès dédié `/api/tables/{tableNumber}/order` renvoyant le détail complet de la commande active d'une table.
- **FR-007**: L'encaissement d'une table rappelée DOIT solder la commande active, libérer la table (`Status = Free`, `ActiveOrderId = null`) et vider le panier de vente.

### Key Entities

- **DiningTable**: Représente la table physique (`TableNumber`, `Capacity`, `Status`, `AssignedWaiterName`, `CoversCount`, `ActiveOrderId`).
- **Order / OrderLine**: Représente la commande en cours (`OrderId`, `TableNumber`, `OperatorId`, `Status`, `Lines: [ProductId, Quantity, UnitPrice, TaxRatePercent, Modifiers, IsDispatched]`, `TotalTtc`).
- **CartItemState**: État en mémoire du panier réactif lié à la table active pour l'interface utilisateur.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Le rappel d'une table et le rechargement intégral de son panier d'articles s'exécutent en **moins de 100 millisecondes** sur tablette et interface web.
- **SC-002**: **100% des articles**, quantités et modificateurs saisis sur une table sont fidèlement restaurés lors des rappels successifs sans perte ni décalage de prix.
- **SC-003**: Aucun article déjà envoyé en cuisine n'est ré-expédié en double lors de l'ajout d'articles complémentaires sur une table rappelée.
- **SC-004**: Les 67 tests de non-régression existants restent au vert (100% de succès) complétés par la nouvelle suite de tests de rappel de table.

---

## Assumptions

- Les commandes actives sont indexées par `TableNumber` et `ActiveOrderId` dans la base SQLite locale (`LocalAppDbContext`) et dans la base maître (`AppDbContext`).
- Le statut d'une table ouverte sans article reste `Occupied` tant qu'elle n'est pas libérée explicitement ou soldée par encaissement.
- L'opérateur connecté a le droit de consulter et compléter les commandes de toutes les tables de son rang/salle.
