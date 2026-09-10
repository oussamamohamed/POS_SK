# Feature Specification: Cross-Platform Tactile Frontend (iOS, Android & Windows)

**Feature Branch**: `002-cross-platform-frontend`

**Created**: 2026-08-15

**Status**: Draft

**Input**: User description: "je veux que le frontend soit multi platforme pas seulement IOS"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Unified Cross-Platform Tactile POS Experience (Priority: P1)

Restaurant operators, waitstaff, and cashiers must be able to use the tactile POS system across a heterogeneous fleet of devices—including Apple iPads (iPadOS), Android tablets and dedicated Android mobile POS hardware (e.g., Sunmi, Pax, Elo), and Windows all-in-one touchscreen terminals (WinUI / Windows 11)—with 100% consistent touch ergonomics, intuitive layouts, and identical order workflows.

**Why this priority**: Restaurants frequently operate mixed hardware environments (e.g., Windows fixed counter terminal at the main bar, iPad tablets in the dining room, and rugged Android mobile terminals on outdoor terraces). The software must provide a unified, seamless experience without platform lock-in.

**Independent Test**: Deploy the application to an iPad, an Android tablet, and a Windows touchscreen PC. Execute the entire order-taking lifecycle (item selection, modifier customization, table seating, bill splitting, and payment) on each platform and verify identical layout usability, minimum 54x54 pt touch targets, custom on-screen numeric keypads, and sub-50ms input responsiveness.

**Acceptance Scenarios**:

1. **Given** an operator working on an iPad, an Android tablet, or a Windows touch terminal, **When** taking customer orders, **Then** the interface provides large responsive touch targets ($\ge 54\times 54\text{ pt}$), a fixed custom on-screen keypad without summoning the platform virtual keyboard, and instant visual/haptic feedback.
2. **Given** various display sizes ranging from 8" mobile handhelds to 15.6"+ counter touchscreens, **When** the application renders, **Then** the UI layout dynamically adapts its grid columns and basket proportions while preserving core ergonomics and readability.
3. **Given** an operator navigating dark-themed indoor dining or high-glare outdoor terraces, **When** adjusting display settings, **Then** the application applies high-contrast light and dark themes consistently across iOS, Android, and Windows.

---

### User Story 2 - Platform-Agnostic Local-First Persistence & Offline Synchronization (Priority: P1)

Each terminal device, regardless of whether it is running on iOS, Android, or Windows, must operate with complete local autonomy during network outages by storing its append-only transaction journal, local catalog cache, and Outbox sync queue in its platform-sandboxed storage, seamlessly synchronizing transactions to the central backend upon reconnection.

**Why this priority**: Hardware diversity must never compromise offline operational continuity or sales data integrity.

**Independent Test**: Disconnect network access on an iPad, an Android device, and a Windows terminal. Enter and finalize multiple orders and cash payments offline on each device, verify local database persistence, restore network access, and verify that all transactions synchronize to the central backend with UUIDv7 deduplication and zero lost records.

**Acceptance Scenarios**:

1. **Given** an offline state on any supported platform (iOS, Android, Windows), **When** orders and payments are processed, **Then** data is written securely to the local embedded database in the platform's sandboxed data directory (`AppDataDirectory`) with monotonic sequence numbers.
2. **Given** restored network connectivity on any client platform, **When** the Outbox synchronization engine executes, **Then** all buffered transactions are transmitted in chronological order to the central backend with full idempotency guarantees.

---

### User Story 3 - Cross-Platform Hardware Drivers & Zero-Configuration Discovery (Priority: P2)

POS terminals across all platforms must discover available network receipt/kitchen thermal printers and the central server on the local network automatically (mDNS / Bonjour) and print tickets or kick cash drawers directly over raw TCP sockets without requiring OS-specific printer spoolers or manual driver installations.

**Why this priority**: Seamless hardware communication across Android, iOS, and Windows eliminates configuration overhead and ensures uniform peripheral interoperability.

**Independent Test**: Trigger network discovery and test print operations from iOS, Android, and Windows clients to a shared network ESC/POS printer, verifying identical thermal receipt output, correct 80mm/58mm line formatting, and automated drawer kick triggers.

**Acceptance Scenarios**:

