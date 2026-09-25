# Device Discovery & Pairing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every till that writes NF525 receipts (iPad or web) is paired with the server through a one-time back-office code and gets a server-assigned `terminalId`; iPads find the server over Bonjour.

**Architecture:** A new `Device` / `DevicePairingCode` pair of entities behind `IDeviceService`. `/api/devices/*` endpoints mint codes (manager only) and exchange a code for a device token. An endpoint filter (`RequireDeviceFilter`) guards the three receipt-writing routes, rejects unknown or revoked tokens with `401 {code:"device_not_paired"}`, and supplies the device's `TerminalId` to the handler instead of the request body. The UDP beacon is replaced by an mDNS advertiser. iOS stores the token in the Keychain and sends `X-Device-Token` on every call; the web client does the same from `localStorage`.

**Tech Stack:** .NET 9 minimal APIs, EF Core (SQLite + InMemory), `Makaretu.Dns.Multicast.New` 0.38.0, `QRCoder` 1.8.0, xUnit + FluentAssertions; Swift 6 / SwiftUI / `Network` / `VisionKit`, Swift Testing, XCUITest; vanilla JS, Playwright.

**Spec:** `docs/superpowers/specs/2026-09-25-device-discovery-pairing-design.md`

## Global Constraints

- Build must stay clean: `Directory.Build.props` sets `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `AnalysisLevel=latest-recommended`. Run `dotnet format RestaurantPos.slnx` then `dotnet build RestaurantPos.slnx` before every .NET commit.
- No EF migrations: every new table gets `CREATE TABLE IF NOT EXISTS` SQL in the idempotent block of `src/RestaurantPos.Api/Program.cs`.
- NF525 hash format (`previousHash|terminalId|sequenceNumber|amountCents|timestampUtc:O|taxBreakdownJson`) must not change. Chains are already per `TerminalId`.
- Pairing code: 8 chars from `ABCDEFGHJKMNPQRSTUVWXYZ23456789`, single use, valid 10 minutes, compared after `Trim().ToUpperInvariant()`.
- Device token: 32 random bytes (`RandomNumberGenerator`), base64url. Only SHA-256 hex hashes of codes and tokens are stored.
- Terminal IDs: `T01`, `T02`… assigned in pairing order, never reused (revoked rows stay).
- Header name: `X-Device-Token`. Error codes: `device_not_paired` (401), `pairing_code_invalid` (400), `rate_limited` (429).
- Gated routes (and only these): `POST /api/checkout/pay`, `POST /api/checkout/void/{receiptId}`, `POST /api/orders/counter/checkout`. X/Z, `latest-closure`, FEC, held orders stay as they are; `POS_MAIN_TERM` keeps its "all terminals" meaning for reports.
- `DeviceRole` values: `Caisse`, `Serveur`, `Cuisine`, `BackOffice`; serialized as strings in DTOs.
- Bonjour service type: `_restaurantpos._tcp`; instance name = config `Discovery:ServerName`, default `Environment.MachineName`. Not started in the `Testing` environment.
- UI strings and code comments in French, matching the codebase.
- iOS `.xcodeproj` is generated: edit `ios/project.yml` only, then `cd ios && xcodegen generate`.

Deliberate simplifications versus the spec (all smaller, same behavior):
- The QR is returned inline as `qrPngBase64` in the pairing-code response instead of a separate `qr.png` endpoint (no code in URLs, no auth on `<img>` requests).
- Token lookup is by SHA-256 hash in a DB query rather than `FixedTimeEquals` over rows: the hash of a 256-bit random token leaks nothing usable through timing.
- The mDNS package is `Makaretu.Dns.Multicast.New` (maintained fork, net9.0); the original `Makaretu.Dns.Multicast` stopped at 0.27.0 in 2019.
- iOS keeps `serverURL` in `UserDefaults` as today; only the device identity (token, terminal, names) goes to the Keychain.
- No `devices.json` Swift fixture: the iPad never lists devices, so there is no Swift model to decode it into.

## Review Focus

1. Pairing code typed in lowercase or with surrounding spaces (web modal, iPad manual entry) → accepted. Pinned in Task 1 (`Pair_IsCaseAndWhitespaceInsensitive`).
2. The same code submitted twice at once (double tap, two iPads scanning one QR) → exactly one device is created. Pinned in Task 1 (`Pair_SameCodeInParallel_OnlyOneSucceeds`).
3. A client sending `terminalId: "POS_A"` in the body → ignored; receipt and void land in the device's own chain. Pinned in Task 2 (`Pay_IgnoresBodyTerminalId_AndStartsDeviceChainFromGenesis`, `Void_WritesIntoDeviceChain`).
4. An iPad revoked mid-shift tries to pay → goes back to the pairing screen, draft ticket kept. Pinned in Task 5 (`revokedDeviceOnPaymentUnpairsAndKeepsDraft`).
5. A device name containing HTML (`<img src=x onerror=…>`) in the back-office list → shown as text. Pinned in Task 7 (Playwright `device names are rendered as text`).

---

## File Map

| File | Status | Responsibility |
|---|---|---|
| `src/RestaurantPos.Domain/Enums/DeviceRole.cs` | new | Role enum |
| `src/RestaurantPos.Domain/Entities/Device.cs` | new | `Device`, `DevicePairingCode` |
| `src/RestaurantPos.Application/Common/Interfaces/IDeviceService.cs` | new | Service contract + result records |
| `src/RestaurantPos.Application/DTOs/DeviceDtos.cs` | new | HTTP DTOs |
| `src/RestaurantPos.Infrastructure/Services/DeviceService.cs` | new | Codes, pairing, auth, list, revoke |
| `src/RestaurantPos.Infrastructure/Persistence/AppDbContext.cs` | modify | DbSets + config |
| `src/RestaurantPos.Api/Endpoints/DeviceEndpoints.cs` | new | `/api/devices/*` |
| `src/RestaurantPos.Api/Endpoints/RequireDeviceFilter.cs` | new | Gate + device lookup |
| `src/RestaurantPos.Api/Endpoints/CheckoutEndpoints.cs` | modify | Gate pay/void, device terminal |
| `src/RestaurantPos.Api/Endpoints/CounterSaleEndpoints.cs` | modify | Gate counter checkout |
| `src/RestaurantPos.Api/Endpoints/SyncEndpoints.cs` | modify | Drop `DiscoveryPort` |
| `src/RestaurantPos.Api/Services/BonjourAdvertiserService.cs` | new | mDNS advertiser |
| `src/RestaurantPos.Api/Services/NetworkDiscoveryBeaconService.cs` | delete | UDP beacon |
| `src/RestaurantPos.Api/Program.cs` | modify | DI, SQL, mapping |
| `src/RestaurantPos.Api/RestaurantPos.Api.csproj` | modify | Packages |
| `tests/RestaurantPos.Infrastructure.Tests/DeviceServiceTests.cs` | new | Service tests |
| `tests/RestaurantPos.Api.Tests/DeviceTestHelper.cs` | new | Pair a test client |
| `tests/RestaurantPos.Api.Tests/DeviceEndpointsTests.cs` | new | Endpoint + gate tests |
| `tests/RestaurantPos.Api.Tests/DevicePairRateLimitTests.cs` | new | 429 on brute force |
| `tests/RestaurantPos.Api.Tests/BonjourAdvertiserServiceTests.cs` | new | Port parsing |
| `tests/RestaurantPos.Api.Tests/{CheckoutE2ETests,MealVoucherPolicyTests,AuthEndpointsTests}.cs` | modify | Pair before gated calls |
| `ios/Packages/PosKit/Sources/PosKit/Models/DeviceModels.swift` | new | `PairResponse`, `DeviceCredentials`, `PairingLink` |
| `ios/Packages/PosKit/Sources/PosKit/Stores/DeviceCredentialStore.swift` | new | Keychain / in-memory store |
| `ios/Packages/PosKit/Sources/PosKit/Networking/ServerBrowser.swift` | new | Bonjour browse + resolve |
| `ios/Packages/PosKit/Sources/PosKit/Networking/{PosAPI,HTTPPosAPI}.swift` | modify | `pair`, header, `deviceNotPaired` |
| `ios/Packages/PosKit/Sources/PosKit/Testing/InMemoryPosAPI.swift` | modify | `pair`, NetworkInfo |
| `ios/Packages/PosKit/Sources/PosKit/Models/OperationsModels.swift` | modify | Drop `discoveryPort` |
| `ios/Packages/PosKit/Sources/PosKit/Stores/{AppContext,AppModel,TicketStore}.swift` | modify | Pairing state, unpair on 401 |
| `ios/Packages/PosKit/Tests/PosKitTests/*` | modify/new | Tests + `pair_response.json` fixture |
| `ios/RestaurantPOS/App/PairingScreen.swift` | new | Pairing UI + QR scanner |
| `ios/RestaurantPOS/App/{RestaurantPOSApp,RootView}.swift` | modify | Wiring, settings sheet |
| `ios/project.yml` | modify | Camera usage string |
| `ios/RestaurantPOSUITests/{PosUITestCase,PairingUITests}.swift` | modify/new | UI tests |
| `src/RestaurantPos.Api/wwwroot/{index.html,app.js}` | modify | Pairing modal, Devices tab, header, scan UI removal |
| `tests/RestaurantPos.Web.E2ETests/tests/helpers/pairing.ts` | new | Shared Playwright pairing |
| `tests/RestaurantPos.Web.E2ETests/tests/device-pairing.spec.ts` | new | Web pairing E2E |
| `tests/RestaurantPos.Web.E2ETests/tests/{checkout-payment,split-bill,hotel-room-charge,web-regressions}.spec.ts` | modify | Pair before login |
| `CLAUDE.md` | modify | Contract note |

---

### Task 1: Device domain, persistence and `DeviceService`

**Files:**
- Create: `src/RestaurantPos.Domain/Enums/DeviceRole.cs`
- Create: `src/RestaurantPos.Domain/Entities/Device.cs`
- Create: `src/RestaurantPos.Application/Common/Interfaces/IDeviceService.cs`
- Create: `src/RestaurantPos.Infrastructure/Services/DeviceService.cs`
- Modify: `src/RestaurantPos.Infrastructure/Persistence/AppDbContext.cs` (DbSets near line 41, config after the `HeldOrder` block near line 300)
- Modify: `src/RestaurantPos.Api/Program.cs` (DI near line 84, SQL block before the closing `}` of the `if (!builder.Environment.IsEnvironment("Testing"))` block near line 261)
- Test: `tests/RestaurantPos.Infrastructure.Tests/DeviceServiceTests.cs`

**Interfaces:**
- Produces:
  - `enum DeviceRole { Caisse, Serveur, Cuisine, BackOffice }` (namespace `RestaurantPos.Domain.Enums`)
  - `class Device { Guid Id; string Name; DeviceRole Role; string TerminalId; string TokenHash; DateTimeOffset PairedAtUtc; DateTimeOffset? LastSeenUtc; DateTimeOffset? RevokedAtUtc }`
  - `class DevicePairingCode { Guid Id; string CodeHash; string Name; DeviceRole Role; DateTimeOffset ExpiresAtUtc; DateTimeOffset? UsedAtUtc; Guid CreatedByOperatorId }`
  - `record PairingCodeResult(string Code, DateTimeOffset ExpiresAtUtc)`
  - `record PairedDevice(Guid DeviceId, string Token, string TerminalId, string Name, DeviceRole Role)`
  - `interface IDeviceService`:
    - `Task<PairingCodeResult> CreatePairingCodeAsync(string name, DeviceRole role, Guid createdByOperatorId, CancellationToken ct = default)`
    - `Task<PairedDevice?> PairAsync(string code, CancellationToken ct = default)` — `null` = invalid/expired/used
    - `Task<Device?> AuthenticateAsync(string? token, CancellationToken ct = default)` — `null` = missing/unknown/revoked; updates `LastSeenUtc`
    - `Task<IReadOnlyList<Device>> ListAsync(CancellationToken ct = default)`
    - `Task<bool> RevokeAsync(Guid deviceId, CancellationToken ct = default)`
  - `DeviceService.Sha256Hex(string value)` public static helper; `DeviceService.CodeLength = 8`; `DeviceService.CodeLifetime = 10 min`.
  - DI: `TimeProvider` singleton (`TimeProvider.System`), `IDeviceService` scoped.

- [ ] **Step 1: Write the failing tests**

Create `tests/RestaurantPos.Infrastructure.Tests/DeviceServiceTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class DeviceServiceTests
{
    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"PosTest_Devices_{Guid.NewGuid()}")
            .Options;

    private static (DeviceService Service, AppDbContext Db, MutableTimeProvider Clock) Create()
    {
        var db = new AppDbContext(NewOptions());
        var clock = new MutableTimeProvider();
        return (new DeviceService(db, clock), db, clock);
    }

    [Fact]
    public async Task CreatePairingCode_ReturnsEightCharCode_AndStoresOnlyItsHash()
    {
        var (service, db, clock) = Create();

        var result = await service.CreatePairingCodeAsync("Caisse comptoir", DeviceRole.Caisse, Guid.NewGuid());

        result.Code.Should().HaveLength(DeviceService.CodeLength);
        result.Code.Should().MatchRegex("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{8}$");
        result.ExpiresAtUtc.Should().Be(clock.Now + DeviceService.CodeLifetime);
        var stored = await db.DevicePairingCodes.SingleAsync();
        stored.CodeHash.Should().Be(DeviceService.Sha256Hex(result.Code));
        stored.CodeHash.Should().NotContain(result.Code);
    }

    [Fact]
    public async Task Pair_WithValidCode_AssignsSequentialTerminalIds_AndStoresOnlyTokenHash()
    {
        var (service, db, _) = Create();
        var first = await service.CreatePairingCodeAsync("Caisse comptoir", DeviceRole.Caisse, Guid.NewGuid());
        var second = await service.CreatePairingCodeAsync("iPad terrasse", DeviceRole.Serveur, Guid.NewGuid());

        var d1 = await service.PairAsync(first.Code);
        var d2 = await service.PairAsync(second.Code);

        d1!.TerminalId.Should().Be("T01");
        d2!.TerminalId.Should().Be("T02");
        d2.Role.Should().Be(DeviceRole.Serveur);
        d1.Token.Should().NotBe(d2.Token);
        var stored = await db.Devices.SingleAsync(d => d.Id == d1.DeviceId);
        stored.TokenHash.Should().Be(DeviceService.Sha256Hex(d1.Token));
        stored.Name.Should().Be("Caisse comptoir");
    }

    [Fact]
    public async Task Pair_IsCaseAndWhitespaceInsensitive()
    {
        var (service, _, _) = Create();
        var created = await service.CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());
        var typed = "  " + string.Concat(created.Code.Select(char.ToLowerInvariant)) + " ";

        var paired = await service.PairAsync(typed);

        paired.Should().NotBeNull();
    }

    [Fact]
    public async Task Pair_WithUsedCode_ReturnsNull()
    {
        var (service, _, _) = Create();
        var created = await service.CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());
        (await service.PairAsync(created.Code)).Should().NotBeNull();

        (await service.PairAsync(created.Code)).Should().BeNull();
    }

    [Fact]
    public async Task Pair_WithExpiredCode_ReturnsNull()
    {
        var (service, _, clock) = Create();
        var created = await service.CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());
        clock.Now += DeviceService.CodeLifetime + TimeSpan.FromSeconds(1);

        (await service.PairAsync(created.Code)).Should().BeNull();
    }

    [Fact]
    public async Task Pair_WithUnknownCode_ReturnsNull()
    {
        var (service, _, _) = Create();
        (await service.PairAsync("ZZZZZZZZ")).Should().BeNull();
        (await service.PairAsync("")).Should().BeNull();
    }

    [Fact]
    public async Task Pair_SameCodeInParallel_OnlyOneSucceeds()
    {
        var options = NewOptions();
        var clock = new MutableTimeProvider();
        await using var setupDb = new AppDbContext(options);
        var created = await new DeviceService(setupDb, clock).CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());

        await using var dbA = new AppDbContext(options);
        await using var dbB = new AppDbContext(options);
        var results = await Task.WhenAll(
            new DeviceService(dbA, clock).PairAsync(created.Code),
            new DeviceService(dbB, clock).PairAsync(created.Code));

        results.Count(r => r is not null).Should().Be(1);
    }

    [Fact]
    public async Task Authenticate_WithValidToken_ReturnsDevice_AndUpdatesLastSeen()
    {
        var (service, db, clock) = Create();
        var created = await service.CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());
        var paired = await service.PairAsync(created.Code);
        clock.Now += TimeSpan.FromMinutes(5);

        var device = await service.AuthenticateAsync(paired!.Token);

        device!.TerminalId.Should().Be("T01");
        (await db.Devices.SingleAsync()).LastSeenUtc.Should().Be(clock.Now);
    }

    [Fact]
    public async Task Authenticate_WithMissingUnknownOrRevokedToken_ReturnsNull()
    {
        var (service, _, _) = Create();
        var created = await service.CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());
        var paired = await service.PairAsync(created.Code);

        (await service.AuthenticateAsync(null)).Should().BeNull();
        (await service.AuthenticateAsync("")).Should().BeNull();
        (await service.AuthenticateAsync("not-a-token")).Should().BeNull();

        (await service.RevokeAsync(paired!.DeviceId)).Should().BeTrue();
        (await service.AuthenticateAsync(paired.Token)).Should().BeNull();
    }

    [Fact]
    public async Task Revoke_UnknownDevice_ReturnsFalse_AndRevokedDevicesStayListed()
    {
        var (service, _, _) = Create();
        (await service.RevokeAsync(Guid.NewGuid())).Should().BeFalse();

        var created = await service.CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());
        var paired = await service.PairAsync(created.Code);
        await service.RevokeAsync(paired!.DeviceId);

        var list = await service.ListAsync();
        list.Should().ContainSingle().Which.RevokedAtUtc.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "FullyQualifiedName~DeviceServiceTests"`
Expected: build FAIL — `DeviceService`, `DeviceRole`, `AppDbContext.DevicePairingCodes` do not exist.

- [ ] **Step 3: Add the enum and entities**

`src/RestaurantPos.Domain/Enums/DeviceRole.cs`:

```csharp
namespace RestaurantPos.Domain.Enums;

public enum DeviceRole
{
    Caisse,
    Serveur,
    Cuisine,
    BackOffice
}
```

`src/RestaurantPos.Domain/Entities/Device.cs`:

```csharp
using System;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Enums;

namespace RestaurantPos.Domain.Entities;

/// <summary>Poste appairé (iPad ou caisse web). Son TerminalId porte sa propre chaîne NF525.</summary>
public class Device
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string Name { get; set; }
    public DeviceRole Role { get; set; }
    public required string TerminalId { get; init; }
    public required string TokenHash { get; init; }
    public DateTimeOffset PairedAtUtc { get; init; }
    public DateTimeOffset? LastSeenUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

/// <summary>Code d'appairage à usage unique généré depuis le back-office.</summary>
public class DevicePairingCode
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string CodeHash { get; init; }
    public required string Name { get; init; }
    public DeviceRole Role { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public Guid CreatedByOperatorId { get; init; }
}
```

- [ ] **Step 4: Add the service contract**

`src/RestaurantPos.Application/Common/Interfaces/IDeviceService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;

namespace RestaurantPos.Application.Common.Interfaces;

public record PairingCodeResult(string Code, DateTimeOffset ExpiresAtUtc);

public record PairedDevice(Guid DeviceId, string Token, string TerminalId, string Name, DeviceRole Role);

public interface IDeviceService
{
    Task<PairingCodeResult> CreatePairingCodeAsync(string name, DeviceRole role, Guid createdByOperatorId, CancellationToken ct = default);

    /// <returns><c>null</c> si le code est inconnu, expiré ou déjà utilisé.</returns>
    Task<PairedDevice?> PairAsync(string code, CancellationToken ct = default);

    /// <returns><c>null</c> si le jeton est absent, inconnu ou révoqué. Met à jour la dernière activité.</returns>
    Task<Device?> AuthenticateAsync(string? token, CancellationToken ct = default);

    Task<IReadOnlyList<Device>> ListAsync(CancellationToken ct = default);

    Task<bool> RevokeAsync(Guid deviceId, CancellationToken ct = default);
}
```

- [ ] **Step 5: Register the DbSets and model configuration**

In `AppDbContext.cs`, after `public DbSet<HappyHourOverrideSession> HappyHourOverrideSessions => Set<HappyHourOverrideSession>();` add:

```csharp

    // Appairage des postes
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DevicePairingCode> DevicePairingCodes => Set<DevicePairingCode>();
```

In `OnModelCreating`, after the `modelBuilder.Entity<HeldOrder>(...)` block add:

```csharp
        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Name).HasMaxLength(64).IsRequired();
            entity.Property(d => d.TerminalId).HasMaxLength(16).IsRequired();
            entity.Property(d => d.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(d => d.TerminalId).IsUnique();
            entity.HasIndex(d => d.TokenHash).IsUnique();
        });

        modelBuilder.Entity<DevicePairingCode>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.CodeHash).HasMaxLength(64).IsRequired();
            entity.Property(p => p.Name).HasMaxLength(64).IsRequired();
            entity.HasIndex(p => p.CodeHash);
        });
```

- [ ] **Step 6: Implement `DeviceService`**

`src/RestaurantPos.Infrastructure/Services/DeviceService.cs`:

```csharp
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public sealed class DeviceService : IDeviceService
{
    public const int CodeLength = 8;
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    // Un seul serveur par restaurant : un verrou process suffit pour rendre
    // « code consommé + TerminalId attribué » atomique entre deux appairages simultanés.
    private static readonly SemaphoreSlim PairingLock = new(1, 1);

    private readonly AppDbContext _db;
    private readonly TimeProvider _time;

    public DeviceService(AppDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Normalize(string code) => code.Trim().ToUpperInvariant();

    public async Task<PairingCodeResult> CreatePairingCodeAsync(string name, DeviceRole role, Guid createdByOperatorId, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);
        var expiresAt = _time.GetUtcNow() + CodeLifetime;
        _db.DevicePairingCodes.Add(new DevicePairingCode
        {
            CodeHash = Sha256Hex(code),
            Name = name,
            Role = role,
            ExpiresAtUtc = expiresAt,
            CreatedByOperatorId = createdByOperatorId
        });
        await _db.SaveChangesAsync(ct);
        return new PairingCodeResult(code, expiresAt);
    }

    public async Task<PairedDevice?> PairAsync(string code, CancellationToken ct = default)
    {
        var normalized = Normalize(code);
        if (normalized.Length != CodeLength)
        {
            return null;
        }

        var codeHash = Sha256Hex(normalized);
        await PairingLock.WaitAsync(ct);
        try
        {
            var now = _time.GetUtcNow();
            // Filtre d'expiration en mémoire : SQLite ne traduit pas les comparaisons de DateTimeOffset.
            var pairing = await _db.DevicePairingCodes.FirstOrDefaultAsync(p => p.CodeHash == codeHash, ct);
            if (pairing is null || pairing.UsedAtUtc is not null || pairing.ExpiresAtUtc <= now)
            {
                return null;
            }

            pairing.UsedAtUtc = now;
            var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var count = await _db.Devices.CountAsync(ct);
            var device = new Device
            {
                Name = pairing.Name,
                Role = pairing.Role,
                TerminalId = string.Create(CultureInfo.InvariantCulture, $"T{count + 1:D2}"),
                TokenHash = Sha256Hex(token),
                PairedAtUtc = now
            };
            _db.Devices.Add(device);
            await _db.SaveChangesAsync(ct);
            return new PairedDevice(device.Id, token, device.TerminalId, device.Name, device.Role);
        }
        finally
        {
            PairingLock.Release();
        }
    }

    public async Task<Device?> AuthenticateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenHash = Sha256Hex(token.Trim());
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.TokenHash == tokenHash, ct);
        if (device is null || device.RevokedAtUtc is not null)
        {
            return null;
        }

        device.LastSeenUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return device;
    }

    // ponytail: tri texte correct jusqu'à T99 ; au-delà (T100), trier sur un numéro entier stocké à part.
    public async Task<IReadOnlyList<Device>> ListAsync(CancellationToken ct = default) =>
        await _db.Devices.AsNoTracking().OrderBy(d => d.TerminalId).ToListAsync(ct);

    public async Task<bool> RevokeAsync(Guid deviceId, CancellationToken ct = default)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
        {
            return false;
        }

        device.RevokedAtUtc ??= _time.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "FullyQualifiedName~DeviceServiceTests"`
Expected: 10 passed.

- [ ] **Step 8: Register DI and SQLite schema**

In `Program.cs`, after `builder.Services.AddSingleton<IPinRateLimiterService, PinRateLimiterService>();` add:

```csharp
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IDeviceService, DeviceService>();
```

In the idempotent SQL block, after the `HappyHourOverrideSessions` statement, add:

```csharp
            try { dbContext.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS Devices (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Role INTEGER NOT NULL,
                TerminalId TEXT NOT NULL,
                TokenHash TEXT NOT NULL,
                PairedAtUtc TEXT NOT NULL,
                LastSeenUtc TEXT NULL,
                RevokedAtUtc TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Devices_TerminalId ON Devices(TerminalId);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Devices_TokenHash ON Devices(TokenHash);"); } catch { }
            try { dbContext.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS DevicePairingCodes (
                Id TEXT PRIMARY KEY,
                CodeHash TEXT NOT NULL,
                Name TEXT NOT NULL,
                Role INTEGER NOT NULL,
                ExpiresAtUtc TEXT NOT NULL,
                UsedAtUtc TEXT NULL,
                CreatedByOperatorId TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_DevicePairingCodes_CodeHash ON DevicePairingCodes(CodeHash);"); } catch { }
```

- [ ] **Step 9: Verify the whole .NET suite and schema**

Run: `dotnet format RestaurantPos.slnx && dotnet build RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: build succeeds with 0 warnings; all tests pass (baseline was 82 infra + 34 API + domain tests, plus 10 new).

Then check the SQL against an existing database:

```bash
cp src/RestaurantPos.Api/restaurantpos.db /tmp/pos-schema-check.db
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5091 \
ConnectionStrings__DefaultConnection="Data Source=/tmp/pos-schema-check.db" \
dotnet run --project src/RestaurantPos.Api &
sleep 8; kill %1
sqlite3 /tmp/pos-schema-check.db ".schema Devices" ".schema DevicePairingCodes"
```
Expected: both `CREATE TABLE` statements and their indexes are printed.

- [ ] **Step 10: Commit**

```bash
git add src/RestaurantPos.Domain src/RestaurantPos.Application src/RestaurantPos.Infrastructure src/RestaurantPos.Api/Program.cs tests/RestaurantPos.Infrastructure.Tests/DeviceServiceTests.cs
git commit -m "feat(api): add device pairing domain and service

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Device endpoints and the fiscal device gate

**Files:**
- Create: `src/RestaurantPos.Application/DTOs/DeviceDtos.cs`
- Create: `src/RestaurantPos.Api/Endpoints/RequireDeviceFilter.cs`
- Create: `src/RestaurantPos.Api/Endpoints/DeviceEndpoints.cs`
- Create (stub, completed in Task 3): `src/RestaurantPos.Api/Services/BonjourAdvertiserService.cs` — only the static `ServerName` helper in this task
- Modify: `src/RestaurantPos.Api/Endpoints/CheckoutEndpoints.cs:23-121`
- Modify: `src/RestaurantPos.Api/Endpoints/CounterSaleEndpoints.cs:207-218`
- Modify: `src/RestaurantPos.Api/RestaurantPos.Api.csproj` (add `QRCoder`)
- Modify: `src/RestaurantPos.Api/Program.cs` (`app.MapDeviceEndpoints();` next to the other `Map*Endpoints` calls near line 362)
- Create: `tests/RestaurantPos.Api.Tests/DeviceTestHelper.cs`
- Create: `tests/RestaurantPos.Api.Tests/DeviceEndpointsTests.cs`
- Create: `tests/RestaurantPos.Api.Tests/DevicePairRateLimitTests.cs`
- Modify: `tests/RestaurantPos.Api.Tests/CheckoutE2ETests.cs`, `MealVoucherPolicyTests.cs`, `AuthEndpointsTests.cs`

**Interfaces:**
- Consumes: `IDeviceService`, `PairedDevice`, `DeviceRole` (Task 1); `IPinRateLimiterService` (existing).
- Produces:
  - DTOs (namespace `RestaurantPos.Application.DTOs`): `CreatePairingCodeRequest(string Name, string Role)`, `PairingCodeResponse(string Code, DateTimeOffset ExpiresAtUtc, string QrPayload, string QrPngBase64)`, `PairRequest(string? Code)`, `PairResponse(Guid DeviceId, string Token, string TerminalId, string Name, string Role, string ServerName)`, `DeviceDto(Guid Id, string Name, string Role, string TerminalId, DateTimeOffset PairedAtUtc, DateTimeOffset? LastSeenUtc, bool IsRevoked)`.
  - `RequireDeviceFilter.HeaderName = "X-Device-Token"`, `RequireDeviceFilter.PairedDevice(HttpContext) : Device`, extension `RouteHandlerBuilder.RequirePairedDevice()`.
  - `BonjourAdvertiserService.ServerName(IConfiguration) : string`.
  - Routes: `POST /api/devices/pairing-codes`, `POST /api/devices/pair`, `GET /api/devices`, `POST /api/devices/{id}/revoke`.
  - Test helper `DeviceTestHelper.PairAsync(PosApiApplicationFactory, HttpClient, string name = "Caisse test") : Task<PairedDevice>`.

- [ ] **Step 1: Add the test helper**

`tests/RestaurantPos.Api.Tests/DeviceTestHelper.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Api.Endpoints;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Enums;

namespace RestaurantPos.Api.Tests;

/// <summary>Appaire un poste via le service et pose son jeton sur le client HTTP.</summary>
internal static class DeviceTestHelper
{
    public static async Task<PairedDevice> PairAsync(PosApiApplicationFactory factory, HttpClient client, string name = "Caisse test")
    {
        using var scope = factory.Services.CreateScope();
        var devices = scope.ServiceProvider.GetRequiredService<IDeviceService>();
        var code = await devices.CreatePairingCodeAsync(name, DeviceRole.Caisse, Guid.NewGuid());
        var paired = await devices.PairAsync(code.Code);
        client.DefaultRequestHeaders.Remove(RequireDeviceFilter.HeaderName);
        client.DefaultRequestHeaders.Add(RequireDeviceFilter.HeaderName, paired!.Token);
        return paired;
    }
}
```

- [ ] **Step 2: Write the failing endpoint tests**

`tests/RestaurantPos.Api.Tests/DeviceEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;

namespace RestaurantPos.Api.Tests;

public class DeviceEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public DeviceEndpointsTests(PosApiApplicationFactory factory) => _factory = factory;

    private HttpClient ClientWithRole(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private async Task<string> CreateCodeAsync(string name = "Caisse comptoir", string role = "Caisse")
    {
        var response = await ClientWithRole("Admin").PostAsJsonAsync("/api/devices/pairing-codes", new CreatePairingCodeRequest(name, role));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PairingCodeResponse>())!.Code;
    }

    private async Task<PairResponse> PairOverHttpAsync(string code)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/devices/pair", new PairRequest(code));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PairResponse>())!;
    }

    /// <summary>Crée une commande ouverte sur une table dédiée et renvoie son identifiant.</summary>
    private async Task<Guid> SeedOpenOrderAsync(string table, long cents)
    {
        var orderId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Orders.Add(new Order
        {
            Id = orderId,
            TableNumber = table,
            Status = OrderStatus.Open,
            Items =
            {
                new OrderItem { Id = Guid.NewGuid(), OrderId = orderId, ProductId = Guid.NewGuid(), ProductName = "Plat test", Quantity = 1, UnitPrice = Money.FromCents(cents), TaxRatePercent = 10 }
            }
        });
        await db.SaveChangesAsync();
        return orderId;
    }

    private static PaymentSettlementRequest Payment(Guid orderId, string table, decimal amount) =>
        new(orderId, table, Guid.NewGuid(), [new TenderItemRequest(PaymentMethod.Cash, amount, amount, 0m)], "POS_A");

    [Fact]
    public async Task CreatePairingCode_ReturnsCodeQrPayloadAndPng()
    {
        var response = await ClientWithRole("Admin").PostAsJsonAsync("/api/devices/pairing-codes", new CreatePairingCodeRequest("Caisse comptoir", "Caisse"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<PairingCodeResponse>())!;
        body.Code.Should().HaveLength(8);
        body.QrPayload.Should().StartWith("posdevice://pair?url=http%3A%2F%2Flocalhost").And.EndWith($"&code={body.Code}");
        Convert.FromBase64String(body.QrPngBase64).Take(4).Should().Equal(0x89, 0x50, 0x4E, 0x47); // signature PNG
    }

    [Theory]
    [InlineData("", "Caisse")]
    [InlineData("Caisse", "Plongeur")]
    [InlineData("Caisse", "99")]
    public async Task CreatePairingCode_WithInvalidInput_Returns400(string name, string role)
    {
        var response = await ClientWithRole("Admin").PostAsJsonAsync("/api/devices/pairing-codes", new CreatePairingCodeRequest(name, role));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ManagerOnlyRoutes_RejectWaiter()
    {
        var waiter = ClientWithRole("Waiter");
        (await waiter.PostAsJsonAsync("/api/devices/pairing-codes", new CreatePairingCodeRequest("X", "Caisse"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.GetAsync("/api/devices")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.PostAsync($"/api/devices/{Guid.NewGuid()}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Pair_ReturnsTokenTerminalAndServerName()
    {
        var paired = await PairOverHttpAsync(await CreateCodeAsync("iPad terrasse", "Serveur"));

        paired.Token.Should().NotBeNullOrWhiteSpace();
        paired.TerminalId.Should().MatchRegex(@"^T\d{2,}$");
        paired.Name.Should().Be("iPad terrasse");
        paired.Role.Should().Be("Serveur");
        paired.ServerName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Pair_WithReusedOrUnknownCode_Returns400PairingCodeInvalid()
    {
        var code = await CreateCodeAsync();
        await PairOverHttpAsync(code);

        foreach (var attempt in new[] { code, "ZZZZZZZZ" })
        {
            var response = await _factory.CreateClient().PostAsJsonAsync("/api/devices/pair", new PairRequest(attempt));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("code").GetString().Should().Be("pairing_code_invalid");
            body.GetProperty("message").GetString().Should().Be("Code invalide ou expiré");
        }
    }

    [Fact]
    public async Task Pay_WithoutDeviceToken_Returns401DeviceNotPaired()
    {
        var orderId = await SeedOpenOrderAsync("D10", 1000);

        var response = await ClientWithRole("Admin").PostAsJsonAsync("/api/checkout/pay", Payment(orderId, "D10", 10m));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("device_not_paired");
    }

    [Fact]
    public async Task CounterCheckout_WithoutDeviceToken_Returns401DeviceNotPaired()
    {
        var request = new CounterCheckoutRequest(
            OrderId: Guid.NewGuid(), TerminalId: "POS_A", Destination: OrderDestination.Takeaway,
            PickupBuzzer: null, PickupScheduledAtUtc: null, TipAmount: 0m, RequestFiscalReceiptPrint: false,
            MealVoucherPolicy: null, Tenders: [new CounterPaymentTender(PaymentMethod.Cash, 5m, 5m, null)]);

        var response = await ClientWithRole("Admin").PostAsJsonAsync("/api/orders/counter/checkout", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("device_not_paired");
    }

    [Fact]
    public async Task Pay_IgnoresBodyTerminalId_AndStartsDeviceChainFromGenesis()
    {
        var client = ClientWithRole("Admin");
        var paired = await DeviceTestHelper.PairAsync(_factory, client, "Caisse chaîne");
        var orderId = await SeedOpenOrderAsync("D11", 1500);

        var response = await client.PostAsJsonAsync("/api/checkout/pay", Payment(orderId, "D11", 15m));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _factory.Services.CreateScope();
        var receipt = scope.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts.Single(r => r.OrderId == orderId);
        receipt.TerminalId.Should().Be(paired.TerminalId);
        receipt.PreviousSignatureHash.Should().Be(NF525FiscalAuditService.GenesisHash);
    }

    [Fact]
    public async Task TwoDevices_KeepIndependentChains()
    {
        var clientA = ClientWithRole("Admin");
        var clientB = ClientWithRole("Admin");
        var a = await DeviceTestHelper.PairAsync(_factory, clientA, "Caisse A");
        var b = await DeviceTestHelper.PairAsync(_factory, clientB, "Caisse B");
        var orderA = await SeedOpenOrderAsync("D12", 1000);
        var orderB = await SeedOpenOrderAsync("D13", 2000);

        (await clientA.PostAsJsonAsync("/api/checkout/pay", Payment(orderA, "D12", 10m))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientB.PostAsJsonAsync("/api/checkout/pay", Payment(orderB, "D13", 20m))).StatusCode.Should().Be(HttpStatusCode.OK);

        a.TerminalId.Should().NotBe(b.TerminalId);
        using var scope = _factory.Services.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts;
        receipts.Single(r => r.OrderId == orderA).PreviousSignatureHash.Should().Be(NF525FiscalAuditService.GenesisHash);
        receipts.Single(r => r.OrderId == orderB).PreviousSignatureHash.Should().Be(NF525FiscalAuditService.GenesisHash);
    }

    [Fact]
    public async Task Void_WritesIntoDeviceChain()
    {
        var client = ClientWithRole("Admin");
        var paired = await DeviceTestHelper.PairAsync(_factory, client, "Caisse annulation");
        var orderId = await SeedOpenOrderAsync("D14", 1200);
        (await client.PostAsJsonAsync("/api/checkout/pay", Payment(orderId, "D14", 12m))).StatusCode.Should().Be(HttpStatusCode.OK);
        Guid receiptId;
        using (var scope = _factory.Services.CreateScope())
        {
            receiptId = scope.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts.Single(r => r.OrderId == orderId).Id;
        }

        var response = await client.PostAsJsonAsync($"/api/checkout/void/{receiptId}", new VoidReceiptRequest("POS_A", Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var verify = _factory.Services.CreateScope();
        verify.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts
            .Single(r => r.VoidedReceiptId == receiptId).TerminalId.Should().Be(paired.TerminalId);
    }

    [Fact]
    public async Task RevokedDevice_CannotPay_AndIsListedAsRevoked()
    {
        var client = ClientWithRole("Admin");
        var paired = await DeviceTestHelper.PairAsync(_factory, client, "Caisse volée");

        (await ClientWithRole("Admin").PostAsync($"/api/devices/{paired.DeviceId}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var orderId = await SeedOpenOrderAsync("D15", 1000);
        (await client.PostAsJsonAsync("/api/checkout/pay", Payment(orderId, "D15", 10m))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var list = await ClientWithRole("Admin").GetFromJsonAsync<List<DeviceDto>>("/api/devices");
        list!.Single(d => d.Id == paired.DeviceId).IsRevoked.Should().BeTrue();
        (await ClientWithRole("Admin").GetStringAsync("/api/devices")).Should().NotContain("tokenHash");
    }

    [Fact]
    public async Task Revoke_UnknownDevice_Returns404()
    {
        (await ClientWithRole("Admin").PostAsync($"/api/devices/{Guid.NewGuid()}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

Check the enum namespaces used above by opening `CheckoutE2ETests.cs` and `MealVoucherPolicyTests.cs` usings: `OrderStatus`, `PaymentMethod`, `OrderDestination`, `CounterCheckoutRequest`, `CounterPaymentTender` must resolve. Add `using RestaurantPos.Domain.Enums;` if `OrderDestination` is not found.

`tests/RestaurantPos.Api.Tests/DevicePairRateLimitTests.cs` (own class = own factory, so its lockout does not leak into other tests):

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Api.Tests;

public class DevicePairRateLimitTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public DevicePairRateLimitTests(PosApiApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Pair_AfterFiveWrongCodes_Returns429()
    {
        var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            (await client.PostAsJsonAsync("/api/devices/pair", new PairRequest("ZZZZZZZZ"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        var locked = await client.PostAsJsonAsync("/api/devices/pair", new PairRequest("ZZZZZZZZ"));

        locked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/RestaurantPos.Api.Tests --filter "FullyQualifiedName~Device"`
Expected: build FAIL — DTOs, `RequireDeviceFilter`, `DeviceEndpoints` do not exist.

- [ ] **Step 4: Add the DTOs**

`src/RestaurantPos.Application/DTOs/DeviceDtos.cs`:

```csharp
using System;

namespace RestaurantPos.Application.DTOs;

public record CreatePairingCodeRequest(string Name, string Role);

public record PairingCodeResponse(string Code, DateTimeOffset ExpiresAtUtc, string QrPayload, string QrPngBase64);

public record PairRequest(string? Code);

public record PairResponse(Guid DeviceId, string Token, string TerminalId, string Name, string Role, string ServerName);

public record DeviceDto(Guid Id, string Name, string Role, string TerminalId, DateTimeOffset PairedAtUtc, DateTimeOffset? LastSeenUtc, bool IsRevoked);
```

- [ ] **Step 5: Add the gate filter**

`src/RestaurantPos.Api/Endpoints/RequireDeviceFilter.cs`:

```csharp
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Api.Endpoints;

/// <summary>
/// Exige un poste appairé sur les routes qui écrivent un ticket fiscal.
/// Le TerminalId vient du poste, jamais du corps de la requête.
/// </summary>
public sealed class RequireDeviceFilter : IEndpointFilter
{
    public const string HeaderName = "X-Device-Token";
    private const string ItemKey = "PairedDevice";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var devices = http.RequestServices.GetRequiredService<IDeviceService>();
        var device = await devices.AuthenticateAsync(http.Request.Headers[HeaderName].ToString(), http.RequestAborted);
        if (device is null)
        {
            return Results.Json(
                new { code = "device_not_paired", message = "Ce poste n'est pas appairé au serveur." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        http.Items[ItemKey] = device;
        return await next(context);
    }

    public static Device PairedDevice(HttpContext http) => (Device)http.Items[ItemKey]!;
}

public static class RequireDeviceFilterExtensions
{
    public static RouteHandlerBuilder RequirePairedDevice(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<RequireDeviceFilter>();
}
```

- [ ] **Step 6: Add the server-name helper (Bonjour service body comes in Task 3)**

`src/RestaurantPos.Api/Services/BonjourAdvertiserService.cs`:

```csharp
using System;
using Microsoft.Extensions.Configuration;

namespace RestaurantPos.Api.Services;

public sealed partial class BonjourAdvertiserService
{
    /// <summary>Nom annoncé en Bonjour et renvoyé à l'appairage (l'iPad s'en sert pour retrouver le serveur).</summary>
    public static string ServerName(IConfiguration config) =>
        string.IsNullOrWhiteSpace(config["Discovery:ServerName"]) ? Environment.MachineName : config["Discovery:ServerName"]!;
}
```

- [ ] **Step 7: Add the endpoints and the QRCoder package**

Run: `dotnet add src/RestaurantPos.Api package QRCoder --version 1.8.0`

`src/RestaurantPos.Api/Endpoints/DeviceEndpoints.cs`:

```csharp
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using QRCoder;
using RestaurantPos.Api.Services;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Enums;

namespace RestaurantPos.Api.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/devices").WithTags("Devices");

        group.MapPost("/pairing-codes", async (CreatePairingCodeRequest req, IDeviceService devices, HttpContext http, CancellationToken ct) =>
        {
            var name = req.Name?.Trim() ?? string.Empty;
            if (name.Length is 0 or > 64
                || !Enum.TryParse<DeviceRole>(req.Role, ignoreCase: true, out var role)
                || !Enum.IsDefined(role)
                || int.TryParse(req.Role, out _))
            {
                return Results.BadRequest(new { Message = "Nom (64 caractères max) et rôle (Caisse, Serveur, Cuisine, BackOffice) obligatoires." });
            }

            var operatorId = Guid.TryParse(http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
            var created = await devices.CreatePairingCodeAsync(name, role, operatorId, ct);

            // Le gérant consulte le back-office depuis le réseau local : l'hôte vu ici est joignable par l'iPad.
            var serverUrl = $"{http.Request.Scheme}://{http.Request.Host}";
            var payload = $"posdevice://pair?url={Uri.EscapeDataString(serverUrl)}&code={created.Code}";
            var png = PngByteQRCodeHelper.GetQRCode(payload, QRCodeGenerator.ECCLevel.M, 8);

            return Results.Ok(new PairingCodeResponse(created.Code, created.ExpiresAtUtc, payload, Convert.ToBase64String(png)));
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapPost("/pair", async (PairRequest req, IDeviceService devices, IPinRateLimiterService rateLimiter, IConfiguration config, HttpContext http, CancellationToken ct) =>
        {
            var clientKey = "pair:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown-client");
            if (rateLimiter.IsLocked(clientKey))
            {
                var wait = Math.Ceiling(rateLimiter.GetRemainingLockout(clientKey).TotalSeconds);
                return Results.Json(
                    new { code = "rate_limited", message = $"Trop de tentatives. Patientez {wait} secondes." },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var paired = await devices.PairAsync(req.Code ?? string.Empty, ct);
            if (paired is null)
            {
                rateLimiter.RecordFailedAttempt(clientKey);
                return Results.BadRequest(new { code = "pairing_code_invalid", message = "Code invalide ou expiré" });
            }

            rateLimiter.ResetAttempts(clientKey);
            return Results.Ok(new PairResponse(paired.DeviceId, paired.Token, paired.TerminalId, paired.Name, paired.Role.ToString(), BonjourAdvertiserService.ServerName(config)));
        }).AllowAnonymous();

        group.MapGet("", async (IDeviceService devices, CancellationToken ct) =>
        {
            var list = await devices.ListAsync(ct);
            return Results.Ok(list.Select(d => new DeviceDto(d.Id, d.Name, d.Role.ToString(), d.TerminalId, d.PairedAtUtc, d.LastSeenUtc, d.RevokedAtUtc is not null)));
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapPost("/{id:guid}/revoke", async (Guid id, IDeviceService devices, CancellationToken ct) =>
            await devices.RevokeAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization("RequireManagerOrAdmin");
    }
}
```

The `int.TryParse(req.Role, out _)` clause rejects numeric roles such as `"1"`, which `Enum.TryParse` would otherwise accept.

In `Program.cs`, next to `app.MapGridEndpoints();` add `app.MapDeviceEndpoints();`.

- [ ] **Step 8: Gate the three receipt-writing routes**

In `CheckoutEndpoints.cs`:

1. Pay handler signature: `async (PaymentSettlementRequest req, ICheckoutPaymentService checkout, AppDbContext db, HttpContext http) =>`
2. Replace

```csharp
            var terminalId = !string.IsNullOrWhiteSpace(req.TerminalId)
                ? req.TerminalId
                : "POS_MAIN_TERM";
```

with

```csharp
            var terminalId = RequireDeviceFilter.PairedDevice(http).TerminalId;
```

3. Close the pay handler with `}).RequirePairedDevice();` instead of `});`.
4. Void handler: signature `async (Guid receiptId, VoidReceiptRequest req, ICheckoutPaymentService checkout, HttpContext http) =>`, and replace its first block and service call with:

```csharp
            if (req.OperatorId == Guid.Empty)
            {
                return Results.BadRequest(new { Message = "OperatorId est obligatoire pour l'annulation." });
            }

            var result = await checkout.VoidReceiptAsync(receiptId, RequireDeviceFilter.PairedDevice(http).TerminalId, req.OperatorId);
```

5. Void closing line becomes `}).RequireAuthorization("RequireManagerOrAdmin").RequirePairedDevice();`

In `CounterSaleEndpoints.cs`, the `/checkout` handler:
- signature gains `HttpContext http` as last parameter;
- replace `string terminalId = !string.IsNullOrWhiteSpace(req.TerminalId) ? req.TerminalId : "POS_MAIN_TERM";` with `string terminalId = RequireDeviceFilter.PairedDevice(http).TerminalId;`
- close with `}).RequirePairedDevice();`

Authorization middleware runs before endpoint filters, so a waiter voiding still gets 403 and an anonymous caller still gets 401 without a body code.

- [ ] **Step 9: Update the existing tests that hit gated routes**

- `CheckoutE2ETests.VoidReceipt_WithValidToken_ShouldSucceed`: after setting the Authorization header add `await DeviceTestHelper.PairAsync(_factory, _client);`.
- `CheckoutE2ETests.Pay_WithTableNumber_ShouldSucceedFreeTableAndIncrementReceiptNumber`: first line of the test body add `await DeviceTestHelper.PairAsync(_factory, _client);` (read that test's first lines; if it sets an Authorization header, add the call right after).
- `MealVoucherPolicyTests.CreateAuthenticatedClientAsync`: before `return client;` add `await DeviceTestHelper.PairAsync(_factory, client);`.
- `AuthEndpointsTests.VoidReceipt_WithFloorManagerRole_ShouldNotBeForbidden`: after adding the `X-Test-Role` header add `await DeviceTestHelper.PairAsync(_factory, client);` (the field holding the factory in that class is `_factory`; check its name).

- [ ] **Step 10: Run the API tests**

Run: `dotnet format RestaurantPos.slnx && dotnet build RestaurantPos.slnx && dotnet test tests/RestaurantPos.Api.Tests`
Expected: 0 warnings; all API tests pass (34 existing + 15 new: 11 facts + 3 theory cases in `DeviceEndpointsTests`, 1 in `DevicePairRateLimitTests`).

If `PngByteQRCodeHelper` does not exist in QRCoder 1.8.0, use instead:

```csharp
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(8);
```

- [ ] **Step 11: Commit**

```bash
git add src/RestaurantPos.Application/DTOs/DeviceDtos.cs src/RestaurantPos.Api tests/RestaurantPos.Api.Tests
git commit -m "feat(api): pair devices and require them for receipt-writing routes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Bonjour advertiser replaces the UDP beacon

**Files:**
- Modify: `src/RestaurantPos.Api/Services/BonjourAdvertiserService.cs` (complete the class started in Task 2)
- Delete: `src/RestaurantPos.Api/Services/NetworkDiscoveryBeaconService.cs`
- Modify: `src/RestaurantPos.Api/Program.cs:84` (hosted service registration)
- Modify: `src/RestaurantPos.Api/Endpoints/SyncEndpoints.cs:51` (drop `DiscoveryPort`)
- Modify: `src/RestaurantPos.Api/RestaurantPos.Api.csproj` (add `Makaretu.Dns.Multicast.New`)
- Test: `tests/RestaurantPos.Api.Tests/BonjourAdvertiserServiceTests.cs`

**Interfaces:**
- Consumes: `BonjourAdvertiserService.ServerName(IConfiguration)` (Task 2).
- Produces: `BonjourAdvertiserService.ServiceType = "_restaurantpos._tcp"`, `BonjourAdvertiserService.PortFrom(IEnumerable<string> addresses) : ushort?`. `/api/network/info` no longer returns `discoveryPort`.

- [ ] **Step 1: Write the failing test**

`tests/RestaurantPos.Api.Tests/BonjourAdvertiserServiceTests.cs`:

```csharp
using FluentAssertions;
using RestaurantPos.Api.Services;

namespace RestaurantPos.Api.Tests;

public class BonjourAdvertiserServiceTests
{
    [Theory]
    [InlineData("http://0.0.0.0:5080", (ushort)5080)]
    [InlineData("http://[::]:5081", (ushort)5081)]
    [InlineData("http://+:5082", (ushort)5082)]
    [InlineData("http://*:5083", (ushort)5083)]
    [InlineData("http://localhost:5084", (ushort)5084)]
    public void PortFrom_ReadsKestrelAddress(string address, ushort expected)
    {
        BonjourAdvertiserService.PortFrom([address]).Should().Be(expected);
    }

    [Fact]
    public void PortFrom_PrefersHttpOverHttps_AndReturnsNullWhenEmpty()
    {
        BonjourAdvertiserService.PortFrom(["https://0.0.0.0:7001", "http://0.0.0.0:5080"]).Should().Be((ushort)5080);
        BonjourAdvertiserService.PortFrom([]).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RestaurantPos.Api.Tests --filter "FullyQualifiedName~BonjourAdvertiserServiceTests"`
Expected: build FAIL — `PortFrom` not defined.

- [ ] **Step 3: Add the package and implement the advertiser**

Run: `dotnet add src/RestaurantPos.Api package Makaretu.Dns.Multicast.New --version 0.38.0`

Replace `src/RestaurantPos.Api/Services/BonjourAdvertiserService.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Makaretu.Dns;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RestaurantPos.Api.Services;

/// <summary>
/// Annonce le serveur en Bonjour (<c>_restaurantpos._tcp</c>) pour que les iPads le trouvent sans saisir d'adresse.
/// Un échec (port 5353 pris, pas d'interface multicast) est journalisé : l'appairage par QR reste possible.
/// </summary>
public sealed partial class BonjourAdvertiserService : BackgroundService
{
    public const string ServiceType = "_restaurantpos._tcp";

    private readonly ILogger<BonjourAdvertiserService> _logger;
    private readonly IServer _server;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConfiguration _config;

    public BonjourAdvertiserService(ILogger<BonjourAdvertiserService> logger, IServer server, IHostApplicationLifetime lifetime, IConfiguration config)
    {
        _logger = logger;
        _server = server;
        _lifetime = lifetime;
        _config = config;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Annonce Bonjour « {Name} » ({ServiceType}) sur le port {Port}")]
    private static partial void LogAdvertising(ILogger logger, string name, string serviceType, int port);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Annonce Bonjour impossible : {Message}. Appairage par QR ou adresse manuelle uniquement.")]
    private static partial void LogAdvertiseFailed(ILogger logger, string message);

    /// <summary>Nom annoncé en Bonjour et renvoyé à l'appairage (l'iPad s'en sert pour retrouver le serveur).</summary>
    public static string ServerName(IConfiguration config) =>
        string.IsNullOrWhiteSpace(config["Discovery:ServerName"]) ? Environment.MachineName : config["Discovery:ServerName"]!;

    /// <summary>Port HTTP réel lu dans les adresses Kestrel (« http://+:5080 », « http://[::]:5080 »…).</summary>
    public static ushort? PortFrom(IEnumerable<string> addresses)
    {
        var parsed = addresses
            .Select(a => Uri.TryCreate(a.Replace("://+:", "://localhost:", StringComparison.Ordinal).Replace("://*:", "://localhost:", StringComparison.Ordinal), UriKind.Absolute, out var uri) ? uri : null)
            .Where(u => u is not null)
            .OrderBy(u => u!.Scheme == Uri.UriSchemeHttp ? 0 : 1)
            .FirstOrDefault();
        return parsed is null ? null : (ushort)parsed.Port;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = _lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        try
        {
            // Le port n'est connu qu'une fois Kestrel démarré.
            await started.Task.WaitAsync(stoppingToken);

            var port = PortFrom(_server.Features.Get<IServerAddressesFeature>()?.Addresses ?? []);
            if (port is null)
            {
                LogAdvertiseFailed(_logger, "port HTTP introuvable");
                return;
            }

            var name = ServerName(_config);
            using var mdns = new MulticastService();
            using var discovery = new ServiceDiscovery(mdns);
            var profile = new ServiceProfile(name, ServiceType, port.Value);
            profile.AddProperty("version", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0");
            mdns.Start();
            discovery.Advertise(profile);
            discovery.Announce(profile, 2);
            LogAdvertising(_logger, name, ServiceType, port.Value);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal du serveur.
        }
#pragma warning disable CA1031 // Un échec mDNS ne doit jamais arrêter l'API.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogAdvertiseFailed(_logger, ex.Message);
        }
    }
}
```

If `new ServiceProfile(name, ServiceType, port.Value)` fails with CS7036 (missing arguments), pass the remaining ones explicitly: `new ServiceProfile(name, ServiceType, port.Value, null, false)`. If the `#pragma` is reported as unnecessary (IDE0079), remove the two pragma lines.

- [ ] **Step 4: Swap the hosted service and clean `network/info`**

- Delete `src/RestaurantPos.Api/Services/NetworkDiscoveryBeaconService.cs`.
- In `Program.cs` replace `builder.Services.AddHostedService<NetworkDiscoveryBeaconService>();` with:

```csharp
        if (!builder.Environment.IsEnvironment("Testing"))
        {
            builder.Services.AddHostedService<BonjourAdvertiserService>();
        }
```

- In `SyncEndpoints.cs` delete the line `DiscoveryPort = NetworkDiscoveryBeaconService.DiscoveryPort,`. Remove `using RestaurantPos.Api.Services;` from that file if nothing else uses it.
- Search for leftovers: `grep -rn "NetworkDiscoveryBeaconService\|DiscoveryPort" src tests --include='*.cs' | grep -v /obj/` must print nothing.

- [ ] **Step 5: Run tests and build**

Run: `dotnet format RestaurantPos.slnx && dotnet build RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: 0 warnings, all tests pass, including 6 new `BonjourAdvertiserServiceTests` cases.

- [ ] **Step 6: Verify the announcement on the LAN (macOS)**

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5080 \
Discovery__ServerName="Serveur salle" \
dotnet run --project src/RestaurantPos.Api &
sleep 8
timeout 5 dns-sd -B _restaurantpos._tcp local.
timeout 5 dns-sd -L "Serveur salle" _restaurantpos._tcp local.
kill %1
```
Expected: `-B` lists an `Add` line for `Serveur salle`; `-L` prints `can be reached at <host>.local.:5080` and the `version=` TXT entry. The server log shows `Annonce Bonjour « Serveur salle »`.

- [ ] **Step 7: Commit**

```bash
git add src/RestaurantPos.Api tests/RestaurantPos.Api.Tests/BonjourAdvertiserServiceTests.cs
git commit -m "feat(api): advertise the server over Bonjour and drop the UDP beacon

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: PosKit networking — `pair`, device header, `deviceNotPaired`

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Models/DeviceModels.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Networking/PosAPI.swift` (`APIError`, protocol)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Networking/HTTPPosAPI.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Testing/InMemoryPosAPI.swift` (`pair`, `networkInfo`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Models/OperationsModels.swift:332` (drop `discoveryPort`)
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/pair_response.json`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/network_info.json`
- Test: `ios/Packages/PosKit/Tests/PosKitTests/NetworkingTests.swift`, `ContractDecodingTests.swift`

**Interfaces:**
- Consumes: HTTP contract from Task 2 (`POST /api/devices/pair` → `PairResponse` camelCase; 401 body `{"code":"device_not_paired",...}`; 400 body `{"code":"pairing_code_invalid","message":"Code invalide ou expiré"}`).
- Produces:
  - `struct PairResponse: Codable, Hashable, Sendable { deviceId: UUID; token, terminalId, name, role, serverName: String }`
  - `struct DeviceCredentials: Codable, Hashable, Sendable` (same fields) with `init(_ response: PairResponse)` and `static let demo` (terminal `T01`, token `device-token-demo`).
  - `struct PairingLink { serverURL: URL; code: String; init?(string:) }` for `posdevice://pair?url=…&code=…`.
  - `APIError.deviceNotPaired`.
  - `PosAPI.pair(code: String) async throws -> PairResponse`.
  - `HTTPPosAPI.init(baseURL: URL, token: String? = nil, deviceToken: String? = nil, session: URLSession? = nil)`.
  - `InMemoryPosAPI.pair` accepts `TESTCODE` (any case), else throws `APIError.server(status: 400, message: "Code invalide ou expiré")`.

- [ ] **Step 1: Write the failing tests**

In `NetworkingTests.swift`, change the helper signature inside `HTTPPosAPITests` so a device token can be injected (existing trailing-closure call sites keep compiling):

```swift
    func makeAPI(deviceToken: String? = nil, _ handler: @escaping (URLRequest) -> StubURLProtocol.Response) -> HTTPPosAPI {
        StubURLProtocol.lock.withLock {
            StubURLProtocol.handler = handler
            StubURLProtocol.captured = []
        }
        let config = URLSessionConfiguration.ephemeral
        config.protocolClasses = [StubURLProtocol.self]
        return HTTPPosAPI(baseURL: URL(string: "http://pos.local:5080")!, deviceToken: deviceToken, session: URLSession(configuration: config))
    }
```

Add these tests inside `HTTPPosAPITests`:

```swift
    @Test func deviceTokenHeaderIsSentOnEveryRequest() async throws {
        let api = makeAPI(deviceToken: "dev-123") { _ in .init(status: 200, body: "[]") }
        _ = try await api.tables()
        #expect(last.value(forHTTPHeaderField: "X-Device-Token") == "dev-123")
    }

    @Test func deviceNotPairedIsDistinctFromExpiredSession() async throws {
        let revoked = makeAPI { _ in .init(status: 401, body: #"{"code":"device_not_paired","message":"Ce poste n'est pas appairé au serveur."}"#) }
        await #expect(throws: APIError.deviceNotPaired) { _ = try await revoked.tables() }
        let expired = makeAPI { _ in .init(status: 401, body: "") }
        await #expect(throws: APIError.unauthorized) { _ = try await expired.tables() }
    }

    @Test func pairPostsCodeWithoutOperatorToken() async throws {
        let api = makeAPI { _ in .init(status: 200, body: #"{"deviceId":"01a0d511-8720-7f5d-81ce-0844b9372ef1","token":"tok","terminalId":"T03","name":"Caisse comptoir","role":"Caisse","serverName":"Serveur salle"}"#) }
        await api.setToken("jwt-old")
        let paired = try await api.pair(code: "ABCD2345")
        #expect(paired.terminalId == "T03")
        #expect(paired.serverName == "Serveur salle")
        #expect(last.httpMethod == "POST")
        #expect(last.url?.path == "/api/devices/pair")
        #expect(try body(last)["code"] as? String == "ABCD2345")
        #expect(last.value(forHTTPHeaderField: "Authorization") == nil)
    }

    @Test func invalidPairingCodeSurfacesServerMessage() async throws {
        let api = makeAPI { _ in .init(status: 400, body: #"{"code":"pairing_code_invalid","message":"Code invalide ou expiré"}"#) }
        await #expect(throws: APIError.server(status: 400, message: "Code invalide ou expiré")) { _ = try await api.pair(code: "WRONG") }
    }
```

Add a new suite at file level (after `HTTPPosAPITests`):

```swift
@Suite("Lien d'appairage (QR)")
struct PairingLinkTests {
    @Test func parsesBackOfficeQrPayload() throws {
        let link = try #require(PairingLink(string: "posdevice://pair?url=http%3A%2F%2F192.168.1.10%3A5080&code=ABCD2345"))
        #expect(link.serverURL.absoluteString == "http://192.168.1.10:5080")
        #expect(link.code == "ABCD2345")
    }

    @Test func rejectsForeignOrIncompletePayloads() {
        #expect(PairingLink(string: "https://example.com") == nil)
        #expect(PairingLink(string: "posdevice://pair?code=ABCD2345") == nil)
        #expect(PairingLink(string: "posdevice://pair?url=http%3A%2F%2F192.168.1.10%3A5080") == nil)
        #expect(PairingLink(string: "posdevice://pair?url=pas-une-url&code=ABCD2345") == nil)
    }
}
```

In `ContractDecodingTests.swift`:
- in `networkAndSync()` delete `#expect(info.discoveryPort == 45454)` and add `#expect(info.port == 5000)`;
- add:

```swift
    @Test func devicePairing() throws {
        let paired = try decode(PairResponse.self, "pair_response")
        #expect(paired.terminalId == "T01")
        #expect(paired.role == "Caisse")
        #expect(!paired.token.isEmpty)
        #expect(DeviceCredentials(paired).serverName == paired.serverName)
    }
```

Create `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/pair_response.json` (shape of the real response; Task 8 replaces it with a captured one):

```json
{"deviceId":"01a0d6f2-3c1e-7b8a-9d4f-2e6a1b3c5d7e","token":"q3Jm0bM8m2Vw7bW2l8m4YyQ5b3o0Zx9t1k6p2r4s8u0","terminalId":"T01","name":"Caisse comptoir","role":"Caisse","serverName":"Serveur salle"}
```

In `network_info.json` delete `"discoveryPort":45454,`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ios/Packages/PosKit && swift test`
Expected: build FAIL — `PairResponse`, `PairingLink`, `APIError.deviceNotPaired`, `pair(code:)`, `deviceToken:` unknown.

- [ ] **Step 3: Add the models**

`ios/Packages/PosKit/Sources/PosKit/Models/DeviceModels.swift`:

```swift
import Foundation

/// Réponse de `POST /api/devices/pair`.
public struct PairResponse: Codable, Hashable, Sendable {
    public var deviceId: UUID
    public var token: String
    public var terminalId: String
    public var name: String
    public var role: String
    public var serverName: String

    public init(deviceId: UUID, token: String, terminalId: String, name: String, role: String, serverName: String) {
        self.deviceId = deviceId
        self.token = token
        self.terminalId = terminalId
        self.name = name
        self.role = role
        self.serverName = serverName
    }
}

/// Identité de l'iPad après appairage, conservée dans le trousseau.
/// `serverName` sert à retrouver le serveur en Bonjour si son adresse IP change.
public struct DeviceCredentials: Codable, Hashable, Sendable {
    public var deviceId: UUID
    public var token: String
    public var terminalId: String
    public var name: String
    public var role: String
    public var serverName: String

    public init(_ response: PairResponse) {
        deviceId = response.deviceId
        token = response.token
        terminalId = response.terminalId
        name = response.name
        role = response.role
        serverName = response.serverName
    }

    /// Poste fictif des tests et du mode `-UITestMode -UITestPaired`.
    public static let demo = DeviceCredentials(PairResponse(
        deviceId: UUID(uuidString: "00000000-0000-0000-0000-0000000000D1")!,
        token: "device-token-demo", terminalId: "T01", name: "iPad démo", role: "Caisse", serverName: "Serveur démo"
    ))
}

/// Contenu du QR affiché par le back-office : `posdevice://pair?url=<serveur>&code=<code>`.
public struct PairingLink: Hashable, Sendable {
    public var serverURL: URL
    public var code: String

    public init?(string: String) {
        guard let components = URLComponents(string: string),
              components.scheme == "posdevice", components.host == "pair",
              let items = components.queryItems,
              let rawURL = items.first(where: { $0.name == "url" })?.value,
              let url = URL(string: rawURL), url.scheme == "http" || url.scheme == "https", url.host != nil,
              let code = items.first(where: { $0.name == "code" })?.value, !code.isEmpty
        else { return nil }
        serverURL = url
        self.code = code
    }
}
```

- [ ] **Step 4: Extend `APIError` and the protocol**

In `PosAPI.swift`:
- add `case deviceNotPaired` to `APIError` (after `case unauthorized`);
- in `errorDescription` add `case .deviceNotPaired: "Ce poste n'est pas appairé au serveur. Générez un code dans Gestion → Appareils."`;
- in the protocol, under `// Auth`, add `func pair(code: String) async throws -> PairResponse`.

- [ ] **Step 5: Implement in `HTTPPosAPI`**

- Add `private var deviceToken: String?` below `private var token: String?`.
- Change the initializer to `public init(baseURL: URL, token: String? = nil, deviceToken: String? = nil, session: URLSession? = nil)` and set `self.deviceToken = deviceToken`.
- In `makeRequest`, after the `Authorization` line, add:

```swift
        if let deviceToken { request.setValue(deviceToken, forHTTPHeaderField: "X-Device-Token") }
```

- In `send`, replace `case 401: throw APIError.unauthorized` with:

```swift
            case 401: throw Self.errorCode(in: data) == "device_not_paired" ? APIError.deviceNotPaired : APIError.unauthorized
```

- Next to `extractMessage`, add:

```swift
    static func errorCode(in data: Data) -> String? {
        (try? JSONSerialization.jsonObject(with: data) as? [String: Any])?["code"] as? String
    }
```

- Under `// MARK: - Auth`, after `login`, add:

```swift
    /// Échange un code d'appairage du back-office contre l'identité du poste. Sans jeton opérateur.
    public func pair(code: String) async throws -> PairResponse {
        var request = try makeRequest("POST", "devices/pair", body: try encoder.encode(["code": code]))
        request.setValue(nil, forHTTPHeaderField: "Authorization")
        let (data, _) = try await send(request)
        return try decode(data)
    }
```

- [ ] **Step 6: Implement in `InMemoryPosAPI` and drop `discoveryPort`**

In `InMemoryPosAPI.swift`, next to `login`, add:

```swift
    public func pair(code: String) async throws -> PairResponse {
        try await step("pair")
        guard code.trimmingCharacters(in: .whitespaces).uppercased() == "TESTCODE" else {
            throw APIError.server(status: 400, message: "Code invalide ou expiré")
        }
        let demo = DeviceCredentials.demo
        return PairResponse(deviceId: demo.deviceId, token: demo.token, terminalId: demo.terminalId, name: demo.name, role: demo.role, serverName: demo.serverName)
    }
```

In `networkInfo()` remove the `discoveryPort: 45454, ` argument. In `OperationsModels.swift` delete `public var discoveryPort: Int?`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `cd ios/Packages/PosKit && swift test`
Expected: all PosKit tests pass, including the 4 new HTTP tests, 2 `PairingLinkTests`, and `devicePairing`.

- [ ] **Step 8: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(ios): pair endpoint, device token header and deviceNotPaired error in PosKit

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: PosKit state — credentials, unpair on revoke, Bonjour browser

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Stores/DeviceCredentialStore.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Networking/ServerBrowser.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Stores/AppContext.swift` (`TerminalSettings`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Stores/TicketStore.swift` (init + `run`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Stores/AppModel.swift:45` (pass unpair closure)
- Test: `ios/Packages/PosKit/Tests/PosKitTests/StoreTests.swift`

**Interfaces:**
- Consumes: `DeviceCredentials`, `DeviceCredentials.demo`, `APIError.deviceNotPaired` (Task 4).
- Produces:
  - `@MainActor protocol DeviceCredentialStore: AnyObject { func load() -> DeviceCredentials?; func save(_:); func clear() }`
  - `InMemoryCredentialStore(_ value: DeviceCredentials? = nil)`, `KeychainCredentialStore()`
  - `TerminalSettings.init(defaults: UserDefaults = .standard, credentialStore: DeviceCredentialStore = InMemoryCredentialStore())`, `credentials: DeviceCredentials?` (read-only), `isPaired: Bool`, `terminalId: String` (read-only, `""` when unpaired), `pair(serverURL: String, credentials: DeviceCredentials)`, `unpair()`
  - `TicketStore.init(..., terminalId:, onDeviceUnpaired: @escaping @MainActor () -> Void = {})`
  - `ServerBrowser` (`@MainActor @Observable`): `servers: [DiscoveredServer]`, `start()`, `stop()`, `static func resolve(name: String, timeoutSeconds: Int = 5) async -> URL?`; `DiscoveredServer { name: String }`.

- [ ] **Step 1: Write the failing tests**

In `StoreTests.swift`, change the shared helper so every existing store test runs as a paired iPad:

```swift
    let model = AppModel(api: api, settings: TerminalSettings(defaults: defaults, credentialStore: InMemoryCredentialStore(.demo)))
```

Add a suite:

```swift
@MainActor
@Suite("Appairage")
struct PairingStateTests {
    @Test func pairAndUnpairUpdateSettingsAndStore() {
        let store = InMemoryCredentialStore()
        let settings = TerminalSettings(defaults: UserDefaults(suiteName: "PosKitTests-\(UUID().uuidString)")!, credentialStore: store)
        #expect(!settings.isPaired)
        #expect(settings.terminalId.isEmpty)

        settings.pair(serverURL: "http://192.168.1.10:5080", credentials: .demo)
        #expect(settings.isPaired)
        #expect(settings.terminalId == "T01")
        #expect(settings.serverURL == "http://192.168.1.10:5080")
        #expect(store.load() == .demo)

        settings.unpair()
        #expect(!settings.isPaired)
        #expect(store.load() == nil)
        #expect(settings.serverURL == "http://192.168.1.10:5080")
    }

    @Test func credentialsAreRestoredAtLaunch() {
        let settings = TerminalSettings(defaults: UserDefaults(suiteName: "PosKitTests-\(UUID().uuidString)")!, credentialStore: InMemoryCredentialStore(.demo))
        #expect(settings.isPaired)
        #expect(settings.credentials?.serverName == "Serveur démo")
    }

    @Test func revokedDeviceOnPaymentUnpairsAndKeepsDraft() async {
        let (model, api) = await makeModel()
        await model.ticket.load(table: "T1")
        model.ticket.add(product(model, "Pizza 4 Fromages"))
        await api.setFailure(.deviceNotPaired)

        let outcome = await model.ticket.pay(method: .cash, amount: Money(cents: 1450), tendered: Money(cents: 2000))

        #expect(outcome == nil)
        #expect(!model.settings.isPaired)
        #expect(!model.ticket.lines.isEmpty)
        #expect(model.notifier.lastMessage == APIError.deviceNotPaired.errorDescription)
    }
}
```

`product(_:_:)` is the existing helper used by `fullPaymentFreesTheTable`; if it is `private` to another suite, move it to file scope next to `makeModel`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ios/Packages/PosKit && swift test`
Expected: build FAIL — `InMemoryCredentialStore`, `credentialStore:`, `pair(serverURL:credentials:)` unknown.

- [ ] **Step 3: Add the credential stores**

`ios/Packages/PosKit/Sources/PosKit/Stores/DeviceCredentialStore.swift`:

```swift
import Foundation
import Security

/// Stockage de l'identité du poste.
@MainActor
public protocol DeviceCredentialStore: AnyObject {
    func load() -> DeviceCredentials?
    func save(_ credentials: DeviceCredentials)
    func clear()
}

/// Tests unitaires, tests UI et aperçus.
@MainActor
public final class InMemoryCredentialStore: DeviceCredentialStore {
    private var value: DeviceCredentials?
    public init(_ value: DeviceCredentials? = nil) { self.value = value }
    public func load() -> DeviceCredentials? { value }
    public func save(_ credentials: DeviceCredentials) { value = credentials }
    public func clear() { value = nil }
}

/// Trousseau iOS : le jeton d'appareil ne doit jamais finir dans `UserDefaults`.
@MainActor
public final class KeychainCredentialStore: DeviceCredentialStore {
    private let service = "com.restaurantpos.device"
    private let account = "credentials"

    public init() {}

    private var baseQuery: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: service,
         kSecAttrAccount as String: account]
    }

    public func load() -> DeviceCredentials? {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess, let data = item as? Data else { return nil }
        return try? JSONDecoder().decode(DeviceCredentials.self, from: data)
    }

    public func save(_ credentials: DeviceCredentials) {
        guard let data = try? JSONEncoder().encode(credentials) else { return }
        SecItemDelete(baseQuery as CFDictionary)
        var query = baseQuery
        query[kSecValueData as String] = data
        query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        SecItemAdd(query as CFDictionary, nil)
    }

    public func clear() { SecItemDelete(baseQuery as CFDictionary) }
}
```

- [ ] **Step 4: Pairing state in `TerminalSettings`**

In `AppContext.swift`, inside `TerminalSettings`:
- delete the stored property `public var terminalId: String { didSet { ... } }` and the line `terminalId = defaults.string(forKey: Keys.terminal) ?? "POS_A"`;
- add:

```swift
    @ObservationIgnored private let credentialStore: DeviceCredentialStore
    /// Identité du poste attribuée par le serveur à l'appairage.
    public private(set) var credentials: DeviceCredentials?
    public var isPaired: Bool { credentials != nil }
    /// Identifiant fiscal attribué par le serveur (`T01`…). Vide tant que le poste n'est pas appairé.
    public var terminalId: String { credentials?.terminalId ?? "" }
```

- change the initializer signature to `public init(defaults: UserDefaults = .standard, credentialStore: DeviceCredentialStore = InMemoryCredentialStore())` and, at the end of its body, add:

```swift
        self.credentialStore = credentialStore
        credentials = credentialStore.load()
        // Ancien identifiant saisi à la main : remplacé par celui du serveur.
        defaults.removeObject(forKey: Keys.terminal)
```

(Place `self.credentialStore = credentialStore` before any use of `self` if the compiler requires all stored properties to be set first; `credentials` has a default of `nil`.)

- add the methods:

```swift
    public func pair(serverURL: String, credentials: DeviceCredentials) {
        self.serverURL = serverURL
        credentialStore.save(credentials)
        self.credentials = credentials
    }

    /// Poste révoqué ou dissocié : retour à l'écran d'appairage. L'adresse du serveur est conservée.
    public func unpair() {
        credentialStore.clear()
        credentials = nil
    }
```

- update the class doc comment to: `/// Paramètres du terminal : adresse et préférences dans \`UserDefaults\`, identité du poste dans \`credentialStore\`.`

- [ ] **Step 5: Unpair from `TicketStore` on `deviceNotPaired`**

In `TicketStore.swift`:
- add `private let onDeviceUnpaired: @MainActor () -> Void` below `terminalId`;
- init signature becomes `public init(api: PosAPI, notifier: Notifier, happyHour: HappyHourStore, session: SessionStore, terminalId: @escaping @MainActor () -> String, onDeviceUnpaired: @escaping @MainActor () -> Void = {})` and sets `self.onDeviceUnpaired = onDeviceUnpaired`;
- in `run`, before `} catch APIError.unauthorized {`, add:

```swift
        } catch APIError.deviceNotPaired {
            // Le brouillon reste sur l'iPad : il sera envoyé après le nouvel appairage.
            onDeviceUnpaired()
            notifier.error(APIError.deviceNotPaired)
            return false
```

In `AppModel.swift`, pass the closure:

```swift
        ticket = TicketStore(api: api, notifier: notifier, happyHour: happyHour, session: session, terminalId: terminal, onDeviceUnpaired: { settings.unpair() })
```

- [ ] **Step 6: Add the Bonjour browser**

`ios/Packages/PosKit/Sources/PosKit/Networking/ServerBrowser.swift`:

```swift
import Foundation
import Network
import Observation

public struct DiscoveredServer: Identifiable, Hashable, Sendable {
    public var name: String
    public var id: String { name }
}

/// Recherche Bonjour des serveurs de caisse (`_restaurantpos._tcp`) du réseau local.
@MainActor @Observable
public final class ServerBrowser {
    public static let serviceType = "_restaurantpos._tcp"

    public private(set) var servers: [DiscoveredServer] = []
    @ObservationIgnored private var browser: NWBrowser?

    public init() {}

    public func start() {
        guard browser == nil else { return }
        let browser = NWBrowser(for: .bonjour(type: Self.serviceType, domain: nil), using: .tcp)
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            let names = results.compactMap { result -> String? in
                if case let .service(name, _, _, _) = result.endpoint { return name }
                return nil
            }.sorted()
            Task { @MainActor in self?.servers = names.map(DiscoveredServer.init(name:)) }
        }
        browser.start(queue: .main)
        self.browser = browser
    }

    public func stop() {
        browser?.cancel()
        browser = nil
        servers = []
    }

    /// Résout un serveur annoncé en URL HTTP IPv4 en ouvrant une connexion TCP vers le service.
    public static func resolve(name: String, timeoutSeconds: Int = 5) async -> URL? {
        let parameters = NWParameters.tcp
        if let ip = parameters.defaultProtocolStack.internetProtocol as? NWProtocolIP.Options { ip.version = .v4 }
        let connection = NWConnection(to: .service(name: name, type: serviceType, domain: "local.", interface: nil), using: parameters)
        return await withCheckedContinuation { continuation in
            let once = ResumeOnce(continuation)
            connection.stateUpdateHandler = { state in
                switch state {
                case .ready:
                    var url: URL?
                    if case let .hostPort(host, port) = connection.currentPath?.remoteEndpoint, case let .ipv4(address) = host {
                        url = URL(string: "http://\(address):\(port.rawValue)")
                    }
                    connection.cancel()
                    once.resume(url)
                case .failed, .cancelled:
                    once.resume(nil)
                default:
                    break
                }
            }
            connection.start(queue: .global())
            DispatchQueue.global().asyncAfter(deadline: .now() + .seconds(timeoutSeconds)) {
                connection.cancel()
                once.resume(nil)
            }
        }
    }
}

/// Garantit une seule reprise de la continuation (connexion prête, échec ou délai dépassé).
private final class ResumeOnce: @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<URL?, Never>?

    init(_ continuation: CheckedContinuation<URL?, Never>) { self.continuation = continuation }

    func resume(_ value: URL?) {
        lock.lock()
        let pending = continuation
        continuation = nil
        lock.unlock()
        pending?.resume(returning: value)
    }
}
```

If Swift 6 strict concurrency rejects capturing `connection` in the `@Sendable` closures (it is `Sendable` in the iOS 17 / macOS 14 SDKs; older SDKs may not mark it), wrap it: `nonisolated(unsafe) let connection = NWConnection(...)`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `cd ios/Packages/PosKit && swift test`
Expected: all tests pass (3 new in `PairingStateTests`; existing fiscal/ticket tests unaffected because `makeModel` is paired as `T01`).

- [ ] **Step 8: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(ios): keep device identity in the Keychain and unpair on revocation

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: iPad app — pairing screen, wiring, UI tests

**Files:**
- Create: `ios/RestaurantPOS/App/PairingScreen.swift`
- Modify: `ios/RestaurantPOS/App/RestaurantPOSApp.swift` (`LaunchConfiguration`, `AppEnvironment`)
- Modify: `ios/RestaurantPOS/App/RootView.swift` (`RootView`, `LockScreen`, `ServerSettingsSheet`)
- Modify: `ios/project.yml` (camera usage string)
- Modify: `ios/RestaurantPOSUITests/PosUITestCase.swift`
- Create: `ios/RestaurantPOSUITests/PairingUITests.swift`

**Interfaces:**
- Consumes: `TerminalSettings.pair/unpair/isPaired/credentials`, `KeychainCredentialStore`, `InMemoryCredentialStore`, `DeviceCredentials.demo`, `PosAPI.pair`, `HTTPPosAPI(baseURL:deviceToken:)`, `PairingLink`, `ServerBrowser`, `DiscoveredServer` (Tasks 4–5).
- Produces: launch argument `-UITestPaired`; accessibility identifiers `pairing.url`, `pairing.code`, `pairing.submit`, `pairing.error`, `pairing.scan`, `pairing.server.<name>`, `settings.unpair`; `AppEnvironment.completePairing(serverURL:response:)`, `AppEnvironment.rediscoverServerIfUnreachable()`; `PosUITestCase.launch(pin:section:happyHour:paired:)`.

- [ ] **Step 1: Write the failing UI tests**

In `PosUITestCase.swift`, change `launch`:

```swift
    func launch(pin: String? = "1234", section: String? = nil, happyHour: Bool = false, paired: Bool = true) -> XCUIApplication {
        app = XCUIApplication()
        app.launchArguments = ["-UITestMode"]
        if paired { app.launchArguments.append("-UITestPaired") }
```

(rest unchanged; update the doc comment with `///   - paired: démarre avec un poste déjà appairé (sinon écran d'appairage).`)

`ios/RestaurantPOSUITests/PairingUITests.swift`:

```swift
import XCTest

final class PairingUITests: PosUITestCase {
    func testPairingWithCodeLeadsToPinScreen() {
        launch(pin: nil, paired: false)
        let code = element("pairing.code")
        XCTAssertTrue(code.waitForExistence(timeout: 10), "L'écran d'appairage doit s'afficher sur un iPad neuf")
        code.tap()
        code.typeText("testcode")
        tap("pairing.submit")
        assertExists("pin.1", timeout: 10)
    }

    func testInvalidCodeShowsError() {
        launch(pin: nil, paired: false)
        let code = element("pairing.code")
        XCTAssertTrue(code.waitForExistence(timeout: 10))
        code.tap()
        code.typeText("WRONG123")
        tap("pairing.submit")
        waitLabel("pairing.error", contains: "invalide")
        assertNotExists("pin.1")
    }

    func testUnpairFromServerSettingsReturnsToPairing() {
        launch(pin: nil)
        tap("lock.server")
        tap("settings.unpair")
        assertExists("pairing.code", timeout: 10)
    }
}
```

- [ ] **Step 2: Run the UI tests to verify they fail**

Run: `cd ios && xcodegen generate && ./scripts/test.sh ui`
Expected: the three `PairingUITests` fail (no `pairing.code`); others may fail too until wiring is done.

- [ ] **Step 3: Camera permission**

In `ios/project.yml`, under `targets.RestaurantPOS.info.properties`, after `NSLocalNetworkUsageDescription`, add:

```yaml
        NSCameraUsageDescription: La caisse utilise l'appareil photo pour scanner le QR d'appairage affiché dans le back-office.
```

(`NSLocalNetworkUsageDescription` and `NSBonjourServices: [_restaurantpos._tcp]` are already present.)

- [ ] **Step 4: Wire launch configuration and environment**

In `RestaurantPOSApp.swift`:

`LaunchConfiguration`: add `let startsPaired: Bool` and in `current` add `startsPaired: isUITest && args.contains("-UITestPaired"),`. Add `/// - \`-UITestPaired\` : poste déjà appairé (sinon écran d'appairage, mode test uniquement).` to the doc comment.

`AppEnvironment.init`: replace the two `settings = TerminalSettings(...)` lines with:

```swift
            settings = TerminalSettings(defaults: defaults, credentialStore: InMemoryCredentialStore(launch.startsPaired ? .demo : nil))
        } else {
            settings = TerminalSettings(credentialStore: KeychainCredentialStore())
```

`makeModel`: replace the last two lines with:

```swift
        let url = settings.url ?? URL(string: "http://localhost:5080")!
        return AppModel(api: HTTPPosAPI(baseURL: url, deviceToken: settings.credentials?.token), settings: settings, realtimeBaseURL: url)
```

Add to `AppEnvironment`:

```swift
    /// Appairage réussi : nouvelle adresse, nouveau jeton d'appareil, nouvelle session.
    func completePairing(serverURL: URL, response: PairResponse) {
        settings.pair(serverURL: serverURL.absoluteString, credentials: DeviceCredentials(response))
        reconnect()
    }

    /// Le serveur a changé d'adresse (DHCP) : on le retrouve par son nom Bonjour.
    func rediscoverServerIfUnreachable() async {
        guard !launch.isUITest, let name = settings.credentials?.serverName else { return }
        if (try? await model.api.health()) != nil { return }
        guard let url = await ServerBrowser.resolve(name: name), url.absoluteString != settings.serverURL else { return }
        settings.serverURL = url.absoluteString
        reconnect()
    }
```

- [ ] **Step 5: Pairing screen**

`ios/RestaurantPOS/App/PairingScreen.swift`:

```swift
import SwiftUI
import VisionKit
import PosKit

/// Premier lancement ou poste révoqué : relie l'iPad au serveur avec le code du back-office.
struct PairingScreen: View {
    @Environment(AppEnvironment.self) private var environment
    @Environment(AppModel.self) private var model
    @State private var browser = ServerBrowser()
    @State private var url = ""
    @State private var code = ""
    @State private var error: String?
    @State private var isPairing = false
    @State private var showsScanner = false

    var body: some View {
        NavigationStack {
            Form {
                Section("Serveurs trouvés") {
                    if browser.servers.isEmpty {
                        Label("Recherche sur le réseau local…", systemImage: "antenna.radiowaves.left.and.right")
                            .foregroundStyle(.secondary)
                    }
                    ForEach(browser.servers) { server in
                        Button(server.name) { Task { await select(server) } }
                            .accessibilityIdentifier("pairing.server.\(server.name)")
                    }
                }
                Section {
                    Button { showsScanner = true } label: {
                        Label("Scanner le QR du back-office", systemImage: "qrcode.viewfinder")
                    }
                    .disabled(!DataScannerViewController.isSupported)
                    .accessibilityIdentifier("pairing.scan")
                }
                Section("Saisie manuelle") {
                    TextField("http://192.168.1.10:5080", text: $url)
                        .keyboardType(.URL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .accessibilityIdentifier("pairing.url")
                    TextField("Code d'appairage", text: $code)
                        .textInputAutocapitalization(.characters)
                        .autocorrectionDisabled()
                        .accessibilityIdentifier("pairing.code")
                    if let error {
                        Text(error).foregroundStyle(.red).accessibilityIdentifier("pairing.error")
                    }
                    Button {
                        Task { await pair() }
                    } label: {
                        HStack {
                            Text("Appairer cet iPad")
                            if isPairing { Spacer(); ProgressView() }
                        }
                    }
                    .disabled(isPairing || code.trimmingCharacters(in: .whitespaces).isEmpty || URL(string: url)?.host == nil)
                    .accessibilityIdentifier("pairing.submit")
                }
                Section {
                    Text("Générez un code dans Gestion → Appareils sur le poste du responsable.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
            .navigationTitle("Appairage")
        }
        .onAppear {
            url = model.settings.serverURL
            if !environment.launch.isUITest { browser.start() }
        }
        .onDisappear { browser.stop() }
        .sheet(isPresented: $showsScanner) {
            QRScannerView { payload in
                showsScanner = false
                guard let link = PairingLink(string: payload) else { error = "QR non reconnu"; return }
                url = link.serverURL.absoluteString
                code = link.code
                Task { await pair() }
            }
            .ignoresSafeArea()
        }
    }

    private func select(_ server: DiscoveredServer) async {
        if let resolved = await ServerBrowser.resolve(name: server.name) {
            url = resolved.absoluteString
            error = nil
        } else {
            error = "Impossible de joindre « \(server.name) »"
        }
    }

    private func pair() async {
        guard let serverURL = URL(string: url.trimmingCharacters(in: .whitespaces)), serverURL.host != nil else {
            error = "Adresse invalide"
            return
        }
        isPairing = true
        defer { isPairing = false }
        error = nil
        let api: PosAPI = environment.launch.isUITest ? model.api : HTTPPosAPI(baseURL: serverURL)
        do {
            let response = try await api.pair(code: code.trimmingCharacters(in: .whitespaces))
            environment.completePairing(serverURL: serverURL, response: response)
        } catch {
            self.error = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
        }
    }
}

/// Lecteur de QR (VisionKit) : renvoie le premier code lu.
struct QRScannerView: UIViewControllerRepresentable {
    let onScan: (String) -> Void

    func makeUIViewController(context: Context) -> DataScannerViewController {
        let scanner = DataScannerViewController(recognizedDataTypes: [.barcode(symbologies: [.qr])], isHighlightingEnabled: true)
        scanner.delegate = context.coordinator
        return scanner
    }

    func updateUIViewController(_ scanner: DataScannerViewController, context: Context) {
        if !scanner.isScanning { try? scanner.startScanning() }
    }

    func makeCoordinator() -> Coordinator { Coordinator(onScan: onScan) }

    @MainActor
    final class Coordinator: NSObject, DataScannerViewControllerDelegate {
        private let onScan: (String) -> Void
        private var done = false

        init(onScan: @escaping (String) -> Void) { self.onScan = onScan }

        func dataScanner(_ dataScanner: DataScannerViewController, didAdd addedItems: [RecognizedItem], allItems: [RecognizedItem]) {
            guard !done else { return }
            for item in addedItems {
                if case let .barcode(barcode) = item, let payload = barcode.payloadStringValue {
                    done = true
                    dataScanner.stopScanning()
                    onScan(payload)
                    return
                }
            }
        }
    }
}
```

- [ ] **Step 6: Root routing and settings sheet**

In `RootView.swift`, `RootView.body`: replace the `if model.session.isUnlocked { … } else { … }` with:

```swift
            if !model.settings.isPaired {
                PairingScreen()
                    .transition(.opacity)
            } else if model.session.isUnlocked {
                MainShell()
                    .transition(.opacity)
            } else {
                LockScreen()
                    .transition(.opacity)
            }
```

and add `.animation(.easeInOut(duration: 0.25), value: model.settings.isPaired)` after the existing `.animation` line.

In `LockScreen`, add after `.sheet(isPresented: $showsServerSettings) { … }`:

```swift
        .task { await environment.rediscoverServerIfUnreachable() }
```

In `ServerSettingsSheet`:
- delete `@State private var terminal = ""`;
- replace the `Section("Terminal") { … }` with:

```swift
                if let device = environment.settings.credentials {
                    Section("Cet iPad") {
                        LabeledContent("Nom", value: device.name)
                        LabeledContent("Terminal", value: device.terminalId)
                        LabeledContent("Serveur", value: device.serverName)
                        Button("Dissocier cet iPad", role: .destructive) {
                            environment.settings.unpair()
                            dismiss()
                        }
                        .accessibilityIdentifier("settings.unpair")
                    }
                }
```

- in the "Enregistrer" action delete `environment.settings.terminalId = terminal.isEmpty ? "POS_A" : terminal`;
- in `.onAppear` delete `terminal = environment.settings.terminalId`;
- in `test()`, use the device token so the check reflects what the app will do: `let latency = try await HTTPPosAPI(baseURL: parsed, deviceToken: environment.settings.credentials?.token).health()`.

- [ ] **Step 7: Run all iOS tests**

Run: `cd ios && xcodegen generate && ./scripts/test.sh unit && ./scripts/test.sh ui`
Expected: unit tests pass; UI tests all pass including the 3 `PairingUITests` (existing tests run paired through the new default).

- [ ] **Step 8: Commit**

```bash
git add ios/project.yml ios/RestaurantPOS ios/RestaurantPOSUITests
git commit -m "feat(ios): pairing screen with Bonjour discovery and QR scan

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Web client — device header, pairing modal, Devices tab

**Files:**
- Modify: `src/RestaurantPos.Api/wwwroot/index.html` (sidebar ~line 270, `tabNetworkSync` pane ~505-518, modals ~1084)
- Modify: `src/RestaurantPos.Api/wwwroot/app.js` (state ~line 40, fetch wrapper ~65, pay payload ~1873, counter checkout payload ~2223, `setupAdminTabs` ~2466, `loadAdminData` ~2485, `setupNetworkSyncHandlers` call ~3619 and body ~3759-3800)
- Create: `tests/RestaurantPos.Web.E2ETests/tests/helpers/pairing.ts`
- Create: `tests/RestaurantPos.Web.E2ETests/tests/device-pairing.spec.ts`

**Interfaces:**
- Consumes: HTTP contract from Task 2.
- Produces: `localStorage['pos_device'] = {"token","terminalId","name"}`; DOM ids `devicePairingModal`, `devicePairingCodeInput`, `devicePairingError`, `btnConfirmDevicePairing`, `btnCancelDevicePairing`, `tabDevices`, `formDevicePairingCode`, `inputDeviceName`, `selectDeviceRole`, `devicePairingCodeResult`, `devicePairingQr`, `devicePairingCode`, `devicePairingExpiry`, `adminDevicesList`; Playwright helpers `loginWithPin(page)`, `managerToken(request)`, `createPairingCode(request, name)`, `pairTill(page)`.

- [ ] **Step 1: Write the Playwright helpers**

`tests/RestaurantPos.Web.E2ETests/tests/helpers/pairing.ts`:

```ts
import { expect, type APIRequestContext, type Page } from '@playwright/test';

/** Jeton gérant (PIN 9999) pour les routes du back-office. */
export async function managerToken(request: APIRequestContext): Promise<string> {
  const res = await request.post('/api/auth/login', { data: { pin: '9999' } });
  expect(res.ok()).toBeTruthy();
  return (await res.json()).token;
}

