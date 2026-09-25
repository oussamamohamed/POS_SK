# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Restaurant POS for iPad with French NF525 fiscal compliance. Three pieces:

- **`src/`** — .NET 9 ASP.NET Core backend (`RestaurantPos.slnx`): Domain / Application / Infrastructure / Api.
- **`src/RestaurantPos.Api/wwwroot/`** — vanilla JS touch web client (`app.js`, single large file), served as static files by the API.
- **`ios/`** — native SwiftUI iPad client (Swift 6, iPadOS 17+) at feature parity with the web client. See `ios/README.md`.

The .NET MAUI client was removed. `src/RestaurantPos.Client.Maui` and `tests/*Maui*` are leftover `bin/obj` folders only. `PLAN.md` and `.specify/memory/constitution.md` still mention MAUI, MediatR/CQRS, and PostgreSQL. The code uses none of these: it uses plain services and SQLite. Treat the code as the source of truth.

Project docs, specs (`specs/`), comments, and UI strings are mostly in French.

## Commands

### Backend (.NET)

```bash
dotnet build RestaurantPos.slnx
dotnet test RestaurantPos.slnx
dotnet test tests/RestaurantPos.Api.Tests --filter "FullyQualifiedName~CheckoutE2ETests"   # single class/test

# Run the API locally (port 5000 is taken by AirPlay on macOS; iOS app defaults to 5080)
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5080 \
Jwt__Secret="SuperSecretKeyForRestaurantPosSystemThatIsAtLeast32BytesLong!" \
dotnet run --project src/RestaurantPos.Api
```

`Directory.Build.props` sets `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` + `AnalysisLevel=latest-recommended`, so any analyzer or `.editorconfig` style violation fails the build. Run `dotnet format RestaurantPos.slnx` before building if unsure.

Demo PINs (seeded): `1234` manager, `2468` waiter, `5678` kitchen, `9999` admin. `dotnet run --project src/RestaurantPos.Api -- --seed-test-data` seeds richer data (see also `scripts/seed_production_test_data.sh`).

### iOS

```bash
cd ios && xcodegen generate      # .xcodeproj is generated from project.yml — edit project.yml, never the .xcodeproj
./scripts/test.sh unit           # PosKit Swift Testing, ~1 s
./scripts/test.sh ui             # XCUITest on iPad simulator, ~8 min
POS_API_URL=http://localhost:5080 ./scripts/test.sh contract   # live flow against the real .NET API
cd Packages/PosKit && swift test --filter <TestName>           # single unit test
```

Test-only launch args: `-UITestMode` (in-memory backend, no server), `-UITestPin 1234`, `-UITestSection floor|kitchen|fiscal|admin`, `-UITestHappyHour`.

### Web E2E (Playwright)

```bash
cd tests/RestaurantPos.Web.E2ETests
POS_WEB_URL=http://localhost:5080 npm test                  # default baseURL is :5000
npx playwright test tests/split-bill.spec.ts --project='iPad Pro 11'   # single spec
```

Requires the API running. Tests run serially (`workers: 1`) because they share server state.

## Backend architecture

- **Layering:** `Domain` (entities, `Money`, enums) ← `Application` (service interfaces in `Common/Interfaces`, DTOs) ← `Infrastructure` (EF Core `AppDbContext`, service implementations in `Services/`, `Security/`, `Printing/`) ← `Api`. There is no MediatR. Endpoints are Minimal API groups in `Api/Endpoints/*Endpoints.cs` that call the injected `I*Service` interfaces directly. All DI registration happens in `Program.cs`.
- **Schema management:** there are no EF migrations. `Program.cs` calls `EnsureCreated()` and then runs a list of idempotent `try { ALTER TABLE ... / CREATE TABLE IF NOT EXISTS ... } catch { }` statements for columns and tables added later. When you add a column or entity, add the matching raw SQL there too. Otherwise existing SQLite databases won't get the change. The `Testing` environment uses the EF InMemory provider and skips this block.
- **Money:** amounts are integer cents (`Domain/ValueObjects/Money.cs`). Never use `float`/`double` for money. The iOS `PosKit` rounds the same way as .NET's `Money.FromDecimal` and uses the same split-to-the-cent rules. Any change to rounding must be made on both sides.
- **NF525 fiscal chain:** `Infrastructure/Services/NF525FiscalAuditService.cs` chains receipts with SHA-256 over `previousHash|terminalId|sequenceNumber|amountCents|timestampUtc:O|taxBreakdownJson`, starting from `GenesisHash`. Changing field order, formatting, or the tax-breakdown serialization breaks verification of existing chains. Fiscal receipts and journal entries are append-only: corrections go through voids/credit notes, never updates.
- **Realtime:** SignalR hubs at `/hubs/pos` (`PosHub`, typed `IPosHubClient`) and `/hubs/kitchen` (`KitchenHub`). Endpoints broadcast through `IHubContext` after mutations (e.g. `OnGridLayoutUpdated`). Both the web and iOS clients subscribe to these event names, so renaming one breaks both clients.
- **Auth:** operator PIN login → JWT (`Security/`), with PIN rate limiting. `Jwt:Secret` is required outside Development. `NetworkDiscoveryBeaconService` is a hosted service that announces the server on the LAN.

## Cross-client contract

The web client (`app.js`), the iOS `PosKit/Models` DTOs, and the .NET DTOs/entities are kept in sync by hand. `ios/Packages/PosKit/Tests/PosKitTests/Fixtures` holds captured real API responses that the Swift contract tests decode. When you change an API response shape, update those fixtures and the Swift models. Known quirk: order destination enum is `Takeaway = 0`, `EatIn = 1`, and table orders default to takeaway on the server, so clients must set eat-in explicitly.

## iOS architecture (summary)

All logic lives in the `PosKit` Swift package (Core math, Codable models, `PosAPI` protocol + `HTTPPosAPI`, minimal SignalR client, `@Observable` stores). SwiftUI views in `RestaurantPOS/` only talk to stores, never to the network. `InMemoryPosAPI` replaces HTTP in tests and in `-UITestMode`. The server API only *appends* order lines, so the iPad keeps a local draft ticket and sends it only on kitchen send, payment, discount, hold, transfer, or table change.

## Spec workflow

Features are specified with spec-kit (`.specify/`, `specs/NNN-*/` with spec/plan/tasks). The speckit skills are in `.agents/skills/`.