1. **Given** a newly installed terminal on iOS, Android, or Windows, **When** the application starts on the local network, **Then** it automatically discovers available network printers and the central server via local mDNS/DNS-SD discovery.
2. **Given** a receipt print or cash drawer kick request on any supported platform, **When** triggered, **Then** the client sends raw ESC/POS byte streams over standard TCP sockets (port 9100), completing the operation in under 500 milliseconds.

---

### Edge Cases

- **Screen Resolution & DPI Variations**: Handling diverse screen sizes (from 800x1280 mobile screens to 1920x1080 counter monitors and 4K displays) with adaptive scaling and touch target preservation.
- **Platform-Specific Network Permissions**: Gracefully requesting and handling local network access permissions on iOS (`NSLocalNetworkUsageDescription`), Android (`NEARBY_WIFI_DEVICES`, `CHANGE_WIFI_MULTICAST_STATE`), and Windows firewall boundaries.
- **Platform Background Execution Limits**: Managing outbox sync queues when mobile operating systems (iOS / Android) enter background or doze states.
- **Peripheral Connectivity Fallbacks**: Supporting both network TCP sockets and optional platform-native serial/USB/Bluetooth printer bridges where raw network sockets are unavailable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support deployment on Apple iPadOS 17+ / iOS 17+, Android 10+ (API level 29+), and Windows 10/11 (x64 / ARM64) using a unified .NET MAUI codebase.
- **FR-002**: System MUST render fluid, responsive touch interfaces adapting dynamically across 7"-8" handhelds, 10"-12" tablets, and 15"-22" counter displays while strictly maintaining $\ge 54\times 54\text{ pt}$ touch targets.
- **FR-003**: System MUST provide custom on-screen numeric keypads and tactile gesture handlers that prevent native operating system virtual keyboards from popping up on all platforms.
- **FR-004**: System MUST execute identical core domain logic, money cent arithmetic, tax calculations, and CQRS command handlers on all platforms via shared .NET 9 assemblies.
- **FR-005**: System MUST implement local-first SQLite persistence located in the respective platform application sandbox (`FileSystem.AppDataDirectory`) on iOS, Android, and Windows.
- **FR-006**: System MUST discover local network thermal printers and central servers using cross-platform mDNS / Bonjour discovery protocols.
- **FR-007**: System MUST communicate directly with ESC/POS thermal printers (port 9100) and trigger RJ11 cash drawer pulses via cross-platform raw TCP socket connections.
- **FR-008**: System MUST support real-time WebSocket / SignalR communication for table states and kitchen order routing across all platforms with automated reconnection.
- **FR-009**: System MUST support platform-appropriate enterprise deployment mechanisms: Apple Business Manager / MDM for iOS, Google Play Private / APK sideloading for Android, and MSIX / Single-file deployment for Windows.

### Key Entities *(include if feature involves data)*

- **DevicePlatformProfile**: Captures the client OS type (`iOS`, `Android`, `Windows`), screen dimensions, pixel density, and device station role.
- **PlatformStorageConfiguration**: Maps platform-specific secure sandboxed paths for local database, cache, and cryptographic logs.
- **PlatformNetworkAdapter**: Encapsulates network interface discovery and local socket communication capabilities per platform.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of core POS capabilities (ordering, seating, bill splitting, cash/card payment, and offline sync) operate with identical workflows and zero functional divergence across iOS, Android, and Windows.
- **SC-002**: User touch inputs deliver visual or haptic feedback in under 50 milliseconds on all supported target platforms.
- **SC-003**: Automated unit and component test suites achieve 100% pass rate across iOS, Android, and Windows platform targets.
- **SC-004**: Network receipt printing and drawer kick complete in under 500 milliseconds from any iOS, Android, or Windows terminal.
- **SC-005**: Dynamic UI layout adjusts cleanly across screen aspect ratios from 4:3 (iPad) to 16:9 / 16:10 (Android/Windows) without clipped text or inaccessible buttons.

## Assumptions

- Target Android devices have Google Play Services or standard Android networking capabilities with multi-cast support.
- Target Windows touch machines support Windows App SDK / WinUI 3 (Windows 10 version 1809+ or Windows 11).
- All client platforms have access to local Wi-Fi / Ethernet for LAN communication with printers and central backend.