export async function createPairingCode(request: APIRequestContext, name: string): Promise<string> {
  const token = await managerToken(request);
  const res = await request.post('/api/devices/pairing-codes', {
    data: { name, role: 'Caisse' },
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(res.ok()).toBeTruthy();
  return (await res.json()).code;
}

let cachedDevice: { token: string; terminalId: string; name: string } | null = null;

/** Appaire ce navigateur de test une fois par exécution, avant le premier chargement de page. */
export async function pairTill(page: Page) {
  if (!cachedDevice) {
    const code = await createPairingCode(page.request, 'Caisse E2E');
    const res = await page.request.post('/api/devices/pair', { data: { code } });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    cachedDevice = { token: body.token, terminalId: body.terminalId, name: body.name };
  }
  await page.addInitScript((device) => localStorage.setItem('pos_device', JSON.stringify(device)), cachedDevice);
}

export async function loginWithPin(page: Page) {
  await page.goto('/?nocache=' + Date.now());
  await page.waitForLoadState('domcontentloaded');
  const pinModal = page.locator('#pinLockModal');
  if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
    await page.click('#btnPinClear');
    for (const d of '1234') await page.click(`.pin-keypad button[data-val="${d}"]`);
    await expect(pinModal).not.toHaveClass(/active/);
  }
}
```

- [ ] **Step 2: Write the failing spec**

`tests/RestaurantPos.Web.E2ETests/tests/device-pairing.spec.ts`:

```ts
import { test, expect } from '@playwright/test';
import { createPairingCode, loginWithPin, managerToken } from './helpers/pairing';

test.describe('Appairage des postes', () => {
  test('un poste non appairé ouvre la modale et se couple avec un code', async ({ page, request }) => {
    await page.addInitScript(() => localStorage.removeItem('pos_device'));
    await loginWithPin(page);

    const status = await page.evaluate(async () => {
      const res = await fetch('/api/checkout/pay', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ orderId: '00000000-0000-0000-0000-000000000000', tableNumber: 'Comptoir', tenders: [] }),
      });
      return res.status;
    });
    expect(status).toBe(401);
    await expect(page.locator('#devicePairingModal')).toHaveClass(/active/);
    await expect(page.locator('#pinLockModal')).not.toHaveClass(/active/);

    await page.fill('#devicePairingCodeInput', 'ZZZZZZZZ');
    await page.click('#btnConfirmDevicePairing');
    await expect(page.locator('#devicePairingError')).toContainText('invalide');

    const code = await createPairingCode(request, 'Caisse E2E modale');
    await page.fill('#devicePairingCodeInput', ` ${code.toLowerCase()} `);
    await page.click('#btnConfirmDevicePairing');
    await expect(page.locator('#devicePairingModal')).not.toHaveClass(/active/);
    const stored = await page.evaluate(() => JSON.parse(localStorage.getItem('pos_device') || 'null'));
    expect(stored.terminalId).toMatch(/^T\d+$/);
  });

  test('le back-office génère un code avec QR et révoque un appareil', async ({ page }) => {
    await loginWithPin(page);
    await page.click('#btnNavAdmin');
    await page.click('.admin-tab-btn[data-admin-tab="tabDevices"]');

    await page.fill('#inputDeviceName', 'iPad bar');
    await page.selectOption('#selectDeviceRole', 'Serveur');
    await page.click('#formDevicePairingCode button[type="submit"]');
    await expect(page.locator('#devicePairingCode')).toHaveText(/^[A-Z2-9]{8}$/);
    await expect(page.locator('#devicePairingQr')).toHaveAttribute('src', /^data:image\/png;base64,/);
    await expect(page.locator('#devicePairingExpiry')).toContainText('Expire dans');

    const code = (await page.locator('#devicePairingCode').textContent())!;
    const paired = await (await page.request.post('/api/devices/pair', { data: { code } })).json();
    await page.click('.admin-tab-btn[data-admin-tab="tabDevices"]');
    const row = page.locator(`.item-list-row[data-device-id="${paired.deviceId}"]`);
    await expect(row).toContainText('iPad bar');

    page.once('dialog', (dialog) => dialog.accept());
    await row.locator('.btn-revoke-device').click();
    await expect(row).toContainText('Révoqué');
  });

  test('device names are rendered as text', async ({ page, request }) => {
    const token = await managerToken(request);
    const hostile = '<img src=x onerror="window.__xss=1">';
    const created = await request.post('/api/devices/pairing-codes', {
      data: { name: hostile, role: 'Caisse' },
      headers: { Authorization: `Bearer ${token}` },
    });
    const { code } = await created.json();
    await request.post('/api/devices/pair', { data: { code } });

    await loginWithPin(page);
    await page.click('#btnNavAdmin');
    await page.click('.admin-tab-btn[data-admin-tab="tabDevices"]');
    await expect(page.locator('#adminDevicesList')).toContainText(hostile);
    expect(await page.evaluate(() => (window as any).__xss)).toBeUndefined();
  });
});
```

- [ ] **Step 3: Run the spec to verify it fails**

Start the API in another terminal:

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5080 \
Jwt__Secret="SuperSecretKeyForRestaurantPosSystemThatIsAtLeast32BytesLong!" \
dotnet run --project src/RestaurantPos.Api
```

