# Research & Architecture Decisions: Cross-Platform Tactile Frontend (iOS, Android & Windows)

**Feature**: `002-cross-platform-frontend`
**Date**: 2026-08-15
**Status**: Completed

## 1. Unified .NET MAUI Multi-Targeting Architecture

### Decision
Configure the client project `RestaurantPos.Client.Maui` as a multi-targeted .NET MAUI application targeting:
- `net9.0-ios` (iOS/iPadOS 17+)
- `net9.0-android` (Android 10+, API Level 29+)
- `net9.0-windows10.0.19041.0` (Windows App SDK / WinUI 3 on Windows 10/11)

### Rationale
- **100% Shared UI & Logic**: 95%+ of UI views (XAML), custom controls (touch numpad, basket layout, product tile grids), view models (`CommunityToolkit.Mvvm`), and local services are shared across all platforms without duplication.
- **Direct Domain Integration**: Client references shared C# `RestaurantPos.Domain` and `RestaurantPos.Application` libraries directly with zero serialization overhead or translation layers.
- **Platform Specifics Isolated**: OS-specific permissions (e.g., iOS `Info.plist`, Android `AndroidManifest.xml`, Windows `Package.appxmanifest`) and native lifecycle handlers are encapsulated within `Platforms/` folders.

### Alternatives Considered
- **Flutter**: Evaluated for cross-platform UI, but rejected because Flutter requires Dart, making it impossible to share the extensive .NET C# Domain, CQRS handlers, and EF Core data models with the backend.
- **React Native**: Rejected due to JavaScript thread execution overhead, latency on rapid touch gestures, and dual-language maintenance.
- **Separate Native Apps (Swift + Kotlin + C#)**: Rejected due to 3x maintenance costs and high risk of business rule divergence (e.g. tax calculations, split bill logic).

---

## 2. Adaptive Responsive Layout & Touch Ergonomics

### Decision
Implement a responsive layout engine in XAML using `VisualStateManager`, custom breakpoint behaviors, and `Microsoft.Maui.Graphics` that adapts dynamically:
- **Handheld Compact Mode (< 9" screens, e.g., iPad Mini, Android Handhelds)**: 2-3 column product grid, single-screen order basket with slide-over drawer.
- **Standard Tablet Mode (10"-12" screens, e.g., iPad Air/Pro, Samsung Galaxy Tab)**: 4-5 column product grid, permanent side-by-side order cart and fixed numeric keypad.
- **Large Counter Mode (15"-22" screens, e.g., Windows Touch AIO, Elo Touch)**: 6+ column product grid, expanded modifier panels, and persistent table status split view.

### Rationale
- Strictly preserves minimum touch target sizes ($\ge 54\times 54\text{ pt}$, up to $68\times 68\text{ pt}$ for rush items) regardless of physical screen size or DPI.
- Eliminates the need for multiple distinct UI codebases for mobile vs. counter stations.

---

## 3. Cross-Platform Local Storage & Sandboxed SQLite

### Decision
Use Microsoft.Maui.Storage `FileSystem.AppDataDirectory` to anchor the local SQLite database across all platforms:
- **iOS/iPadOS**: Sandboxed in `Application Support` directory (backed up and isolated).
- **Android**: Sandboxed in internal app storage `/data/data/<package>/files/`.
- **Windows**: Sandboxed in `%LOCALAPPDATA%\Packages\<PackageId>\LocalState\`.

### Rationale
- Uniform path resolution through standard .NET MAUI abstractions.
- EF Core SQLite provides identical ACID transactional integrity, outbox queueing, and sub-millisecond local reads/writes across iOS, Android, and Windows.

---

## 4. Cross-Platform Hardware Drivers & Network Sockets

### Decision
Implement `IPrinterService` and `INetworkDiscoveryService` using standard .NET BCL networking:
- **Thermal Printing & Cash Drawer**: Standard `System.Net.Sockets.TcpClient` over raw TCP port 9100. Byte commands (`ESC/POS`) execute identically on iOS, Android, and Windows without OS printer spoolers.
- **mDNS / Bonjour Discovery**: Cross-platform Zeroconf / DNS-SD broadcast scanning to discover local servers (`_pos-server._tcp`) and network printers (`_printer._tcp`).

### Rationale
- Zero third-party hardware vendor SDK locks (works uniformly with Epson, Star, Munbyn, Bixolon, Citizen).
- Bypasses OS spooler latencies, achieving $< 500\text{ms}$ ticket printing from any OS.
