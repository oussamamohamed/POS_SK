# Quickstart & Validation Guide: Happy Hour Multi-Select

Ce guide décrit les étapes de validation pour vérifier le fonctionnement de la sélection groupée et de l'administration tactile Happy Hour.

## 1. Prérequis

- Serveur API démarré sur `http://localhost:5000`
- Utilisateur authentifié en caisse avec les droits d'administration (ex: Alexandre Dupont PIN `1234` ou `9999`)

## 2. Validation Manuelle Tactile

### Scénario A : Configuration groupée de Familles (Catégories)
1. Ouvrir l'application Web POS : `http://localhost:5000`
2. Aller dans l'onglet **⚙️ Paramétrage** > sous-onglet **Happy Hour**.
3. Dans la section de configuration du planning actif, basculer sur l'onglet **🏷️ Familles éligibles**.
4. Cocher au moins 2 catégories (ex: "Boissons" et "Desserts").
5. Saisir `20` dans le champ Remise (%).
6. Cliquer sur **Appliquer à la sélection**.
7. Vérifier que les 2 familles s'affichent dans la liste des règles actives avec le badge `-20%`.

### Scénario B : Sélection multiple d'Articles avec Prix Fixe
1. Basculer sur l'onglet **🍺 Articles spécifiques**.
2. Filtrer par une catégorie ou utiliser la recherche pour afficher les bières/cocktails.
3. Cocher 3 articles ou cliquer sur **Tout sélectionner**.
4. Choisir le mode **Prix Fixe (€)** et saisir `5.00`.
5. Cliquer sur **Appliquer aux articles sélectionnés**.
6. Constater l'apparition immédiate des 3 articles avec leur prix fixe `5,00 €` dans la liste récapitulative.

### Scénario C : Suppression groupée
1. Dans la liste récapitulative des règles d'articles, cocher 2 règles.
2. Cliquer sur **Supprimer la sélection**.
3. Vérifier la disparition instantanée des 2 règles.

## 3. Tests Automatisés

```bash
# Tests unitaires du moteur batch (.NET)
dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "Batch"

# Tests End-to-End Playwright
cd tests/RestaurantPos.Web.E2ETests
npx playwright test tests/happy-hour-multiselect.spec.ts
```