Run: `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npx playwright test tests/device-pairing.spec.ts --project='iPad Pro 11'`
Expected: FAIL — `#devicePairingModal` / `tabDevices` not found.

- [ ] **Step 4: HTML — Devices tab, pairing modal, scan UI removal**

In `index.html`, after the `tabNetworkSync` sidebar button add:

```html
                    <button class="admin-tab-btn" data-admin-tab="tabDevices">
                        <span>📲</span> Appareils
                    </button>
```

Before `<!-- Tab 6: Financial Dashboard & KPIs -->` add:

```html
                    <!-- Tab: Appareils appairés -->
                    <div class="admin-tab-pane" id="tabDevices">
                        <div class="admin-split">
                            <div class="admin-card">
                                <h4>➕ Ajouter un appareil</h4>
                                <form id="formDevicePairingCode" class="admin-form">
                                    <div class="form-group">
                                        <label>Nom de l'appareil :</label>
                                        <input type="text" id="inputDeviceName" placeholder="Ex: Caisse comptoir" maxlength="64" required>
                                    </div>
                                    <div class="form-group">
                                        <label>Rôle :</label>
                                        <select id="selectDeviceRole">
                                            <option value="Caisse">Caisse</option>
                                            <option value="Serveur">Serveur</option>
                                            <option value="Cuisine">Cuisine</option>
                                            <option value="BackOffice">Back-office</option>
                                        </select>
                                    </div>
                                    <button type="submit" class="btn-primary">Générer un code d'appairage</button>
                                </form>
                                <div id="devicePairingCodeResult" style="display:none; margin-top:14px; text-align:center;">
                                    <img id="devicePairingQr" alt="QR d'appairage" style="width:220px; height:220px; background:white; padding:8px;">
                                    <div id="devicePairingCode" style="font-size:2rem; letter-spacing:6px; font-weight:800; margin-top:8px;"></div>
                                    <div id="devicePairingExpiry" style="font-size:0.85rem; color:var(--text-muted);"></div>
                                </div>
                            </div>
                            <div class="admin-card">
                                <h4>Appareils appairés</h4>
                                <div id="adminDevicesList" style="display:flex; flex-direction:column; gap:8px;"></div>
                            </div>
                        </div>
                    </div>

```

