# Data Model: Cross-Platform Tactile Frontend (iOS, Android & Windows)

**Feature**: `002-cross-platform-frontend`
**Date**: 2026-08-15
**Status**: Complete

## 1. Platform & Device Configuration Models

### DevicePlatformProfile
Encapsulates runtime platform detection and hardware form-factor metrics.

| Field | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `DeviceId` | `string` | Unique per installation | Unique hardware / app sandbox installation identifier |
| `PlatformType` | `PlatformType` | Enum (`iOS = 0`, `Android = 1`, `Windows = 2`, `MacCatalyst = 3`) | Operating system runtime |
| `DeviceIdiom` | `DeviceIdiomType` | Enum (`Phone = 0`, `Tablet = 1`, `Desktop = 2`) | Form factor classification |
| `ScreenClass` | `ScreenClassType` | Enum (`CompactHandheld = 0`, `StandardTablet = 1`, `LargeCounterAIO = 2`) | Screen layout breakpoint classification |
| `ScreenWidthDip` | `double` | $> 0$ | Display width in density-independent points/pixels |
| `ScreenHeightDip` | `double` | $> 0$ | Display height in density-independent points/pixels |
| `DisplayDensity` | `double` | $> 0$ | Pixel scaling density factor (Retina / hdpi / xxhdpi) |
| `OsVersion` | `string` | e.g., `iOS 17.5`, `Android 14`, `Windows 11 23H2` | Native OS version |
| `AppVersion` | `string` | Semantic version string | POS client application build version |

---

### DisplayBreakpointConfig
Defines UI layout rules dynamically applied based on `ScreenClass`.

| Screen Class | Min Width (dp) | Grid Columns | Touch Target Size | Layout Strategy |
| :--- | :--- | :--- | :--- | :--- |
| **CompactHandheld** | $< 700\text{ dp}$ | 2 - 3 columns | $54\times 54\text{ pt}$ | Single-column view with slide-over cart drawer |
| **StandardTablet** | $700 - 1100\text{ dp}$ | 4 - 5 columns | $58\times 58\text{ pt}$ | Split view (60% catalog grid, 40% cart & numpad) |
| **LargeCounterAIO** | $> 1100\text{ dp}$ | 6 - 8 columns | $68\times 68\text{ pt}$ | Triple split view (catalog, modifiers, active tables) |

---

### HardwarePeripheralBinding
Maps physical hardware devices to a cross-platform POS station.

| Field | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `Guid` (UUIDv7) | Primary Key | Binding record identifier |
| `PeripheralRole` | `PeripheralRoleType` | Enum (`ReceiptPrinter = 0`, `KitchenPrinter = 1`, `CashDrawer = 2`, `PaymentTerminal = 3`) | Assigned hardware role |
| `ConnectionType` | `ConnectionType` | Enum (`NetworkTcp = 0`, `Bluetooth = 1`, `UsbSerial = 2`) | Communication protocol |
| `TargetEndpoint` | `string` | IP address, hostname, or Bluetooth MAC | Device network target (e.g. `192.168.1.150`) |
| `Port` | `int` | Default `9100` for TCP | Port for socket communication |
| `PaperWidthMm` | `int` | `80` or `58` | Thermal paper roll dimension |
| `IsDefault` | `bool` | Default `false` | Default device for station role |

---

## 2. Cross-Platform Storage Resolution Model

```text
┌────────────────────────────────────────────────────────────────────────┐
│ .NET MAUI FileSystem.AppDataDirectory Resolution                      │
├─────────────────┬──────────────────────────────────────────────────────┤
│ iOS / iPadOS    │ Library/Application Support/ (Protected App Sandbox) │
├─────────────────┼──────────────────────────────────────────────────────┤
│ Android         │ /data/user/0/<package>/files/ (Internal Data Storage)│
├─────────────────┼──────────────────────────────────────────────────────┤
│ Windows (WinUI) │ %LOCALAPPDATA%\Packages\<PackageId>\LocalState\      │
└─────────────────┴──────────────────────────────────────────────────────┘
```
