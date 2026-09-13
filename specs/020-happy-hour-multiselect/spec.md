# Feature Specification: Happy Hour Multi-Select Configuration (Articles & Familles)

**Feature Branch**: `020-happy-hour-multiselect`

**Created**: 2026-09-13

**Status**: Ready for Planning

**Input**: User description: "mise a jour pour le happy hour changer dans l'interface graphique pour pouvoir choisir plusieur article ou famille par une config happy hour"

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sélection et Application Groupée par Famille / Catégorie (Priority: P1)

En tant que gérant ou responsable de restaurant, je souhaite sélectionner en un clic une ou plusieurs familles de produits (ex: "Bières", "Cocktails", "Tapas") et leur appliquer instantanément une remise en pourcentage commune (ex: -20%) pour un planning Happy Hour donné, sans devoir configurer chaque article un par un.

**Why this priority**: C'est le moyen le plus rapide et le plus efficace d'activer un Happy Hour sur tout un rayon du bar ou de la carte, évitant les erreurs d'omission d'articles.

**Independent Test**: Ouvrir l'écran de configuration Happy Hour, sélectionner l'onglet "🏷️ Familles éligibles", cocher 2 familles (ex: "Bières" et "Cocktails"), saisir 20% de remise, cliquer sur "Appliquer au lot", et vérifier que tous les articles de ces 2 familles bénéficient immédiatement du tarif réduit pendant le créneau.

**Acceptance Scenarios**:

1. **Given** un planning Happy Hour sélectionné, **When** le gérant coche plusieurs familles et saisit un pourcentage de remise, **Then** toutes les familles cochées reçoivent la règle de remise correspondante enregistrée pour ce planning.
2. **Given** une liste de familles affichée avec leurs statuts, **When** le gérant clique sur "Tout sélectionner", **Then** toutes les familles visibles sont cochées d'un coup.
3. **Given** des familles déjà associées à un planning, **When** le gérant consulte l'onglet des familles, **Then** les familles actuellement sous promotion Happy Hour apparaissent clairement identifiées avec le pourcentage appliqué et un bouton de retrait rapide.

---

### User Story 2 - Sélection Tactile Multiple d'Articles avec Application de Prix Fixe ou Remise (Priority: P1)

En tant que responsable, je souhaite sélectionner plusieurs articles spécifiques à la fois (via une grille tactile avec filtres de catégorie, cases à cocher / tags sélectionnables et recherche rapide), puis leur affecter en une seule action soit un prix fixe commun (ex: 5,00 € pour toutes les bières pression sélectionnées), soit une remise en pourcentage commune (ex: -25%).

**Why this priority**: Les bars et restaurants ont souvent des offres groupées ciblées sur des produits phares de même valeur (ex: toutes les bières 50cl à 5 € ou tous les cocktails signature à 7 €).

**Independent Test**: Ouvrir l'onglet "🍺 Articles spécifiques", filtrer par la catégorie "Boissons", cocher 4 bières différentes avec le bouton "Tout sélectionner", choisir le mode "Prix fixe", saisir `5.00 €`, valider, et vérifier que 4 règles individuelles sont créées et visibles dans la liste des règles actives.

**Acceptance Scenarios**:

1. **Given** la grille de sélection des articles, **When** le gérant filtre par une catégorie ou saisit un mot-clé de recherche, **Then** la grille n'affiche que les articles correspondants tout en préservant les éléments déjà cochés.
2. **Given** plusieurs articles cochés et le mode "Prix fixe" sélectionné, **When** l'utilisateur saisit un montant (ex: 5,00 €) et valide, **Then** chaque article sélectionné se voit attribuer une règle `FixedPrice` de 5,00 € rattachée au planning actif.
3. **Given** plusieurs articles cochés et le mode "Remise en %" sélectionné, **When** l'utilisateur saisit un pourcentage (ex: 30%) et valide, **Then** chaque article sélectionné se voit attribuer une règle `PercentageDiscount` de 30%.
4. **Given** un article ayant déjà une règle dans ce planning, **When** une nouvelle règle groupée est appliquée sur cet article, **Then** la règle existante est mise à jour avec la nouvelle valeur sans duplication.

---

### User Story 3 - Vue Détaillée et Gestion Collective des Règles Actives (Priority: P2)

En tant que superviseur, je souhaite visualiser clairement la liste complète des règles actuellement configurées sur le planning (séparées par familles et par articles), avec la possibilité de supprimer des règles en masse ou individuellement directement depuis l'écran tactile.

**Why this priority**: Assure la lisibilité et la maintenance des promotions sans encombrer la vue ni obliger à supprimer les articles un à un.

**Independent Test**: Cocher 3 règles d'articles existantes dans la liste récapitulative, cliquer sur "Supprimer la sélection", et vérifier qu'elles sont instantanément retirées du planning et de la base de données.