In the `tabNetworkSync` pane, delete from `<h4>🔍 Découverte Automatique (Zero-Config)</h4>` through the `<hr class="divider" style="margin:16px 0;">` line (the paragraph, `btnScanNetwork`, and `discoveredServersList` go with it).

After the `supervisorPinModal` block add:

```html
    <!-- Appairage du poste (encaissement refusé : poste non appairé) -->
    <div class="modal-overlay" id="devicePairingModal" style="z-index:9996;">
        <div class="modal-card" style="max-width:380px; text-align:center; padding:24px;">
            <div class="modal-header" style="justify-content:center; margin-bottom:12px;">
                <h3>📲 Coupler ce poste</h3>
            </div>
            <p style="font-size:0.9rem; color:var(--text-muted); margin-bottom:16px;">Ce poste doit être appairé pour encaisser. Saisissez le code généré dans Gestion → Appareils.</p>
            <input type="text" id="devicePairingCodeInput" maxlength="12" autocomplete="off" style="width:100%; text-align:center; font-size:1.6rem; letter-spacing:6px; padding:10px; border-radius:6px; background:var(--bg-main); border:1px solid var(--border-color); color:white; margin-bottom:10px; text-transform:uppercase;">
            <div id="devicePairingError" style="color:#ef4444; min-height:1.2em; font-size:0.85rem; margin-bottom:10px;"></div>
            <div style="display:flex; gap:10px;">
                <button type="button" class="btn-secondary" id="btnCancelDevicePairing" style="flex:1; padding:12px;">Annuler</button>
                <button type="button" class="btn-action btn-pay" id="btnConfirmDevicePairing" style="flex:1; padding:12px;">Coupler</button>
            </div>
        </div>
    </div>
```

