# Technical Research: Grille Tactile avec Pagination & Interface Sans Ascenseur

**Feature**: `013-customizable-touch-grid`  
**Date**: 2026-09-03  
**Status**: Resolved

---

## 1. Ergonomie de Pagination Tactile vs Défilement Vertical

### Decision
Adopter une **pagination matricielle discrète par blocs fixes $4 \times 4$** (16 tuiles par page) au lieu d'un défilement vertical continu (scroll) sur l'écran de vente.

### Rationale
- **Mémoire Musculaire & Fixité Spatiale** : Dans un environnement tactile de caisse, un défilement continu brise la fixité spatiale des articles (le serveur doit chercher visuellement au lieu d'appuyer réflexivement sur un emplacement stable).
- **Zéro Dépassement** : Une matrice $4 \times 4$ fixe garantit que la taille de chaque tuile reste exactement carrée et identique quelle que soit la quantité de produits dans la catégorie.
- **Accès Rapide** : Les boutons tactiles `◀ Précédent` et `Suivant ▶` combinés aux pastilles indicatrices (`● ○ ○`) permettent de sauter instantanément d'une page à l'autre en un seul tap tactile sans inertie ni risque de double-tap erroné.

### Alternatives Considered
- *Infinite Scroll / Défilement vertical* : Rejeté car provoque l'apparition de barres d'ascenseurs, déforme l'alignement vertical et ralentit la saisie tactile.
- *Carrousel horizontal continu* : Rejeté car les gestes de balayage rapides peuvent s'arrêter entre deux positions et masquer partiellement des tuiles.

---

## 2. Architecture Technique « Zéro Ascenseur » (Zero-Scrollbar / Fixed Viewport)

### Decision
Appliquer une stratégie globale de **verrouillage de viewport (`viewport-locked layout`)** avec masquage systématique des barres d'ascenseur natives du navigateur et contrôle strict des conteneurs internes.

### Rationale
- **Règles CSS Globales** :
  ```css
  html, body {
      height: 100vh;
      max-height: 100vh;
      overflow: hidden;
      margin: 0;
      padding: 0;
      user-select: none;
      -webkit-user-select: none;
      touch-action: manipulation;
  }

  /* Masquage de toutes les barres d'ascenseur sur tous les navigateurs */
  * {
      scrollbar-width: none !important; /* Firefox */
      -ms-overflow-style: none !important; /* IE/Edge */
  }
  *::-webkit-scrollbar {
      display: none !important; /* Chrome, Safari, Edge, WebKit */
      width: 0 !important;
      height: 0 !important;
  }
  ```
- **Zones Débordantes Internes** (ex. liste des lignes du panier de caisse, liste des commandes KDS) :
  - La zone utilise `overflow-y: auto; overflow-x: hidden; -webkit-overflow-scrolling: touch;`.
  - Le défilement tactile naturel (inertiel au doigt) reste 100% fonctionnel mais aucun ascenseur visuel disgracieux n'apparaît.

### Alternatives Considered
- *Custom stylized thin scrollbars* : Rejeté car l'utilisateur a explicitement demandé d'éliminer l'ascenseur pour tous les écrans, ce qui correspond au standard des interfaces POS natives dédiées (type iPad POS / Clover / Toast).

---

## 3. Modèle de Données Multi-Pages & Résolution de Requêtes

### Decision
Conserver le schéma composite `(CategoryId, PageIndex)` sur `GridLayout` avec `PageIndex` commençant à 0.
- `GET /api/grid-layouts/{categoryId}?page={pageIndex}` : Récupère la page demandée ou retourne la Page 0 avec le décompte total des pages (`totalPages`).
- `GET /api/grid-layouts/{categoryId}/pages` : Récupère toutes les pages d'une catégorie en une seule requête pour le pré-chargement en cache local.
- `POST /api/grid-layouts` : Sauvegarde ou crée une page spécifique de layout.

### Rationale
- Permet une rétrocompatibilité totale avec les DTOs existants.
- Les clients Web et MAUI peuvent mettre en cache toutes les pages localement dans `localStorage` / SQLite pour des transitions instantanées ($< 10\text{ ms}$).

---

## 4. Éditeur Back-Office Multi-Pages

### Decision
Intégrer une barre de gestion de pages au-dessus de la matrice miroir dans l'onglet d'administration :
- Boutons de sélection rapide de page : `[Page 1] [Page 2] ... [➕ Ajouter une page]`.
- Chaque page est éditée indépendamment avec le même moteur Drag & Drop.
- La palette latérale du catalogue met en surbrillance les articles déjà placés sur la page active (`Placé sur cette page`), sur une autre page (`Placé Page X`) ou disponibles (`+ Placer`).
