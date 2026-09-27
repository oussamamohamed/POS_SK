# Feature Specification: Modification des Objets dans le Module de Configuration

**Feature Branch**: `011-edit-configuration-objects`  
**Created**: 2026-08-31  
**Status**: Draft  
**Input**: User description: "dans le module de configuration je veux pouvoir modifer les objects"

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Modification des Articles et Produits du Catalogue (Priority: P1) 🎯 MVP

En tant que gérant ou responsable de salle, je dois pouvoir modifier directement depuis la liste des articles du module de paramétrage les caractéristiques d'un produit existant (nom, prix unitaire, famille de rattachement, taux de TVA, poste de préparation en cuisine KDS, couleur et statut touche rapide « Coup de feu »), afin de réajuster ma carte, mes tarifs ou mes circuits de production sans avoir à recréer l'article.

**Why this priority**: L'ajustement des prix, des intitulés de plats et des affectations de postes de cuisine est l'opération de gestion la plus fréquente dans la vie d'un restaurant.

**Independent Test**: Ouvrir le panneau « ⚙️ Paramétrage POS » $\to$ onglet « 🏷️ Familles & Articles » $\to$ cliquer sur le bouton « ✏️ Modifier » sur la ligne « Burger Maison & Frites » $\to$ modifier le prix de 16.50 € à 17.50 € $\to$ valider $\to$ vérifier que la liste se met à jour, que le catalogue de caisse affiche le nouveau tarif de 17.50 € et que l'ajout au panier prend en compte le nouveau prix.

**Acceptance Scenarios**:

1. **Given** un article existant dans le catalogue (ex: « Burger Maison » à 16.50 €), **When** le gérant clique sur le bouton « ✏️ Modifier » sur la carte/ligne de cet article, **Then** une modale d'édition pré-remplie avec toutes les valeurs actuelles s'ouvre.
2. **Given** la modale d'édition d'article ouverte, **When** le gérant modifie un ou plusieurs champs (ex: prix = 17.50 €, active « Touche Rapide ») et clique sur « Enregistrer les modifications », **Then** les données sont mises à jour en base SQLite/API centrale, la modale se ferme, une notification de succès s'affiche, et la grille de vente POS reflète immédiatement les nouvelles informations sans rechargement.
3. **Given** la modale d'édition d'article ouverte, **When** le gérant clique sur « Annuler », **Then** aucune modification n'est enregistrée et l'article conserve ses valeurs initiales.

---

### User Story 2 - Modification des Familles / Catégories (Priority: P1)

En tant que gérant, je dois pouvoir modifier le libellé, la couleur distinctive et l'ordre d'affichage d'une famille existante (ex: renommer « Vins » en « Vins & Champagnes » et changer la couleur en bordeaux), afin d'organiser la navigation tactile de caisse.

**Why this priority**: La clarté visuelle des familles d'articles sur les terminaux tactiles conditionne la rapidité de saisie des serveurs lors des coups de feu.

**Independent Test**: Dans l'onglet « Familles & Articles », cliquer sur « ✏️ Modifier » sur la famille « Plats » $\to$ changer la couleur en `#8B5CF6` et le nom en « Plats & Spécialités » $\to$ valider $\to$ vérifier que l'onglet correspondant dans la caisse adopte immédiatement le nouveau nom et la nouvelle couleur.

**Acceptance Scenarios**:

1. **Given** une catégorie existante (« Plats »), **When** l'opérateur clique sur « ✏️ Modifier », **Then** un formulaire d'édition apparaît avec le nom et la couleur actuels.
2. **Given** le formulaire d'édition de catégorie renseigné, **When** l'opérateur enregistre, **Then** la catégorie est mise à jour et la barre des onglets de caisse est instantanément synchronisée.

---

### User Story 3 - Modification des Employés et Codes PIN (Priority: P1)

