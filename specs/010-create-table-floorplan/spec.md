# Feature Specification: Création Directe de Table depuis la Vue Tables / Plan de Salle

**Feature Branch**: `010-create-table-floorplan`  
**Created**: 2026-08-30  
**Status**: Implemented  
**Input**: User description: "je veux pouvoir cree une nouvelle table directement de la vue tables"

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Création Rapide d'une Nouvelle Table depuis le Plan de Salle (Priority: P1) 🎯 MVP

En plein service ou lors d'un réagencement imprévu (ex: ajout d'une table d'appoint sur la terrasse ou dans un carré privé), le maître d'hôtel ou le serveur doit pouvoir créer instantanément une nouvelle table directement depuis la vue du plan de salle (Vue Tables), sans avoir à quitter l'écran pour aller dans les menus d'administration complexes.

**Why this priority**: Répond directement au besoin d'agilité opérationnelle en restaurant : le personnel de salle doit pouvoir enregistrer une nouvelle table en moins de 5 secondes lors des périodes de rush.

**Independent Test**: Aller sur la vue « 🗺️ Plan de Salle » $\to$ cliquer sur le bouton « ➕ Nouvelle Table » $\to$ renseigner le nom « T9 » et la capacité 4 couverts $\to$ valider. La nouvelle table « T9 » apparaît immédiatement sur la grille du plan de salle avec le statut « Libre ».

**Acceptance Scenarios**:

1. **Given** l'opérateur se trouve sur l'écran du plan de salle (Vue Tables), **When** il clique sur le bouton « ➕ Nouvelle Table », **Then** une boîte de dialogue modale ergonomique et tactile s'affiche, demandant le numéro/nom de la table et sa capacité en couverts.
2. **Given** la modale de création de table ouverte, **When** l'opérateur saisit un identifiant (ex: « T9 » ou « Terrasse 1 ») avec une capacité (ex: 4 places) et valide, **Then** la table est immédiatement enregistrée dans le système, la modale se ferme, une notification de confirmation s'affiche, et la nouvelle carte de table est instantanément ajoutée sur le plan de salle avec le statut « Libre ».
3. **Given** la modale de création de table ouverte, **When** l'opérateur clique sur « Annuler » ou sur la croix de fermeture, **Then** la saisie est annulée sans créer de table et le plan de salle reste inchangé.

---

### User Story 2 - Prise de Commande Immédiate sur la Table Créée (Priority: P1)

Dès qu'une nouvelle table a été créée depuis le plan de salle, l'opérateur doit pouvoir cliquer directement dessus pour ouvrir la table (définir le nombre de couverts et le serveur) et basculer instantanément dans le terminal de vente pour démarrer la commande.

**Why this priority**: Garantit un flux de travail continu : créer la table $\to$ l'ouvrir $\to$ prendre la commande des clients sans aucune friction ni rechargement d'application.

**Independent Test**: Créer la table « T10 » (capacité 2) $\to$ cliquer sur la carte « T10 » dans le plan de salle $\to$ saisir 2 couverts $\to$ le terminal de vente s'ouvre avec le badge « Table T10 » et un panier prêt à la saisie des articles.

**Acceptance Scenarios**:

1. **Given** une nouvelle table créée (« T10 ») au statut « Libre », **When** l'opérateur clique sur sa carte dans le plan de salle, **Then** le système propose l'ouverture de table avec le nombre de couverts par défaut égal à sa capacité et bascule vers l'écran de caisse.

---

### User Story 3 - Prévention des Doublons et Validation des Noms de Table (Priority: P2)

Le système doit vérifier l'unicité du numéro/nom de table (insensible à la casse, ex: « t2 » vs « T2 ») et valider la plage de capacité (1 à 30 places).

**Why this priority**: Évite la confusion en cuisine et sur les additions liée à des tables portant le même numéro.

**Independent Test**: Tenter de créer une table « T2 » (déjà existante). Le système empêche la duplication et prévient l'opérateur avec un message clair.

**Acceptance Scenarios**:

1. **Given** une table « T2 » existante, **When** l'opérateur tente de créer une nouvelle table sous le nom « T2 » ou « t2 », **Then** le système refuse la duplication et affiche un message d'erreur « La table T2 existe déjà ».
2. **Given** une saisie de capacité vide ou égale à 0, **When** l'opérateur valide, **Then** le système applique automatiquement une capacité minimale par défaut de 2 couverts ou demande une valeur valide comprise entre 1 et 30.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: L'interface du Plan de Salle (Vue Tables) DOIT comporter un bouton d'action tactile visible et accessible « ➕ Nouvelle Table ».
- **FR-002**: Cliquer sur « ➕ Nouvelle Table » DOIT ouvrir un formulaire tactile demandant obligatoirement le numéro / nom de la table et sa capacité (nombre de places assises).
- **FR-003**: Le formulaire DOIT proposer des raccourcis tactiles de capacité rapide (ex: 2, 4, 6, 8 couverts) ainsi qu'un champ numérique ajustable.
- **FR-004**: Le système DOIT valider l'unicité du nom de table de manière insensible à la casse au sein de l'établissement.
- **FR-005**: La table nouvellement créée DOIT être initialisée avec le statut « Libre » (`Status = Free`), 0 couvert actif et aucun identifiant de commande active.
- **FR-006**: L'enregistrement d'une nouvelle table DOIT rafraîchir immédiatement le plan de salle tactile sans rechargement complet de la page.
- **FR-007**: Le backend POS et l'API DOIVENT exposer un point d'accès `POST /api/tables` acceptant le numéro de table et sa capacité.
- **FR-008**: Toute tentative de création d'une table avec un identifiant vide ou déjà attribué DOIT renvoyer un message d'erreur explicite et préserver l'état de l'application.

---

### Key Entities

- **DiningTable**:
  - `TableNumber` (Chaîne, Clé unique) : Identifiant ou nom de la table.
  - `Capacity` (Entier) : Nombre maximal de convives / places assises.
  - `Status` (Enum) : Statut de la table (`Free`, `Occupied`, `BillRequested`, `Paid`).
  - `PositionX`, `PositionY` (Double) : Coordonnées 2D d'affichage sur le plan de salle.
  - `AssignedWaiterName` (Chaîne, Optionnel) : Nom du serveur en charge.
  - `CoversCount` (Entier) : Nombre de convives actuellement installés.
  - `ActiveOrderId` (Guid, Optionnel) : Identifiant de la commande en cours.

---

## Success Criteria *(mandatory)*

- **SC-001**: La création d'une nouvelle table et son affichage sur le plan de salle s'exécutent en moins de 100ms après validation.
- **SC-002**: Un opérateur peut créer et nommer une nouvelle table en moins de 3 interactions tactiles.
- **SC-003**: 100% des tables créées sont immédiatement interactives sans redémarrage.
- **SC-004**: 100% des tentatives de création de doublons sont bloquées avec notification.
- **SC-005**: 100% de la suite de tests automatisés (Domaine, Infrastructure, Client) passe avec succès (0 warning, 0 error).