(`maxlength="12"` leaves room for spaces around a pasted code; the server trims.)

- [ ] **Step 5: JS — state and fetch wrapper**

In `app.js`, replace

```js
        token: localStorage.getItem('pos_jwt_token') || null
    };
```

with

```js
        token: localStorage.getItem('pos_jwt_token') || null,
        device: loadStoredDevice()
    };
    if (state.device) state.terminalId = state.device.terminalId;

    function loadStoredDevice() {
        try {
            const raw = localStorage.getItem('pos_device');
            return raw ? JSON.parse(raw) : null;
        } catch {
            return null;
        }
    }

    function storeDevice(device) {
        state.device = device;
        if (device) state.terminalId = device.terminalId;
        try {
            if (device) localStorage.setItem('pos_device', JSON.stringify(device));
            else localStorage.removeItem('pos_device');
        } catch {
            // Stockage indisponible : l'appairage reste valable jusqu'au rechargement.
        }
    }
```

In the fetch wrapper, replace

```js
        const res = await originalFetch(resource, config);
        if (res.status === 401 && !resource.toString().includes('/api/auth/login')) {
```

with

```js
        if (state.device?.token) {
            config = config || {};
            config.headers = config.headers || {};
            if (config.headers instanceof Headers) config.headers.set('X-Device-Token', state.device.token);
            else if (Array.isArray(config.headers)) config.headers.push(['X-Device-Token', state.device.token]);
            else config.headers['X-Device-Token'] = state.device.token;
        }
        const res = await originalFetch(resource, config);
        if (res.status === 401) {
            const body = await res.clone().json().catch(() => ({}));
            if (body.code === 'device_not_paired') {
                // Poste inconnu ou révoqué : on garde la session opérateur, on demande un code.
                storeDevice(null);
                openDevicePairingModal();
                return res;
            }
        }
        if (res.status === 401 && !resource.toString().includes('/api/auth/login')) {
```

