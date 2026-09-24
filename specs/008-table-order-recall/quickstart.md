# Quickstart Validation Guide: Table Order Recall & Cart Hydration

**Feature**: `008-table-order-recall`  
**Date**: 2026-08-30  

---

## 1. Objectif du Test

Valider de bout en bout que la sélection et le rappel d'une table occupée dans le plan de salle ou en caisse restaure immédiatement la totalité de son contenu d'articles, ses quantités, ses modificateurs et ses montants calculés, et permet d'ajouter des lignes de suite de service sans écrasement.

---

## 2. Scénarios de Validation Exécutables

### Scénario 1 : Création et Rappel de Commande sur Table
1. **Ouvrir le POS** sur `http://localhost:5000` (ou application MAUI).
2. **Accéder au Plan de Salle** : Cliquer sur la table `T2` (3 couverts).
3. **Ajouter des articles** :
   * 2x `Salade César Poulet` (9.50 €)
   * 1x `Burger Gourmet Rossini` (19.50 €)
   * Sous-total attendu : `38.50 €` TTC.
4. **Cliquer sur "Envoyer Cuisine"** (la commande est enregistrée et transmise).
5. **Basculer vers le Plan de Salle**, cliquer sur une autre table (`T1` vide), puis **re-cliquer sur `T2`**.
6. **Vérification** :
   * Le badge indique `Table T2 (3 Couverts)`.
   * Le panier contient bien les 2 Salades et le Burger.
   * Le total TTC affiche exactement `38.50 €`.
   * Les lignes portent le badge "En Cuisine" (IsDispatched: true).

### Scénario 2 : Ajout Incrémental de Desserts
1. Sur la table `T2` rappelée, ajouter 2x `Tiramisu Spéculos Maison` (7.50 €).
2. Nouveau total : `53.50 €` TTC.
3. Cliquer sur "Envoyer Cuisine".
4. Vérifier que seul le ticket des Tiramisu est expédié à la station `DESSERT` sans ré-expédier les salades et le burger.
5. Re-rappeler `T2` : les 5 articles sont présents dans la commande consolidée.

### Scénario 3 : Règlement et Libération de Table
1. Sur la table `T2`, cliquer sur "Encaisser".
2. Régler la somme totale de `53.50 €` par Carte Bancaire.
3. Retourner sur le Plan de Salle : la table `T2` passe au statut `Libre` (vert).
4. Re-sélectionner `T2` : le panier est vierge et prêt pour de nouveaux clients.

---

## 3. Commandes de Validation Automatisée

```powershell
# Exécution de la suite complète de tests incluant le rappel de table
dotnet test tests/RestaurantPos.Infrastructure.Tests/RestaurantPos.Infrastructure.Tests.csproj
dotnet test tests/RestaurantPos.Client.Maui.Tests/RestaurantPos.Client.Maui.Tests.csproj
```
