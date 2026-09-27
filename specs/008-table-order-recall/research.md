# Research & Architectural Decisions: Table Order Recall & Cart Hydration

**Feature**: `008-table-order-recall`  
**Date**: 2026-08-30  

---

## 1. Analyse du Problème & Cause Racine (Root Cause)

### Problème Constaté
Lorsqu'un utilisateur sélectionne ou rappelle une table occupée (ex: `Table T2`) depuis le plan de salle ou le terminal de vente, l'en-tête de la table change bien, mais le panier d'articles reste vide ou conserve l'état résiduel en mémoire sans charger la commande active enregistrée en base pour cette table.

### Diagnostic Technique
1. **Entité `DiningTable` :** Contient bien la référence `ActiveOrderId`, mais aucun point d'accès unifié ne rechargeait automatiquement les `OrderLines` associées lors de la transition d'état.
2. **Contrat `ITableManagementService` :** Disposait de `OpenTableAsync` et `TransferTableAsync`, mais pas de méthode dédiée `GetActiveOrderForTableAsync(string tableNumber)` renvoyant l'arbre complet de la commande (`Order` + `OrderLine` + `Modifiers` + `DispatchStatus`).
3. **Clients (MAUI & Web) :** L'événement de sélection de table dans `FloorPlanViewModel` et `app.js` basculait uniquement la propriété `activeTable`, sans déclencher l'appel d'hydratation asynchrone du panier.

---

## 2. Décisions d'Architecture

### Décision 1 : Modèle d'Hydratation Réactive du Panier (Cart Hydration)
* **Approche retenue :** Lors de la sélection d'une table, le `PosTerminalViewModel` (et le client Web) appelle `GetActiveOrderForTableAsync(tableNumber)`.
  - Si la table a une commande active (`ActiveOrderId != null`) : Le panier est réinitialisé et repeuplé fidèlement avec toutes les lignes enregistrées, leurs quantités, leurs prix unitaires exacts en `Money`, leurs modificateurs et leur statut `IsDispatched`.
  - Si la table est libre (`Status == Free`) : Le panier est initialisé à vide, prêt pour une nouvelle saisie.
* **Avantages :** 
  - Cohérence parfaite entre la vue de salle et l'écran de caisse.
  - Calcul instantané des totaux HT/TVA/TTC dès le chargement de la vue.

### Décision 2 : Distinction des Lignes "Déjà Envoyées" vs "Nouvelles Lignes" (Incremental Dispatch)
* **Approche retenue :** Chaque ligne d'article dans la commande possède un indicateur booléen `IsDispatched` (ou enum `LineStatus { Pending, Dispatched, Prepared, Served }`).
  - Les articles déjà transmis à la cuisine (`IsDispatched = true`) portent un badge visuel ("En Cuisine") et sont protégés contre les suppressions directes non autorisées.
  - Les nouveaux articles ajoutés au fur et à mesure sont `IsDispatched = false`.
  - Le bouton "Envoyer Cuisine" déclenche le routage KDS **uniquement sur le delta des lignes non expédiées**, puis met à jour leur drapeau en base de données.
* **Avantages :** Zéro doublon de préparation en cuisine ni de réimpression intempestive sur les imprimantes thermiques de production.

### Décision 3 : Persistance Locale-First SQLite & API Synchronisée
* **Approche retenue :** La méthode `GetActiveOrderForTableAsync` est implémentée sur `LocalAppDbContext` (MAUI) et `AppDbContext` (API backend) avec les mêmes signatures et DTOs sérialisables.
* **Point d'accès REST API :** 
  - `GET /api/tables/{tableNumber}/order` $\to$ Renvoie la commande active complète ou 404/vide si table libre.
  - `POST /api/tables/{tableNumber}/items` $\to$ Ajoute ou met à jour des lignes d'articles sur la table active.
  - `POST /api/tables/{tableNumber}/dispatch` $\to$ Expédie les nouvelles lignes en cuisine.