- [ ] **Step 6: JS — stop sending `terminalId` on receipt-writing calls**

Delete the line `                terminalId: state.terminalId || 'POS_MAIN_TERM',` in the `/api/checkout/pay` payload, and the line `            terminalId: state.terminalId || 'POS_A',` directly above `            destination: destinationToEnum(state.destination),` in `executeCounterCheckout`.

- [ ] **Step 7: JS — pairing modal, Devices tab, scan UI removal**

Add, just before `function setupNetworkSyncHandlers() {`:

```js
    // ==================== APPAIRAGE DES POSTES ====================
    function openDevicePairingModal() {
        const modal = document.getElementById('devicePairingModal');
        if (!modal) return;
        document.getElementById('devicePairingCodeInput').value = '';
        document.getElementById('devicePairingError').textContent = '';
        modal.classList.add('active');
        document.getElementById('devicePairingCodeInput').focus();
    }

    function setupDevicePairingHandlers() {
        const modal = document.getElementById('devicePairingModal');
        document.getElementById('btnCancelDevicePairing')?.addEventListener('click', () => modal.classList.remove('active'));
        document.getElementById('btnConfirmDevicePairing')?.addEventListener('click', async () => {
            const code = document.getElementById('devicePairingCodeInput').value.trim().toUpperCase();
            const errorEl = document.getElementById('devicePairingError');
            if (!code) {
                errorEl.textContent = 'Saisissez le code';
                return;
            }
            const res = await fetch('/api/devices/pair', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ code })
            });
            const data = await res.json().catch(() => ({}));
            if (!res.ok) {
                errorEl.textContent = data.message || 'Code invalide ou expiré';
                return;
            }
            storeDevice({ token: data.token, terminalId: data.terminalId, name: data.name });
            modal.classList.remove('active');
            showToast(`Poste couplé : ${data.name} (${data.terminalId}). Relancez l'encaissement.`, 'success');
        });
    }

    const escapeHtml = (value) => String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    async function loadAdminDevices() {
        const list = document.getElementById('adminDevicesList');
        if (!list) return;
        const res = await fetch('/api/devices');
        if (!res.ok) {
            list.innerHTML = '<div style="color:var(--text-muted);">Réservé aux responsables</div>';
            return;
        }
        const devices = await res.json();
        list.innerHTML = devices.length === 0
            ? '<div style="color:var(--text-muted);">Aucun appareil appairé</div>'
            : devices.map(d => `
                <div class="item-list-row" data-device-id="${d.id}">
                    <div>
                        <strong>${escapeHtml(d.name)}</strong>
                        <span style="color:var(--text-muted);">${escapeHtml(d.terminalId)} · ${escapeHtml(d.role)}</span>
                        <div style="font-size:0.8rem; color:#94a3b8;">${d.isRevoked ? 'Révoqué' : (d.lastSeenUtc ? 'Dernier encaissement : ' + new Date(d.lastSeenUtc).toLocaleString() : 'Jamais utilisé')}</div>
                    </div>
                    ${d.isRevoked ? '' : `<button type="button" class="btn-archive btn-revoke-device" data-device-id="${d.id}">Révoquer</button>`}
                </div>`).join('');
        list.querySelectorAll('.btn-revoke-device').forEach(btn => btn.addEventListener('click', async () => {
            if (!confirm('Révoquer cet appareil ? Il ne pourra plus encaisser.')) return;
            const r = await fetch(`/api/devices/${btn.dataset.deviceId}/revoke`, { method: 'POST' });
            if (r.ok) {
                showToast('Appareil révoqué', 'success');
                await loadAdminDevices();
            } else {
                showToast('Révocation impossible', 'error');
            }
        }));
    }

    let pairingCountdown = null;

    function setupDeviceAdminHandlers() {
        document.getElementById('formDevicePairingCode')?.addEventListener('submit', async (e) => {
            e.preventDefault();
            const name = document.getElementById('inputDeviceName').value.trim();
            const role = document.getElementById('selectDeviceRole').value;
            const res = await fetch('/api/devices/pairing-codes', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name, role })
            });
            const data = await res.json().catch(() => ({}));
            if (!res.ok) {
                showToast(data.message || 'Génération du code impossible', 'error');
                return;
            }
            document.getElementById('devicePairingQr').src = `data:image/png;base64,${data.qrPngBase64}`;
            document.getElementById('devicePairingCode').textContent = data.code;
            document.getElementById('devicePairingCodeResult').style.display = 'block';
            const expiry = document.getElementById('devicePairingExpiry');
            clearInterval(pairingCountdown);
            const tick = () => {
                const seconds = Math.max(0, Math.round((new Date(data.expiresAtUtc) - Date.now()) / 1000));
                expiry.textContent = seconds > 0
                    ? `Expire dans ${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`
                    : 'Code expiré';
                if (seconds === 0) clearInterval(pairingCountdown);
            };
            tick();
            pairingCountdown = setInterval(tick, 1000);
        });
    }
