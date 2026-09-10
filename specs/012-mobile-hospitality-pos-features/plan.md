# Implementation Plan: Fonctions Populaires d'Encaissement Mobile & Tablette (Restauration & Hôtellerie)

**Branch**: `012-mobile-hospitality-pos-features` | **Date**: 2026-09-02 | **Spec**: [specs/012-mobile-hospitality-pos-features/spec.md](spec.md)

**Input**: Feature specification from `specs/012-mobile-hospitality-pos-features/spec.md`

---

## Summary

Implémenter les 5 fonctionnalités les plus populaires et à haute valeur ajoutée pour les caisses sur tablettes et terminaux mobiles en restauration et hôtellerie :
1. **Transfert & Fusion atomiques de tables** avec mise à jour temps réel du plan de salle.
2. **Gestion des Remises et Articles offerts (Comps)** avec traçabilité fiscale stricte NF525.
3. **Temps de service et signal tactile de réclame cuisine (« Direct », « Suite », « Réclamer Suite »)** via SignalR et impression tickets.
4. **Encaissement au bout de table avec pourboires tactiles suggérés et division équilibrée au centime**.
5. **Règlement par Facturation sur Chambre d'Hôtel / Folio PMS** avec signature tactile sur tablette.

---

## Technical Context

**Language/Version**: C# 13 / .NET 9.0  
**Primary Dependencies**: ASP.NET Core Minimal APIs, Entity Framework Core 9.0, SignalR Core, CommunityToolkit.Mvvm, Vanilla HTML5 Canvas / Modern CSS  
**Storage**: SQLite (`AppDbContext`) via EF Core avec journalisation immuable  
**Testing**: xUnit, FluentAssertions, Moq  
**Target Platform**: iPadOS 17+ (Apple iPad Ecosystem) / Android Tablets / Web Mobile  
**Project Type**: Clean Architecture POS  
**Performance Goals**: Transfert de table < 100ms, Signal de réclame cuisine < 200ms, Rendu des modales tactiles < 50ms  
**Constraints**: Conformité NF525 pour les remises/offerts, zéro écart d'arrondi sur les splits, validation rigoureuse des signatures et chambres d'hôtel  
**Scale/Scope**: Prise en charge simultanée de 50 tables, 500 articles, flux KDS multi-postes, gestion de 100 chambres  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principe Constitutionnel | Statut | Justification / Alignement |
|---|---|---|
| **I. Touch-First Ergonomics & iPadOS** | ✅ Pass | Cibles tactiles ≥ 54x54 pt (et ≥ 68x68 pt pour les boutons rush « Réclamer Suite » et « Transfert »), pavé de pourboires direct, zone de signature tactile fluide sans clavier virtuel perturbateur |
| **II. Clean Architecture & Centralized Backend** | ✅ Pass | Séparation stricte entre Domain (`HotelRoomResident`, `RoomFolioCharge`, `OrderDiscount`), Application (`IOrderDiscountService`, `IRoomBillingService`), Infrastructure et Présentation |
| **III. Transactional Integrity & Offline-First** | ✅ Pass | Persistance locale SQLite, génération d'UUIDv7, journalisation immuable des gestes commerciaux et fusions |
| **IV. Real-Time Sync & Hardware Abstraction** | ✅ Pass | Hub SignalR (`/hubs/kitchen`) pour la diffusion instantanée de l'ordre de réclame suite et abstraction d'impression ESC/POS |
| **V. Test-First & Immutabilité Fiscale (NF525)** | ✅ Pass | Utilisation systématique de `Money` en centimes entiers, interdiction des suppressions silencieuses, audit complet des remises |

---

## Project Structure

### Documentation (this feature)

```text
specs/012-mobile-hospitality-pos-features/
├── plan.md              # Ce document
├── research.md          # Analyse des flux tactiles et décisions d'architecture
├── data-model.md        # Énumérations, entités, DTOs et modèles de données
├── quickstart.md        # Guide de validation pas à pas
├── contracts/           # Schémas d'API, TypeScript et interfaces C#
│   ├── mobile-hospitality-api.json
│   ├── hospitality-contracts.ts
│   └── IHospitalityServices.cs
└── checklists/
    └── requirements.md  # Checklist qualité de la spécification
```

### Source Code (repository root)

```text
src/
├── RestaurantPos.Domain/
│   ├── Entities/ (Order, OrderItem, FiscalReceipt, RoomFolioCharge, HotelRoomResident, TableTransferLog)
│   └── ValueObjects/ (Money, TaxBreakdownItem)
├── RestaurantPos.Application/
│   └── Common/Interfaces/ (ITableManagementService, IOrderDiscountService, IRoomBillingService, ICheckoutPaymentService)
├── RestaurantPos.Infrastructure/
│   ├── Persistence/ (AppDbContext)
│   └── Services/ (TableManagementService, OrderDiscountService, RoomBillingService, CheckoutPaymentService)
├── RestaurantPos.Api/
    ├── Program.cs
    ├── Hubs/ (KitchenHub)
    └── wwwroot/
        ├── index.html
        ├── styles.css
        └── app.js

tests/
├── RestaurantPos.Domain.Tests/
├── RestaurantPos.Infrastructure.Tests/
│   └── HospitalityFeaturesTests.cs
└── RestaurantPos.Client.Maui.Tests/
```

**Structure Decision**: Extension naturelle de la Clean Architecture existante avec services spécialisés et endpoints Minimal APIs dédiés.

---

## Complexity Tracking

*Aucune dérogation constitutionnelle requise.*
