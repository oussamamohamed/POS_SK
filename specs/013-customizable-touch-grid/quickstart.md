# Quickstart & Validation Guide: Grille Tactile avec Pagination & Zero-Scrollbar

**Feature**: `013-customizable-touch-grid`  
**Date**: 2026-09-03  
**Status**: Ready

---

## 1. Démarrage Rapide

```bash
# Compiler l'application et la suite de tests
dotnet build

# Exécuter l'ensemble des tests unitaires et d'intégration
dotnet test

# Lancer le serveur API et client Web sur le port 5000
dotnet run --project src/RestaurantPos.Api/RestaurantPos.Api.csproj -c Debug --urls http://localhost:5000
```

---

## 2. Scénarios de Validation de Bout en Bout

### Scénario 1 : Pagination Multi-Pages sur l'Écran de Vente (Caisse)
1. Ouvrir le navigateur à l'adresse `http://localhost:5000`.
2. Cliquer sur la catégorie **Pizzas** ou une catégorie dense.
3. Observer la grille $4 \times 4$ de 16 cases et la barre de pagination tactiles en bas de grille (`◀`, `Page 1 / 2`, `▶`, pastilles `● ○`).
4. Cliquer sur le bouton `▶` ou sur la pastille 2 :
   - La grille bascule instantanément (< 50ms) vers la Page 2.
   - Les articles de la page 2 s'affichent à leurs coordonnées précises.
5. Cliquer sur un article de la Page 2 :
   - L'article est immédiatement ajouté au panier de vente sans rechargement.

### Scénario 2 : Élimination Intégrale de l'Ascenseur (Zero-Scrollbar)
1. Inspecter l'écran de vente :
   - Ajouter plus de 15 articles au panier.
   - Constater que la liste du panier défile au doigt/souris de manière fluide mais **sans aucune barre d'ascenseur apparente** du navigateur.
   - Constater que l'écran global ne subit aucun défilement vertical (`overflow: hidden; height: 100vh`).
2. Naviguer vers les différents écrans via la barre de navigation inférieure :
   - **Plan de salle (`tablesView`)** : Vérifier l'absence d'ascenseur.
   - **Écran Cuisine KDS (`kdsView`)** : Vérifier l'agencement sans scrollbar.
   - **Paramétrage Admin (`adminView`)** : Vérifier que les onglets et listes internes tiennent dans le viewport sans ascenseur externe.
   - **Modales (Encaissement, Division, Clôture Z, Édition de slot)** : Vérifier que chaque modale s'adapte sans dépasser du viewport.

### Scénario 3 : Éditeur Back-Office Multi-Pages
1. Aller dans **Paramétrage > Disposition de l'Écran**.
2. Sélectionner une catégorie.
3. Cliquer sur les onglets de page (`Page 1`, `Page 2`, `➕ Page`).
4. Glisser un article depuis le catalogue vers une case vide de la `Page 2`.
5. Vérifier que la modification est enregistrée et immédiatement disponible en caisse sur la `Page 2`.

---

## 3. Scripts de Validation Automatisée

```powershell
# Validation API multi-pages
$p1 = Invoke-RestMethod -Uri "http://localhost:5000/api/grid-layouts/CAT_62CD75AE?page=0" -Method Get
Write-Host "Page 1 Slots Count: $($p1.slots.Count) - TotalPages: $($p1.totalPages)"

$p2 = Invoke-RestMethod -Uri "http://localhost:5000/api/grid-layouts/CAT_62CD75AE?page=1" -Method Get
Write-Host "Page 2 Slots Count: $($p2.slots.Count) - PageIndex: $($p2.pageIndex)"
```
