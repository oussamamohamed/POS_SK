# Implementation Plan: Cross-Platform Tactile Frontend (iOS, Android & Windows)

**Branch**: `002-cross-platform-frontend` | **Date**: 2026-08-15 | **Spec**: [specs/002-cross-platform-frontend/spec.md](spec.md)

**Input**: Feature specification from `specs/002-cross-platform-frontend/spec.md`

## Summary

Expand the tactile client application (`RestaurantPos.Client.Maui`) into a full cross-platform frontend supporting Apple iPadOS/iOS 17+, Android 10+ (API 29+), and Windows 10/11 (WinUI 3). The single unified .NET MAUI codebase ensures 100% shared UI views, custom touch keypad controls, view models, and domain business calculations while providing responsive layout adaptations across compact handhelds, standard tablets, and large counter touchscreen terminals.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (Multi-targeting `net9.0-ios`, `net9.0-android`, `net9.0-windows10.0.19041.0`)

**Primary Dependencies**:
- Client Framework: `.NET MAUI`, `CommunityToolkit.Mvvm` (8.x), `Microsoft.Maui.Graphics`
- Local Persistence: `Microsoft.EntityFrameworkCore.Sqlite` (using `FileSystem.AppDataDirectory`)
- Networking & Sync: `Microsoft.AspNetCore.SignalR.Client`, `System.Net.Sockets`

**Storage**: Local sandboxed SQLite database on each client platform:
- iOS: `Library/Application Support/`
- Android: `/data/user/0/<package>/files/`
- Windows: `%LOCALAPPDATA%\Packages\<PackageId>\LocalState\`

**Testing**: `xUnit`, `FluentAssertions`, `Moq`, .NET MAUI Device / Component Test Runners

**Target Platform**: 
- Apple iPadOS 17+ & iOS 17+
- Android 10+ (API 29+) (Tablets, Sunmi / Pax / Elo POS devices)
- Windows 10 (1809+) & Windows 11 (x64 / ARM64 Touchscreen AIO terminals)

**Project Type**: Cross-Platform Tactile Native Client Application (.NET MAUI)

**Performance Goals**:
- Tactile visual & haptic input acknowledgment: $< 50\text{ms}$ on all platforms
- UI cold start to PIN entry screen: $< 1.2\text{s}$
- Local-First offline transaction commit: $< 10\text{ms}$
- Thermal receipt socket printing dispatch: $< 500\text{ms}$

**Constraints**:
- Single unified C# / XAML codebase for 95%+ of client views and logic
- Zero system virtual pop-up keyboards during order entry across all OS platforms
- Strict adherence to minimum touch targets ($\ge 54\times 54\text{ pt}$) across all screen resolutions
- Zero compiler warnings across all target frameworks (`TreatWarningsAsErrors=true`)

## Constitution Check

*GATE: Evaluated and Passed across all Core Principles.*

| Principle | Status | Compliance Verification |
| :--- | :--- | :--- |
| **I. Touch-First Ergonomics & Tactile Design** | **PASS** | Cibles tactiles $\ge 54\times 54\text{ pt}$, pavé numérique personnalisé fixe, suppression du clavier système sur iOS, Android et Windows, support des thèmes clair/sombre. |
| **II. Clean Architecture & Centralized Multi-POS Backend** | **PASS** | `RestaurantPos.Client.Maui` consomme directement les bibliothèques partagées `Domain` et `Application` sans duplication. |
| **III. Transactional Integrity and Offline-First Operations** | **PASS** | SQLite local sandboxed via `FileSystem.AppDataDirectory`, journalisation *append-only* et synchronisation Outbox multiplateforme. |
| **IV. Hardware Driver Abstraction & Real-Time Sync** | **PASS** | Sockets TCP bruts port 9100 et mDNS fonctionnant de manière transverse sur iOS, Android et Windows. |
| **V. Test-First, Immutability & Fiscal Traceability** | **PASS** | Règles de calcul monétaire `Money` partagées et tests automatisés multi-cibles. |

## Project Structure

### Documentation (this feature)

```text
specs/002-cross-platform-frontend/
├── plan.md              # Implementation plan (/speckit-plan output)
├── research.md          # Multi-targeting decisions & layout strategy
├── data-model.md        # Device profiles, screen breakpoints & peripheral bindings
├── quickstart.md        # Multi-target build and verification guide
├── contracts/           # Cross-platform environment & discovery contracts
│   ├── platform-abstraction-contract.md
│   └── cross-platform-discovery-contract.md
├── checklists/
│   └── requirements.md  # Specification quality validation checklist
└── spec.md              # Feature specification
```

### Source Code Structure

```text
src/RestaurantPos.Client.Maui/
├── App.xaml / App.xaml.cs
├── AppShell.xaml / AppShell.xaml.cs
├── MauiProgram.cs                         # Multi-platform DI & service registration
│
├── Platforms/                             # Platform-specific configurations & permissions
│   ├── Android/                           # AndroidManifest.xml, MainActivity.cs
│   ├── iOS/                               # Info.plist, AppDelegate.cs
│   └── Windows/                           # Package.appxmanifest, App.xaml.cs
│
├── Controls/                              # Custom Touch-First Controls
│   ├── NumericKeypadView.xaml             # Integrated fixed on-screen numpad
│   ├── ProductTileButton.xaml             # Large touch targets with haptic feedback
│   └── ResponsiveGridContainer.cs         # Adaptive column layout by screen class
│
├── Views/                                 # Shared Tactile Pages (XAML)
│   ├── PinLockPage.xaml                   # Quick PIN authentication
│   ├── PosTerminalPage.xaml               # Order entry & dynamic basket
│   ├── SplitBillModal.xaml                # Split bill modal
│   └── FloorPlanPage.xaml                 # 2D table layout canvas
│
├── ViewModels/                            # MVVM ViewModels (CommunityToolkit.Mvvm)
│   ├── PinLockViewModel.cs
│   ├── PosTerminalViewModel.cs
│   └── FloorPlanViewModel.cs
│
├── Services/                              # Shared Client Services
│   ├── PlatformEnvironmentService.cs      # FileSystem paths, haptics, keep-awake
│   ├── CrossPlatformDiscoveryService.cs   # mDNS network scanner
│   ├── NetworkPrinterClient.cs            # Raw TCP socket ESC/POS driver
│   └── LocalSyncWorker.cs                 # Background Outbox synchronizer
│
└── Resources/                             # Shared Icons, Fonts, Styles
    ├── Styles/Styles.xaml                 # Colors, typography, touch target metrics
    └── AppIcon/
```

**Structure Decision**: Multi-targeted .NET MAUI project supporting iOS, Android, and Windows natively from a single shared project structure, maximizing code reuse and delivering a flawless tactile experience across all restaurant hardware.

## Complexity Tracking

*No unjustified complexity violations. .NET MAUI multi-targeting natively manages the compilation for iOS, Android, and Windows within a single project.*