En tant qu'administrateur, je dois pouvoir modifier les informations d'un collaborateur existant (nom, rôle : Serveur / Manager / Admin / Cuisinier, et code PIN d'authentification à 4-6 chiffres), notamment lors d'un changement de rôle ou d'une réinitialisation de code PIN oublié.

**Why this priority**: Sécurité et flexibilité opérationnelle pour gérer les arrivées, départs et changements de postes au sein de la brigade.

**Independent Test**: Aller sur l'onglet « 👥 Serveurs & Codes PIN » $\to$ cliquer sur « ✏️ Modifier » sur l'employé « Alexandre » $\to$ modifier son code PIN de `1234` à `5678` et son rôle en « Responsable de Salle » $\to$ valider $\to$ verrouiller l'écran et vérifier que le code `5678` déverrouille bien la session avec le nouveau rôle.

**Acceptance Scenarios**:

1. **Given** un employé existant, **When** le gestionnaire clique sur « ✏️ Modifier », **Then** une modale permet de changer le nom, le rôle dans la liste déroulante et de saisir un nouveau code PIN.
2. **Given** un code PIN modifié (ex: `5678`), **When** la modification est validée, **Then** l'ancien PIN devient inopérant et le nouveau PIN est exigé lors du prochain déverrouillage de caisse.
3. **Given** une tentative de saisie d'un code PIN avec moins de 4 chiffres, **When** le gestionnaire tente d'enregistrer, **Then** le système bloque la validation avec un message d'erreur explicite.

---

### User Story 4 - Modification des Imprimantes Réseau et Postes de Production (Priority: P2)

En tant que responsable technique, je dois pouvoir modifier la configuration d'une imprimante ticket existante (nom, adresse IP, port 9100, largeur de papier 80mm/58mm, déclencheur de tiroir-caisse RJ11 et stations de production associées : Chaude, Froide, Bar), notamment lors du remplacement d'un équipement matériel ou d'un changement de bail DHCP.

**Why this priority**: Continuité d'activité matérielle sans devoir supprimer et recréer les liaisons de tickets de caisse et de bons de commande.

**Independent Test**: Sur l'onglet « 🖨️ Imprimantes Réseau », modifier l'adresse IP de l'imprimante « Cuisine Chaude » de `192.168.1.201` à `192.168.1.205` $\to$ enregistrer $\to$ vérifier que les futurs tickets de cuisine chaude ciblent la nouvelle adresse IP.

**Acceptance Scenarios**:

1. **Given** une imprimante configurée, **When** l'opérateur clique sur « ✏️ Modifier », **Then** ses paramètres réseau et ses affectations de stations de production s'affichent en mode édition.
2. **Given** de nouveaux paramètres réseau saisis (IP, Port, Stations), **When** l'opérateur enregistre, **Then** la configuration du pool d'impression est immédiatement mise à jour.

---

### Edge Cases

- **Modification concurrente de commande en cours** : Si le prix d'un produit est modifié alors qu'une table a déjà commandé ce produit, la commande en cours conserve le tarif initial historique (traçabilité fiscale NF525). Seuls les nouveaux ajouts bénéficient du tarif modifié.
- **Code PIN dupliqué** : Avertir l'administrateur si le nouveau code PIN saisi est déjà attribué à un autre employé actif pour éviter les usurpations de session.
- **Format d'adresse IP invalide** : Contrôler la syntaxe IPv4 (`x.x.x.x`) lors de la modification d'imprimante réseau.
- **Suppression/Remplacement de famille d'un produit** : Si la famille d'un produit est modifiée, le produit doit disparaître de son ancien onglet et s'afficher dans le nouvel onglet sans laisser d'orphelins.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Le module de configuration DOIT proposer un bouton d'action « ✏️ Modifier » pour chaque élément des listes : Articles, Familles, Employés et Imprimantes.
- **FR-002**: Cliquer sur « ✏️ Modifier » DOIT ouvrir une interface d'édition (modale ou formulaire contextuel) pré-remplie avec l'intégralité des attributs existants de l'objet sélectionné.
- **FR-003**: Le backend REST et les services d'application DOIVENT exposer les points d'accès de mise à jour correspondants :
  - `PUT /api/products/{id}` pour les articles
  - `PUT /api/categories/{id}` pour les familles
  - `PUT /api/staff/{id}` pour les employés
  - `PUT /api/printers/{id}` pour les imprimantes
- **FR-004**: La modification d'un article DOIT permettre d'ajuster son nom, son prix TTC, son taux de TVA, sa catégorie, sa couleur, son poste de préparation KDS, son ordre d'affichage et son statut de touche rapide.
- **FR-005**: La modification d'une catégorie DOIT synchroniser immédiatement la barre d'onglets de caisse et la palette visuelle.
- **FR-006**: La modification d'un employé DOIT actualiser immédiatement son rôle et son code PIN chiffré dans le cache d'authentification.
- **FR-007**: La modification d'une imprimante DOIT actualiser dynamiquement le registre des imprimantes réseau et les filtres de stations de production.
- **FR-008**: Le système DOIT valider les formats de données avant tout enregistrement (prix $> 0$, PIN de 4 à 6 chiffres unique parmi les collaborateurs actifs, nom non vide, format IP valide).
- **FR-009**: L'enregistrement d'une modification DOIT afficher un toast de confirmation et mettre à jour les composants de l'interface en temps réel sans nécessiter de rafraîchissement complet du navigateur (`F5`).
- **FR-010**: L'opérateur DOIT pouvoir annuler toute modification en cours sans altérer les données existantes.

---

### Key Entities

- **Product**: `Id`, `Name`, `CategoryId`, `Price` (`Money`), `TaxRatePercent`, `PreparationStationId`, `ColorHex`, `IsQuickKey`, `DisplayOrder`.
- **Category**: `Id`, `Name`, `ColorHex`, `DisplayOrder`, `IconName`.
- **User / Staff**: `Id`, `Name`, `Role`, `PinHash`, `IsActive`.
- **PrinterConfiguration**: `Id`, `Name`, `IpAddress`, `Port`, `PaperWidthMm`, `HasCashDrawer`, `TargetStations`.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: L'ouverture de la modale d'édition pré-remplie s'effectue en moins de 50ms au clic.
- **SC-002**: L'enregistrement de la modification et la répercussion dans le terminal de vente s'exécutent en moins de 100ms.
- **SC-003**: 100% des modifications de prix ou d'intitulés d'articles sont immédiatement visibles dans le catalogue de vente de caisse.
- **SC-004**: 100% des tentatives de modification avec données invalides (PIN trop court, prix négatif, IP corrompue) sont bloquées avec message d'erreur clair.
- **SC-005**: 100% des tests automatisés (Domain, Infrastructure, Application, MAUI Client) passent avec succès (0 warning, 0 error).

---

## Assumptions

- Seuls les utilisateurs connectés avec un profil disposant des droits administratifs ou de gérance ont accès au module de configuration.
- Les modifications de configuration sont persistées localement (SQLite) et synchronisées sur le serveur central.
