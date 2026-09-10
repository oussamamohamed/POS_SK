# Phase 0 Research: Modification des Objets dans le Module de Configuration

**Feature**: `011-edit-configuration-objects`  
**Date**: 2026-08-31

---

## 1. Analyse des besoins & Ergonomie de Gestion

### Problématique
Le module d'administration permettait initialement de créer de nouveaux articles, familles, utilisateurs et imprimantes, mais ne proposait pas d'action directe pour modifier un tarif, changer un nom de plat, réinitialiser un code PIN ou ajuster une adresse IP d'imprimante. L'opérateur devait soit manipuler directement la base de données, soit recréer l'entité.

### Solution Ciblée
1. **Bouton d'édition sur chaque carte** : Chaque carte dans les listes administratives (`#adminCatalogList`, `#adminStaffList`, `#adminPrintersList`) intègre un bouton tactile `✏️ Modifier`.
2. **Modales d'édition pré-remplies** :
   - `editProductModal` : Formulaire pré-rempli avec Nom, Famille, Prix TTC, TVA, Poste de cuisine, Statut touche rapide, Couleur.
   - `editCategoryModal` : Nom et Couleur du badge.
   - `editStaffModal` : Nom, Rôle et Code PIN (nouveau code optionnel ou pré-rempli).
   - `editPrinterModal` : Nom, IP, Port, Largeur, Tiroir-caisse, Stations.
3. **Endpoints REST idempotents `PUT`** :
   - `PUT /api/products/{id}`
   - `PUT /api/categories/{id}`
   - `PUT /api/staff/{id}`
   - `PUT /api/printers/{id}`
4. **Synchronisation dynamique de l'interface** :
   - Rechargement immédiat du catalogue (`loadCatalogData()`), des onglets de caisse (`categoryTabsBar`), des touches rapides (`quickKeysBar`) et de l'administration (`loadAdminData()`) sans aucun rechargement complet de la page.

---

## 2. Décisions d'Architecture

### Décision 1 : Granularité des endpoints REST
- **Choix** : Utiliser la méthode HTTP `PUT` avec payload complet de l'entité.
- **Justification** : Standard REST, idempotent et permettant la mise à jour complète de l'objet.

### Décision 2 : Sécurité des codes PIN modifiés
- **Choix** : Lors de la modification du code PIN d'un collaborateur, hasher immédiatement le PIN en SHA-256 avec sel avant persistance.
- **Justification** : Respect des standards de sécurité NF525 / RGPD.

### Décision 3 : Traçabilité des commandes existantes
- **Choix** : Les commandes déjà ouvertes ou clôturées conservent les lignes `OrderItem` avec leur `UnitPrice` et `TaxRatePercent` historiques.
- **Justification** : Conformité fiscale inaltérable : la modification du prix d'un produit en carte n'altère pas les additions déjà imprimées ou en cours.

---

## 3. Matrice de validation des choix techniques

| Entité | Endpoint REST | Contrat Service | Impact Interface Caisse |
|---|---|---|---|
| **Article / Produit** | `PUT /api/products/{id}` | `ICatalogManagementService.UpdateProductAsync` | Grille de vente, Quick-keys, Prix de vente |
| **Famille / Catégorie** | `PUT /api/categories/{id}` | `ICatalogManagementService.UpdateCategoryAsync` | Onglets de caisse, Couleurs de badges |
| **Collaborateur** | `PUT /api/staff/{id}` | `IStaffManagementService.UpdateStaffAsync` | Déverrouillage PIN, Attribution de serveur |
| **Imprimante** | `PUT /api/printers/{id}` | `IPrinterRoutingService.UpdatePrinterAsync` | Routage KDS, Impression de bons |
