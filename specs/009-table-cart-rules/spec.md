# Feature Specification: Règles de Vidage du Panier et Réinitialisation post-Envoi Cuisine

**Feature Branch**: `009-table-cart-rules`  
**Created**: 2026-08-30  
**Status**: Draft  
**Input**: User description: "dans une table quand je click sur le button vider le panier il ne vide que les element qui ne sont pas envoyer vers la cuisine et une fois il est envoyer l'ecrant se vide"

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Vidage Sélectif du Panier de Table (Priority: P1) 🎯 MVP

Lorsqu'un opérateur ou serveur est en cours de saisie sur une table contenant à la fois des articles déjà envoyés en production cuisine et de nouveaux articles non encore envoyés, cliquer sur le bouton « Vider le panier » ne doit annuler et retirer que les articles en attente d'envoi. Les articles déjà envoyés en cuisine restent protégés dans la commande active de la table et ne sont pas effacés.

**Why this priority**: Empêche les erreurs opérationnelles graves (suppression accidentelle d'articles déjà cuisinés ou servis) tout en offrant une ergonomie tactile rapide pour annuler une erreur de saisie sur les nouveaux articles ajoutés.

**Independent Test**: Ouvrir la Table `T2` (qui contient déjà 2 Salades et 1 Burger envoyés en cuisine). Ajouter 2 Tiramisus (non envoyés). Cliquer sur le bouton « Vider le panier ». Vérifier que les 2 Tiramisus sont retirés, que les 2 Salades et le Burger restent affichés avec le total 38.50 €, et qu'un message informe l'opérateur.

**Acceptance Scenarios**:

1. **Given** une table occupée avec 3 articles déjà envoyés en cuisine (`isDispatched = true`) et 2 nouveaux articles ajoutés (`isDispatched = false`), **When** l'opérateur clique sur le bouton « Vider le panier », **Then** les 2 nouveaux articles sont immédiatement retirés du panier, les 3 articles envoyés en cuisine sont conservés, les totaux (HT, TVA, TTC) sont recalculés sur les articles conservés, et une notification indique que seuls les nouveaux articles ont été annulés.
2. **Given** une commande ou une table où AUCUN article n'a été envoyé en cuisine (`isDispatched = false` pour tous les articles), **When** l'opérateur clique sur « Vider le panier », **Then** tous les articles sont retirés et le panier redevient complètement vide.
3. **Given** une table où TOUS les articles présents dans le panier sont déjà envoyés en cuisine (`isDispatched = true`), **When** l'opérateur clique sur « Vider le panier », **Then** aucun article n'est supprimé et une notification informe l'opérateur que les articles déjà transmis en cuisine ne peuvent pas être vidés directement.

---

### User Story 2 - Réinitialisation et Transition d'Écran Post-Envoi Cuisine (Priority: P1)

Lorsqu'un serveur ou caissier valide l'envoi en cuisine (« Envoyer Cuisine ») des articles d'une table, l'écran de caisse en cours doit se vider / se réinitialiser et basculer automatiquement sur la vue du plan de salle (ou l'état de sélection de table), permettant au serveur d'enchaîner immédiatement le service d'une autre table sans laisser la commande précédente affichée.

**Why this priority**: Fluidité et rapidité du service en salle : dès qu'une commande est envoyée en production, le terminal tactile est libéré instantanément pour accueillir la commande suivante.

**Independent Test**: Sur la Table `T2`, ajouter 1 dessert et appuyer sur « Envoyer Cuisine ». Vérifier que le ticket KDS est émis, que le panier de travail se réinitialise et que l'interface bascule immédiatement sur le plan de salle avec la table `T2` mise à jour.

**Acceptance Scenarios**:

1. **Given** un panier de table contenant de nouveaux articles non envoyés, **When** l'opérateur clique sur le bouton « Envoyer Cuisine », **Then** les nouveaux articles sont enregistrés et dispatchés vers les stations de cuisine concernées, le panier local est réinitialisé, l'écran bascule automatiquement vers le plan de salle, et une notification de succès confirme l'envoi.
2. **Given** une table sélectionnée sur le plan de salle après un envoi cuisine réussi, **When** l'opérateur clique à nouveau sur cette même table, **Then** la table est rappelée et affiche tous ses articles avec le badge « En Cuisine ».

---

### User Story 3 - Gestion des Lignes Individuelles Non Envoyées (Priority: P2)

Permettre également la diminution de quantité ou la suppression unitaire d'un article non envoyé via les contrôles `[-]` ou la touche suppression, tout en bloquant la réduction en dessous de la quantité déjà envoyée en cuisine.

**Why this priority**: Assure la cohérence entre le vidage global du panier et les ajustements fins ligne par ligne.

**Independent Test**: Sur une ligne d'article déjà commandé à 2 unités en cuisine, si l'opérateur en ajoute 1 (quantité totale = 3, dont 1 nouvelle), appuyer sur `[-]` réduit la quantité à 2 (la part en cuisine) mais ne permet pas de descendre à 1 sans motif d'annulation explicite.

**Acceptance Scenarios**:

1. **Given** un article avec une quantité en cuisine de 2 et un ajout en attente de 1 (total 3), **When** l'opérateur clique sur `[-]`, **Then** la quantité passe à 2 et l'article redevient entièrement marqué « En Cuisine ».

---

### Edge Cases

- **Panier complètement vide** : Cliquer sur « Vider le panier » alors que le panier est déjà vide n'engendre aucune action ni erreur.
- **Envoi cuisine avec 0 nouvel article** : Si tous les articles du panier sont déjà `isDispatched = true`, cliquer sur « Envoyer Cuisine » ne génère aucun ticket doublon en cuisine et affiche une notification « Tous les articles sont déjà en cuisine ».
- **Coupure réseau lors de l'envoi cuisine** : Les articles sont enregistrés dans le journal local sécurisé (Local-First) avant la tentative de transmission ; l'écran se réinitialise après confirmation de persistance locale.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Le système DOIT distinguer, au sein d'un même panier de table, les lignes d'articles déjà transmises en cuisine (`IsDispatched = true`) des lignes en attente de transmission (`IsDispatched = false`).
- **FR-002**: L'action « Vider le panier » DOIT supprimer exclusivement les articles dont l'état est non envoyé (`IsDispatched = false`).
- **FR-003**: L'action « Vider le panier » NE DOIT PAS supprimer les articles dont l'état est déjà envoyé (`IsDispatched = true`), et DOIT recalculer immédiatement les totaux (HT, TVA, TTC) sur la base des articles restants.
- **FR-004**: Le système DOIT afficher un message explicite lorsque le vidage du panier a conservé les articles en cuisine (ex: *"Nouveaux articles retirés. Les articles en cuisine sont conservés."*).
- **FR-005**: Si tous les articles du panier sont déjà envoyés en cuisine, le système DOIT avertir l'utilisateur que le panier ne peut pas être vidé directement.
- **FR-006**: Lors du déclenchement de l'action « Envoyer Cuisine », le système DOIT persister et expédier uniquement les articles non encore envoyés (`IsDispatched = false`).
- **FR-007**: Immédiatement après la confirmation de l'envoi en cuisine, le système DOIT réinitialiser l'état du panier actif et basculer l'affichage vers la vue du plan de salle (ou vue d'accueil des tables).
- **FR-008**: Le système DOIT rafraîchir l'état du plan de salle en arrière-plan afin que le statut et le décompte de la table reflètent fidèlement la nouvelle commande.

---

### Key Entities *(include if feature involves data)*

- **OrderCart / TableSession**:
  - `TableNumber`: Identifiant de la table active.
  - `ActiveOrderId`: Identifiant unique de la commande associée.
  - `Items`: Liste des articles présents dans le panier, incluant pour chaque ligne le flag `IsDispatched` (booléen).
- **OrderItem / CartLine**:
  - `ProductId`, `ProductName`, `UnitPrice`, `Quantity`, `TaxRatePercent`.
  - `IsDispatched`: Indique si la ligne ou la fraction de quantité a déjà été émise vers les stations de production.
  - `SelectedModifiers`: Options ou cuissons associées.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: L'action « Vider le panier » s'exécute en moins de 50ms sur le terminal tactile sans aucun gel d'interface.
- **SC-002**: 100% des articles déjà envoyés en cuisine (`IsDispatched = true`) sont préservés lors du clic sur « Vider le panier ».
- **SC-003**: Lors de l'envoi en cuisine, la réinitialisation du panier et la transition vers le plan de salle s'effectuent en moins de 200ms.
- **SC-004**: Zéro doublon de ticket généré en cuisine lorsqu'une table complétée est envoyée en production.
- **SC-005**: 100% des tests unitaires, d'intégration et scénarios de régression passent avec succès (0 warning, 0 error).

---

## Assumptions

- Les serveurs et opérateurs ont besoin d'une transition rapide vers le plan de salle pour enchaîner les prises de commandes entre tables différentes.
- L'annulation d'un article déjà envoyé en cuisine relève d'une procédure d'annulation / décommande spécifique (pouvant requérir un motif ou des droits manager), distincte du simple bouton « Vider le panier » de saisie courante.
- L'environnement tactile de caisse supporte les retours haptiques et les notifications toast non bloquantes.