```

Wire it up:
- after the `setupNetworkSyncHandlers();` call (near line 3619) add `setupDevicePairingHandlers();` and `setupDeviceAdminHandlers();`;
- in `setupAdminTabs`, extend the tab branch: after `} else if (targetTab === 'tabDashboard') { loadFinancialDashboard('today'); }` add `else if (targetTab === 'tabDevices') { loadAdminDevices(); }` (keep the existing brace style);
- in `loadAdminData`, add `loadAdminDevices(),` to the `Promise.all` list.

In `setupNetworkSyncHandlers`, delete `const btnScan = document.getElementById('btnScanNetwork');`, `const listContainer = document.getElementById('discoveredServersList');`, and the whole `if (btnScan && listContainer) { … }` block.

Check for leftovers: `grep -n "btnScanNetwork\|discoveredServersList\|45454\|discoveryPort" src/RestaurantPos.Api/wwwroot/*` must print nothing.

- [ ] **Step 8: Run the spec to verify it passes**

Restart the API (static files are served from `wwwroot`; a restart guarantees no cache), then:
Run: `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npx playwright test tests/device-pairing.spec.ts`
Expected: 3 tests × 2 projects pass.

- [ ] **Step 9: Commit**

```bash
git add src/RestaurantPos.Api/wwwroot tests/RestaurantPos.Web.E2ETests/tests/helpers tests/RestaurantPos.Web.E2ETests/tests/device-pairing.spec.ts
git commit -m "feat(web): pair tills, send device token and manage devices in back-office

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Existing E2E suites, live contract, fixtures, docs

**Files:**
- Modify: `tests/RestaurantPos.Web.E2ETests/tests/checkout-payment.spec.ts`, `split-bill.spec.ts`, `hotel-room-charge.spec.ts`, `web-regressions.spec.ts`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/NetworkingTests.swift` (`LiveAPITests`)
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/pair_response.json` (captured)
- Modify: `CLAUDE.md` (Cross-client contract section)

**Interfaces:**
- Consumes: `pairTill(page)` (Task 7), `HTTPPosAPI(baseURL:deviceToken:)`, `pair(code:)` (Task 4), live API from Tasks 1–3.
- Produces: nothing new; all suites green against a real server.

- [ ] **Step 1: Pair before every web suite that takes payments**

In each of the four specs, add `import { pairTill } from './helpers/pairing';` (for `web-regressions.spec.ts` merge with the existing import line) and call `await pairTill(page);` as the first line of its login helper, before `page.goto`:
- `checkout-payment.spec.ts` → `ensureLoggedIn`, before `resetComptoirDb();`
- `split-bill.spec.ts` → `ensureLoggedIn`
- `hotel-room-charge.spec.ts` → `ensureLoggedIn`
- `web-regressions.spec.ts` → `login`

- [ ] **Step 2: Run the full web E2E suite**

With the API running on 5080 (fresh start):
Run: `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npm test`
Expected: all specs pass. If a spec that was already failing on `main` still fails, confirm with `git stash && npm test -- <spec>` against the baseline and report it rather than fixing it here.

- [ ] **Step 3: Pair the live contract test**

In `NetworkingTests.swift`, replace the body of `LiveAPITests` down to (and including) the `endToEndTableFlow` test's `let login = …` / `try #require(login.success)` lines with:

```swift
struct LiveAPITests {
    static let env = ProcessInfo.processInfo.environment
    static let baseURL = URL(string: env["POS_API_URL"] ?? "http://localhost:5080")!
    static let pin = env["POS_API_PIN"] ?? "1234"

    /// Crée un code avec le PIN gérant, appaire ce client de test, puis ouvre une session opérateur.
    static func pairedSession() async throws -> (api: HTTPPosAPI, login: LoginResponse, device: PairResponse) {
        let manager = HTTPPosAPI(baseURL: baseURL)
        let managerLogin = try await manager.login(pin: pin)
        var request = URLRequest(url: baseURL.appendingPathComponent("api/devices/pairing-codes"))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(managerLogin.token ?? "")", forHTTPHeaderField: "Authorization")
        request.httpBody = Data(#"{"name":"iPad contrat","role":"Caisse"}"#.utf8)
        let (data, _) = try await URLSession.shared.data(for: request)
        let code = try #require((try JSONSerialization.jsonObject(with: data) as? [String: Any])?["code"] as? String)
        let device = try await manager.pair(code: code)
        let api = HTTPPosAPI(baseURL: baseURL, deviceToken: device.token)
        let login = try await api.login(pin: pin)
        try #require(login.success)
        return (api, login, device)
    }

    @Test func endToEndTableFlow() async throws {
        let (api, login, device) = try await Self.pairedSession()
```

Keep the rest of `endToEndTableFlow` as is, and after `#expect(paid.remainingBalance == .zero)` add:

```swift
        #expect(paid.receiptNumber?.hasPrefix("\(device.terminalId)-") == true)
```

In `readEndpointsDecode`, replace its first line with `let (api, _, _) = try await Self.pairedSession()`, and remove the old `let api = HTTPPosAPI(…)` stored property.

Run with the API on 5080: `cd ios && POS_API_URL=http://localhost:5080 ./scripts/test.sh contract`
Expected: `LiveAPI` suite passes; the receipt number starts with the paired `Tnn-`.

- [ ] **Step 4: Capture the real `pair_response.json`**

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/api/auth/login -H 'Content-Type: application/json' -d '{"pin":"9999"}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])')
CODE=$(curl -s -X POST http://localhost:5080/api/devices/pairing-codes -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"name":"Caisse comptoir","role":"Caisse"}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["code"])')
curl -s -X POST http://localhost:5080/api/devices/pair -H 'Content-Type: application/json' -d "{\"code\":\"$CODE\"}" > ios/Packages/PosKit/Tests/PosKitTests/Fixtures/pair_response.json
cat ios/Packages/PosKit/Tests/PosKitTests/Fixtures/pair_response.json
```

Expected: one JSON object with exactly the keys `deviceId, token, terminalId, name, role, serverName`. The `devicePairing` contract test asserts `terminalId == "T01"`: if the captured value differs (the database already had devices), edit only that field back to `"T01"` in the fixture. Then run `cd ios/Packages/PosKit && swift test --filter ContractDecodingTests` → PASS. Also re-capture `network_info.json` with `curl -s http://localhost:5080/api/network/info` and confirm it has no `discoveryPort`.

Revoke the device you just created (it holds a real token): `curl -s -X POST http://localhost:5080/api/devices/$(python3 -c 'import json;print(json.load(open("ios/Packages/PosKit/Tests/PosKitTests/Fixtures/pair_response.json"))["deviceId"])')/revoke -H "Authorization: Bearer $TOKEN"`.

- [ ] **Step 5: Document the contract**

In `CLAUDE.md`, section "Cross-client contract", append:

```markdown
Receipt-writing routes (`POST /api/checkout/pay`, `/api/checkout/void/{id}`, `/api/orders/counter/checkout`) require a paired device: header `X-Device-Token`, obtained from `POST /api/devices/pair` with a code generated in the back-office (Gestion → Appareils). The server takes `terminalId` from the device (`T01`, `T02`…) and ignores the body's. A missing, unknown or revoked token returns `401 {"code":"device_not_paired"}`; both clients then show their pairing UI. The server announces itself over Bonjour as `_restaurantpos._tcp` (name = `Discovery:ServerName`).
```

And in the iOS test-args line add `-UITestPaired` next to `-UITestMode`: `Test-only launch args: \`-UITestMode\` (in-memory backend, no server), \`-UITestPaired\` (start already paired), …`.

- [ ] **Step 6: Manual check on a real iPad (not automatable: no reliable mDNS in CI)**

With the API running on the Mac (`Discovery__ServerName="Serveur salle"`, `ASPNETCORE_URLS=http://0.0.0.0:5080`) and the iPad on the same Wi-Fi:
1. Fresh install → pairing screen lists « Serveur salle » under « Serveurs trouvés »; tapping it fills `http://<mac-ip>:5080`.
2. Back-office on the Mac → Gestion → Appareils → generate a code → scan the QR with the iPad → PIN screen appears.
3. Pay a counter sale → receipt number starts with the iPad's `Tnn-`.
4. Change the Mac's IP (toggle Wi-Fi / renew DHCP lease), relaunch the app → it reaches the lock screen without manual URL entry.
5. Revoke the iPad in the back-office → next payment shows « Ce poste n'est pas appairé… » and the pairing screen; the ticket lines are still there after re-pairing.

Record the outcome of each item in the PR description.

- [ ] **Step 7: Full verification and commit**

Run:

```bash
dotnet format RestaurantPos.slnx && dotnet build RestaurantPos.slnx && dotnet test RestaurantPos.slnx
cd ios && ./scripts/test.sh unit && ./scripts/test.sh ui
```

Expected: everything green (the web E2E and contract suites were run in Steps 2–3).

```bash
git add tests/RestaurantPos.Web.E2ETests/tests ios/Packages/PosKit/Tests CLAUDE.md
git commit -m "test: pair devices in E2E and live contract suites; document device contract

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