**Acceptance Scenarios**:

1. **Given** un planning sélectionné, **When** l'utilisateur visualise l'écran de configuration, **Then** deux sections distinctes affichent d'un côté les familles avec leurs remises, et de l'autre les articles avec leurs prix Happy Hour / remises.
2. **Given** la liste des règles d'articles, **When** l'utilisateur coche plusieurs règles et clique sur "Supprimer la sélection", **Then** les règles correspondantes sont supprimées et la grille tarifaire est actualisée.

---

## Edge Cases

- Que se passe-t-il si un article fait partie d'une famille éligible à -20%, mais qu'une règle spécifique de prix fixe à 5,00 € lui est également attribuée ?  
  -> La règle spécifique de produit (prix fixe) reste strictement prioritaire sur la remise de famille, conformément aux spécifications du moteur tarifaire Feature 019.
- Que se passe-t-il si l'utilisateur clique sur "Appliquer" sans avoir coché aucun article ou aucune famille ?  
  -> Un message d'avertissement tactile indique immédiatement "Veuillez sélectionner au moins un élément".
- Que se passe-t-il si le prix fixe saisi est supérieur ou égal au prix standard de l'article ?  
  -> Le système avertit l'utilisateur ou ignore l'application si le prix Happy Hour ne constitue pas une réduction avantageuse.
- Que se passe-t-il si la recherche textuelle ne renvoie aucun produit ?  
  -> Un état vide ergonomique affiche "Aucun article trouvé pour cette recherche" avec un bouton pour réinitialiser le filtre.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: L'interface d'administration Happy Hour DOIT proposer deux onglets de configuration dédiés au sein du planning : "🏷️ Familles éligibles" et "🍺 Articles spécifiques".
- **FR-002**: L'onglet des familles DOIT afficher toutes les catégories du catalogue sous forme de tuiles/cartes sélectionnables avec case à cocher, nom de la famille et nombre d'articles inclus.
- **FR-003**: L'onglet des familles DOIT permettre d'appliquer en une seule action une remise en pourcentage (0-100%) à toutes les familles sélectionnées.
- **FR-004**: L'onglet des articles DOIT proposer une barre de filtre par catégorie, un champ de recherche instantané, et une grille tactile d'articles avec cases à cocher.
- **FR-005**: L'onglet des articles DOIT comporter les boutons "Tout sélectionner" (visibles) et "Tout désélectionner".
- **FR-006**: L'utilisateur DOIT pouvoir basculer entre deux modes de tarification groupée pour les articles : "Prix fixe (€)" ou "Remise (%)".
- **FR-007**: La validation d'un lot DOIT créer ou mettre à jour les règles associées au planning via une requête API groupée (batch endpoint) pour garantir l'atomicité et la réactivité.
- **FR-008**: L'interface DOIT afficher un compteur dynamique du nombre d'éléments sélectionnés (ex: "4 articles sélectionnés").
- **FR-009**: L'utilisateur DOIT pouvoir supprimer des règles de prix en lot via des cases à cocher et un bouton "Supprimer la sélection".
- **FR-010**: Tout changement enregistré dans les règles groupées DOIT notifier en temps réel les terminaux caisse et actualiser la grille tarifaire Happy Hour active.

---

### Key Entities

- **HappyHourSchedule**: Créneau horaire (nom, jours de la semaine, heure début/fin, priorité, takeaway éligible).
- **HappyHourPriceRule**: Règle tarifaire rattachée à un planning. Type de cible (`Product` ou `Category`), identifiant de cible, mode de prix (`FixedPrice` ou `PercentageDiscount`), montant ou pourcentage.
- **BatchPriceRuleRequest**: DTO transportant une collection d'identifiants cibles (produits ou catégories) avec la configuration tarifaire commune à appliquer en une seule opération.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: La configuration d'un Happy Hour couvrant 10 articles ou 3 familles de produits s'effectue en moins de 15 secondes sur l'écran tactile (contre plus d'une minute avec la saisie unitaire).
- **SC-002**: L'opération d'enregistrement groupé (batch save) s'exécute avec un retour visuel instantané (< 300 ms).
- **SC-003**: 100% des articles sélectionnés reçoivent la règle tarifaire sans désynchronisation ni oubli.
- **SC-004**: Les utilisateurs sur tablette ou écran tactile peuvent sélectionner et désélectionner les articles avec des cibles tactiles confortables (taille minimale 48x48px).

---

## Assumptions

- Les familles correspondent exactement aux catégories définies dans le catalogue existant (`Categories`).
- Le composant tactile réutilise le design system sombre et amber déjà mis en place pour la Feature 019.
- Les droits de modification des plannings et des règles Happy Hour restent réservés aux profils ayant accès au paramétrage (FloorManager, Admin).
