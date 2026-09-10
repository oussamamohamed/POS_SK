# Implementation Plan: Création Directe de Table depuis la Vue Tables

**Branch**: `010-create-table-floorplan` | **Date**: 2026-08-30 | **Spec**: [specs/010-create-table-floorplan/spec.md](spec.md)

---

## Summary

Permettre aux serveurs et gérants de créer rapidement une nouvelle table directement depuis le plan de salle tactile (Vue Tables) via une modale légère et ergonomique (`➕ Nouvelle Table`), enregistrer la table dans la base de données centrale et le cache local SQLite, et afficher immédiatement la nouvelle carte de table au statut `Libre` prête à la prise de commande.

---

## Technical Context

**Language/Version**: C# 13 / .NET 9.0  
**Primary Dependencies**: ASP.NET Core Minimal APIs, Entity Framework Core 9.0, .NET MAUI / CommunityToolkit.Mvvm, Vanilla HTML5 / Modern CSS  
**Storage**: SQLite local (`pos_offline_cache.db` / `AppDbContext`) via EF Core  
**Testing**: xUnit, FluentAssertions, Moq  
**Target Platform**: iPadOS 17+ / Windows 11 / Web Browser  
**Project Type**: Clean Architecture Full-Stack POS  
**Performance Goals**: Rendu de la nouvelle carte sur le plan de salle < 100ms  
**Constraints**: Nom de table unique insensible à la casse, validation capacité (1 à 30 couverts)  

---

## Constitution Check

*GATE: All Passed.*

| Principe Constitutionnel | Statut | Justification |
|---|---|---|
| **I. Touch-First Ergonomics** | ✅ Pass | Boutons de capacité rapide 2p, 4p, 6p, 8p, retour haptique |
| **II. Clean Architecture** | ✅ Pass | Interface `ITableManagementService` dans `Application`, implémentation dans `Infrastructure`, endpoint dans `Api` |
| **III. Transactional Integrity** | ✅ Pass | Persistance unifiée `DiningTable` |
| **IV. Real-Time Sync** | ✅ Pass | Rafraîchissement automatique de la grille de salle |
| **V. Test-First** | ✅ Pass | Tests unitaires sur `CreateTableAsync` |
