# Phase 0 Research: Création Directe de Table depuis la Vue Tables

**Feature**: `010-create-table-floorplan`  
**Date**: 2026-08-30

---

## 1. Analyse des besoins & Contexte Opérationnel

### Problème Initial
Dans les logiciels de caisse traditionnels, l'ajout d'une table d'appoint requiert d'ouvrir un module d'administration séparé, ce qui paralyse les serveurs lors de l'arrivée imprévue de clients nécessitant l'installation immédiate d'une table supplémentaire (ex: « T9 », « Terrasse 1 », « Mange-Debout 2 »).

### Objectif
Permettre l'ajout d'une table directement depuis le plan de salle tactile en 1 clic sur `➕ Nouvelle Table`, avec ouverture d'une modale simple demandant le numéro de table et la capacité (avec boutons de présélection 2p, 4p, 6p, 8p).

---

## 2. Décisions d'Architecture

### Décision 1 : Clé primaire et normalisation du numéro de table
- **Choix** : Utiliser `TableNumber` comme clé d'identification unique, normalisée en majuscules (`Trim().ToUpperInvariant()`).
- **Justification** : `TableNumber` est l'identifiant métier visible sur les tickets de caisse, les bons de commande KDS et le plan de salle.
- **Alternatives rejetées** : Utilisation d'un ID numérique auto-incrémenté sans unicité du nom métier.

### Décision 2 : Ergonomie tactile sans friction (Touch-First)
- **Choix** : Fournir des boutons rapides de capacité `[2p] [4p] [6p] [8p]` en complément d'un champ numérique avec incrémenteur.
- **Justification** : 90% des tables de restaurant ont une capacité standard de 2, 4, 6 ou 8 couverts. La sélection par bouton rapide évite d'ouvrir le clavier système.
- **Alternatives rejetées** : Formulaire multi-étapes.

### Décision 3 : Traitement de l'unicité et idempotence
- **Choix** : Vérifier l'existence en base de données :
  - Si la table existe déjà au statut `Free`, renvoyer l'entité existante sans erreur bloquante.
  - Si la table existe déjà au statut `Occupied`, avertir l'opérateur que la table est actuellement en service.
- **Justification** : Évite les blocages de caisse tout en garantissant la cohérence des données.

---

## 3. Matrice de validation des choix techniques

| Composant | Solution retenue | Avantages |
|---|---|---|
| **API Backend** | `POST /api/tables` (Minimal API) | Haute performance (< 10ms), payload JSON léger |
| **Service Application** | `ITableManagementService.CreateTableAsync` | Totalement testable unitairement avec mocks |
| **Frontend Web** | Modale CSS Glassmorphism + Fetch API | Rendu fluide sans rechargement de page (< 50ms) |
| **Frontend MAUI** | `FloorPlanViewModel.AddTableAsync` + CommunityToolkit MVVM | Retour haptique tactile et liaison de données réactive |
