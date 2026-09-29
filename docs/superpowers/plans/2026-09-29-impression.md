# Impression des tickets (sous-projet C) — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Imprimer réellement bons de retrait, tickets de caisse et bons cuisine (EN/FR/AR) sur les imprimantes ESC/POS réseau, via une file persistée qui ne bloque jamais une vente.

**Architecture:** Les déclencheurs (paiement comptoir, paiement à table, envoi en cuisine) construisent un `TicketDocument` et l'insèrent dans la table `PrintJobs` après le succès métier. Un `BackgroundService` (`PrintWorker`) dépile par imprimante (FIFO, relances, échéance 30 min), rend le document en image 1 bit (SkiaSharp + Topten.RichTextKit, polices Noto embarquées) et l'envoie en `GS v 0` sur TCP 9100. L'état des imprimantes est suivi en mémoire et diffusé sur `/hubs/pos` (`OnPrinterStatusChanged`).

**Tech Stack:** .NET 9 (Minimal API, EF Core SQLite/InMemory, xUnit + FluentAssertions), SkiaSharp, Topten.RichTextKit, JS vanilla + Playwright, Swift 6 / SwiftUI / Swift Testing / XCUITest.

**Spec:** `docs/superpowers/specs/2026-09-29-impression-design.md`

## Global Constraints

- Postes : `HOT_KITCHEN`, `COLD`, `GRILL`, `DESSERT`, `BAR` (cuisine) + `RECEIPT` (caisse). Liste fixe, aucun poste personnalisable.
- Résolution du poste d'une ligne : `OrderItem.PreparationStationId` → `Product.PreparationStationId` → `Category.PreparationStationId` → `HOT_KITCHEN`.
- Imprimante des tickets client : `Device.ReceiptPrinterId` (active) sinon première imprimante active du poste `RECEIPT` (ordre alphabétique du nom).
- Langues : `en`/`fr`/`ar`. Tickets client en `ReceiptLanguage`, bons cuisine en `KitchenTicketLanguage` (défaut = `ReceiptLanguage`).
- File : échéance `DeadlineAtUtc` = création (ou relance manuelle) + 30 min. Relances +5 s, +15 s, +30 s puis 60 s. Scrutation 5 s + signal à chaque mise en file. Purge quotidienne `Sent`/`Cancelled` > 7 jours ; `Failed` conservés.
- Rendu : 576 points (80 mm) ou 384 points (58 mm), bandes de 256 lignes `GS v 0`, tiroir `ESC p 0 25 250`, coupe `GS V 66 0`. Connexion 3 s, écriture 10 s.
- La mise en file ne fait **jamais** échouer l'opération métier : toute exception est journalisée et donne `printQueued: false`.
- Réponses de paiement : `printQueued` ajouté ; `printPickupVoucher` / `printFiscalReceipt` / `openCashDrawer` inchangés.
- NF525 inchangé : l'impression lit le reçu signé, n'écrit rien dans la chaîne fiscale.
- Montants en centimes jusqu'au rendu, affichés `0.00` en culture invariante, chiffres occidentaux.
- Aucune logique d'impression ne connaît les entités métier hormis `TicketDocumentBuilder` et `PrintDispatcher`.
- EF Core SQLite ne traduit ni tri ni comparaison sur `DateTimeOffset` : filtrer/trier ces champs **en mémoire** après `ToListAsync` (requêtes SQL sur `Status`/`PrinterId` seulement).
- Journalisation par `[LoggerMessage]` (style de `AuthEndpoints.cs:139`), pas d'appel direct `LogWarning(...)`.
- `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` : `dotnet format RestaurantPos.slnx` avant chaque build ; les nouveaux paquets ne doivent produire aucun avertissement (NU1608, NU1701…).
- Schéma : chaque colonne/table nouvelle a son SQL idempotent dans le bloc non-`Testing` de `Program.cs`.
- `PrintWorker` n'est enregistré que hors environnement `Testing` (comme `BonjourAdvertiserService`).
- `ios/RestaurantPOS.xcodeproj` est généré : modifier `ios/project.yml`, jamais le `.xcodeproj`.
- Nouvelles chaînes dans les trois langues : serveur `.resx`, web `i18n/*.json`, PosKit `Localizable.xcstrings`, app `Localizable.xcstrings`.
- Messages de commit en français (`feat(impression): …`, `test(impression): …`).

## Review Focus

1. **Client ancien qui envoie un `PUT` sans le nouveau champ** (catégorie sans `preparationStationId`, réglages sans `kitchenTicketLanguage`) : un iPad non mis à jour ne doit effacer ni le poste de la famille ni la langue cuisine. → Réglages : champ nullable = inchangé (test Task 2 `Put_WithoutKitchenLanguage_KeepsIt`). Catégories : champ nullable = inchangé, chaîne vide = effacer (test Task 2 `PutCategory_WithoutStation_KeepsIt`).
2. **Ligne de commande qui arrive avec `HOT_KITCHEN` par défaut** (web `app.js:924/989`, iOS `CartLine.station`) : la famille n'est alors jamais consultée et tout part en cuisine chaude. → Task 9 et Task 10 envoient `null` quand l'article n'a pas de poste ; test Task 7 `Resolve_UsesCategory_WhenItemAndProductEmpty`, test Task 10 `cartLineWithoutProductStationSendsNil`.
3. **Texte arabe mêlé de chiffres/latin** (« طاولة T05 », « سفري | A12 », montants) : lettres liées, ordre bidi correct, chiffres occidentaux. → Test Task 3 `Render_MixedArabicLatin_DrawsInkOnRightHalfForRtlStart` + contrôle visuel Task 3 + essai manuel Task 12.
4. **Imprimante débranchée en plein service** : les ventes continuent, les bons s'accumulent sans doublon ni perte d'ordre, puis sortent dans l'ordre à la reconnexion. → Tests Task 5 `HeadFailure_BlocksFollowingJobs_OfSamePrinterOnly` et `Recovery_PrintsBacklogInOrder_AndNotifiesOnline`.
5. **Aucune imprimante configurée** (installation neuve, poste sans imprimante, imprimante de caisse désactivée) : paiement OK, `printQueued: false`, aucun job orphelin. → Tests Task 8 `CounterSale_NoReceiptPrinter_ReturnsFalse_NoJob` et `Kitchen_StationWithoutPrinter_QueuesNothing`.

---

## File Structure

**Domain**
- Create `src/RestaurantPos.Domain/Entities/PrintJob.cs` — entité + enums `PrintJobKind`, `PrintJobStatus`.
- Create `src/RestaurantPos.Domain/Common/PreparationStations.cs` — liste fixe + résolution.
- Modify `Entities/Category.cs`, `Entities/Device.cs`, `Entities/RestaurantSettings.cs`.

**Infrastructure / Printing**
- Modify `TicketDocument.cs` — discriminant JSON.
- Create `TicketDocumentJson.cs` — (dé)sérialisation.
- Modify `TicketDocumentBuilder.cs` — `Receipt`, `KitchenTicket`, `TestPage`.
- Create `EscPosRasterRenderer.cs` (+ `MonoImage`), `EscPosCommands.cs`, `EscPosSender.cs`, `PrinterTransport.cs` (`IPrinterTransport`, `EscPosPrinterTransport`).
- Create `Fonts/NotoSans-Regular.ttf`, `NotoSans-Bold.ttf`, `NotoSansArabic-Regular.ttf`, `NotoSansArabic-Bold.ttf`, `OFL.txt`.
- Create `PrintQueue.cs` (`PrintSignal`, `PrintQueue`), `PrinterStatusTracker.cs`, `PrintQueueProcessor.cs` (+ `IPrinterStatusNotifier`), `PrintDispatcher.cs`, `PrintLog.cs`.
- Modify `Services/PrinterConfigurationService.cs`, `Services/KitchenRoutingService.cs`, `Services/RestaurantSettingsService.cs`, `Services/BackOfficeCatalogService.cs`, `Services/DeviceService.cs`, `Persistence/AppDbContext.cs`, `Localization/SharedResource*.resx`, `RestaurantPos.Infrastructure.csproj`.

**Api**
- Create `Services/PrintWorker.cs`, `Services/SignalRPrinterStatusNotifier.cs`, `Endpoints/PrintJobEndpoints.cs`.
- Modify `Program.cs`, `Hubs/PosHub.cs`, `Hubs/KitchenHub.cs`, `Endpoints/CounterSaleEndpoints.cs`, `CheckoutEndpoints.cs`, `TableEndpoints.cs`, `HospitalityEndpoints.cs`, `CatalogEndpoints.cs`, `DeviceEndpoints.cs`.
- Modify `Application/Common/Interfaces/IRestaurantSettingsService.cs`, `IDeviceService.cs`, `IBackOfficeCatalogService.cs`, `Application/DTOs/CounterSaleDtos.cs`, `DeviceDtos.cs`.

**Web** : `wwwroot/app.js`, `index.html`, `i18n/{en,fr,ar}.json`, `tests/RestaurantPos.Web.E2ETests/tests/printing.spec.ts`.

**iOS** : PosKit `Models/*`, `Networking/PosAPI.swift`, `HTTPPosAPI.swift`, `Testing/InMemoryPosAPI.swift`, `Realtime/SignalRClient.swift`, `Stores/AdminStores.swift`, `Stores/TicketStore.swift`, `Stores/AppModel.swift`, `Core/OrderMath.swift`, fixtures ; app `Features/Admin/AdminScreen.swift`, `Features/Payment/PaymentSheet.swift`, `Resources/Localizable.xcstrings`, UI tests.

**Docs** : `docs/impression.md`, `CLAUDE.md` (lignes Realtime et Backend).

---

### Task 1: Entité `PrintJob`, postes et schéma

**Files:**
- Create: `src/RestaurantPos.Domain/Entities/PrintJob.cs`
- Create: `src/RestaurantPos.Domain/Common/PreparationStations.cs`
- Modify: `src/RestaurantPos.Domain/Entities/Category.cs`, `Device.cs`, `RestaurantSettings.cs`
- Modify: `src/RestaurantPos.Infrastructure/Persistence/AppDbContext.cs` (DbSets l.11-46, config ~l.335)
- Modify: `src/RestaurantPos.Infrastructure/Services/RestaurantSettingsService.cs:37` (création de la ligne)
- Modify: `src/RestaurantPos.Api/Program.cs:301-305` (bloc schéma)
- Test: `tests/RestaurantPos.Infrastructure.Tests/PrintJobSchemaTests.cs`, `tests/RestaurantPos.Domain.Tests/PreparationStationsTests.cs`

**Interfaces:**
- Produces: `PrintJob` (props ci-dessous), `PrintJobKind { PickupVoucher=0, Receipt=1, KitchenTicket=2 }`, `PrintJobStatus { Pending=0, Sent=1, Failed=2, Cancelled=3 }`, `PrintJob.Lifetime` (30 min), `AppDbContext.PrintJobs`, `Category.PreparationStationId : string?`, `Device.ReceiptPrinterId : Guid?`, `RestaurantSettings.KitchenTicketLanguage : string` (required), `PreparationStations.HotKitchen`, `.Receipt`, `.Kitchen`, `.Normalize(string?)`, `.IsKitchenStationOrEmpty(string?)`, `.Resolve(string? item, string? product, string? category) : string`.

- [ ] **Step 1: Write the failing tests**

`tests/RestaurantPos.Domain.Tests/PreparationStationsTests.cs` :

```csharp
using FluentAssertions;
using RestaurantPos.Domain.Common;
using Xunit;

namespace RestaurantPos.Domain.Tests;

public class PreparationStationsTests
{
    [Theory]
    [InlineData("BAR", "COLD", "DESSERT", "BAR")]
    [InlineData(null, "COLD", "DESSERT", "COLD")]
    [InlineData("", " ", "DESSERT", "DESSERT")]
    [InlineData(null, null, null, "HOT_KITCHEN")]
    public void Resolve_FollowsItemProductCategoryDefault(string? item, string? product, string? category, string expected) =>
        PreparationStations.Resolve(item, product, category).Should().Be(expected);

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("GRILL", true)]
    [InlineData("RECEIPT", false)]
    [InlineData("STATION-HOT", false)]
    public void IsKitchenStationOrEmpty(string? id, bool expected) =>
        PreparationStations.IsKitchenStationOrEmpty(id).Should().Be(expected);
}
```

`tests/RestaurantPos.Infrastructure.Tests/PrintJobSchemaTests.cs` (SQLite réel, vérifie le mapping `DateTimeOffset`/enums) :

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class PrintJobSchemaTests
{
    [Fact]
    public async Task PrintJob_RoundTripsThroughSqlite()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        using (var db = new AppDbContext(options))
        {
            db.Database.EnsureCreated();
            db.PrintJobs.Add(new PrintJob
            {
                PrinterId = Guid.NewGuid(), Kind = PrintJobKind.KitchenTicket, DocumentJson = "{}",
                NextAttemptAtUtc = now, DeadlineAtUtc = now + PrintJob.Lifetime, CreatedAtUtc = now
            });
            await db.SaveChangesAsync();
        }
        using (var db = new AppDbContext(options))
        {
            var job = (await db.PrintJobs.Where(j => j.Status == PrintJobStatus.Pending).ToListAsync()).Single();
            job.Kind.Should().Be(PrintJobKind.KitchenTicket);
            job.DeadlineAtUtc.Should().Be(now.AddMinutes(30));
            job.Attempts.Should().Be(0);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RestaurantPos.Domain.Tests --filter PreparationStationsTests` puis `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter PrintJobSchemaTests`
Expected: échec de compilation (`PreparationStations`, `PrintJob` introuvables).

- [ ] **Step 3: Implement**

`src/RestaurantPos.Domain/Common/PreparationStations.cs` :

```csharp
using System.Collections.Generic;
using System.Linq;

namespace RestaurantPos.Domain.Common;

/// <summary>Postes de préparation (liste fixe). RECEIPT désigne les imprimantes de caisse, jamais une ligne de commande.</summary>
public static class PreparationStations
{
    public const string HotKitchen = "HOT_KITCHEN";
    public const string Receipt = "RECEIPT";
    public static readonly IReadOnlyList<string> Kitchen = [HotKitchen, "COLD", "GRILL", "DESSERT", "BAR"];

    public static string? Normalize(string? id) => string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    public static bool IsKitchenStationOrEmpty(string? id) => Normalize(id) is not { } s || Kitchen.Contains(s);

    /// <summary>Ligne → article → famille → cuisine chaude.</summary>
    public static string Resolve(string? item, string? product, string? category) =>
        Normalize(item) ?? Normalize(product) ?? Normalize(category) ?? HotKitchen;
}
```

`src/RestaurantPos.Domain/Entities/PrintJob.cs` :

```csharp
using System;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

public enum PrintJobKind { PickupVoucher = 0, Receipt = 1, KitchenTicket = 2 }

public enum PrintJobStatus { Pending = 0, Sent = 1, Failed = 2, Cancelled = 3 }

/// <summary>Document en attente d'impression, rejoué par le PrintWorker jusqu'à DeadlineAtUtc.</summary>
public class PrintJob
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid PrinterId { get; set; }
    public PrintJobKind Kind { get; set; }
    public required string DocumentJson { get; set; }
    public bool OpenCashDrawer { get; set; }
    public PrintJobStatus Status { get; set; } = PrintJobStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset DeadlineAtUtc { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? SentAtUtc { get; set; }
}
```

Champs ajoutés :

```csharp
// Category.cs, après ColorHex
/// <summary>Poste par défaut des articles de la famille (null = cuisine chaude).</summary>
public string? PreparationStationId { get; set; }

// Device.cs, après RevokedAtUtc
/// <summary>Imprimante des tickets client de ce poste (null = imprimante du poste RECEIPT).</summary>
public Guid? ReceiptPrinterId { get; set; }

// RestaurantSettings.cs, après ReceiptLanguage
public required string KitchenTicketLanguage { get; set; }
```

`RestaurantSettingsService.LoadOrCreateAsync` : `var lang = hasOrders ? "fr" : "en";` puis `new RestaurantSettings { ReceiptLanguage = lang, KitchenTicketLanguage = lang }`. Corriger de même tout autre `new RestaurantSettings {` (`grep -rn "new RestaurantSettings" src tests`).

`AppDbContext` : `public DbSet<PrintJob> PrintJobs => Set<PrintJob>();` et dans `OnModelCreating` :

```csharp
modelBuilder.Entity<PrintJob>(entity =>
{
    entity.HasKey(j => j.Id);
    entity.HasIndex(j => new { j.Status, j.NextAttemptAtUtc });
});
```

`Program.cs`, juste après le `CREATE TABLE IF NOT EXISTS RestaurantSettings` (avant l'appel `GetAsync()` de la l.308) :

```csharp
try { dbContext.Database.ExecuteSqlRaw("ALTER TABLE RestaurantSettings ADD COLUMN KitchenTicketLanguage TEXT NULL;"); } catch { }
try { dbContext.Database.ExecuteSqlRaw("UPDATE RestaurantSettings SET KitchenTicketLanguage = ReceiptLanguage WHERE KitchenTicketLanguage IS NULL;"); } catch { }
try { dbContext.Database.ExecuteSqlRaw("ALTER TABLE Categories ADD COLUMN PreparationStationId TEXT NULL;"); } catch { }
try { dbContext.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN ReceiptPrinterId TEXT NULL;"); } catch { }
try { dbContext.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS PrintJobs (
    Id TEXT PRIMARY KEY,
    PrinterId TEXT NOT NULL,
    Kind INTEGER NOT NULL,
    DocumentJson TEXT NOT NULL,
    OpenCashDrawer INTEGER NOT NULL,
    Status INTEGER NOT NULL,
    Attempts INTEGER NOT NULL,
    NextAttemptAtUtc TEXT NOT NULL,
    DeadlineAtUtc TEXT NOT NULL,
    LastError TEXT NULL,
    CreatedAtUtc TEXT NOT NULL,
    SentAtUtc TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_PrintJobs_Status_NextAttemptAtUtc ON PrintJobs(Status, NextAttemptAtUtc);"); } catch { }
```

- [ ] **Step 4: Run tests + build**

Run: `dotnet format RestaurantPos.slnx && dotnet build RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS, 0 avertissement.

- [ ] **Step 5: Vérifier la migration sur une base existante**

```bash
cp src/RestaurantPos.Api/restaurantpos.db /tmp/old.db
# Lancer l'API sur cette copie (nom de la chaîne de connexion : lire Program.cs ~l.52), attendre « Now listening », arrêter.
sqlite3 /tmp/old.db ".schema PrintJobs" "SELECT ReceiptLanguage, KitchenTicketLanguage FROM RestaurantSettings;" "PRAGMA table_info(Categories);" "PRAGMA table_info(Devices);"
```
Expected: table `PrintJobs` + index, `KitchenTicketLanguage` = `ReceiptLanguage`, colonnes `PreparationStationId` et `ReceiptPrinterId` présentes.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(impression): entité PrintJob, postes de préparation et schéma"
```

---

### Task 2: API de configuration — langue cuisine, poste des familles, imprimante des caisses, `requestReceiptPrint`

**Files:**
- Modify: `src/RestaurantPos.Application/Common/Interfaces/IRestaurantSettingsService.cs`
- Modify: `src/RestaurantPos.Infrastructure/Services/RestaurantSettingsService.cs`
- Modify: `src/RestaurantPos.Application/Common/Interfaces/IBackOfficeCatalogService.cs:12-13`, `src/RestaurantPos.Infrastructure/Services/BackOfficeCatalogService.cs:52-88`
- Modify: `src/RestaurantPos.Api/Endpoints/CatalogEndpoints.cs:27-37`, `src/RestaurantPos.Api/Program.cs:665-666` (records catégorie), `Program.cs:676` (`PaymentSettlementRequest`)
- Modify: `src/RestaurantPos.Application/DTOs/DeviceDtos.cs:13`, `Application/Common/Interfaces/IDeviceService.cs`, `Infrastructure/Services/DeviceService.cs`, `Api/Endpoints/DeviceEndpoints.cs:69-78`
- Modify: `src/RestaurantPos.Infrastructure/Localization/SharedResource.resx`, `.fr.resx`, `.ar.resx`
- Test: `tests/RestaurantPos.Api.Tests/SettingsEndpointsTests.cs`, `tests/RestaurantPos.Api.Tests/PrintingConfigEndpointsTests.cs`, `tests/RestaurantPos.Infrastructure.Tests/RestaurantSettingsServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 (`PreparationStations`, nouveaux champs).
- Produces: `RestaurantSettingsDto(string ReceiptLanguage, string KitchenTicketLanguage)`, `UpdateRestaurantSettingsRequest(string ReceiptLanguage, string? KitchenTicketLanguage = null)` ; `CreateCategoryRequest(..., string? PreparationStationId = null)`, `UpdateCategoryRequest(..., string? PreparationStationId = null)` ; `IBackOfficeCatalogService.CreateCategoryAsync(string name, string? colorHex, int displayOrder, string? iconName, string? preparationStationId = null, CancellationToken ct = default)` et `UpdateCategoryAsync(..., bool isActive, string? preparationStationId = null, CancellationToken ct = default)` (null = inchangé, `""` = effacer) ; `DeviceDto(..., bool IsRevoked, Guid? ReceiptPrinterId)` ; `SetReceiptPrinterRequest(Guid? PrinterId)` ; `IDeviceService.SetReceiptPrinterAsync(Guid deviceId, Guid? printerId, CancellationToken ct = default) : Task<bool>` ; `PUT /api/devices/{id}/receipt-printer` → 204 / 404 ; `PaymentSettlementRequest(..., string? TerminalId = null, bool RequestReceiptPrint = false)`.

Écart assumé à la spec : il n'existe pas d'endpoint de modification d'appareil, d'où la route dédiée `PUT /api/devices/{id}/receipt-printer`.

- [ ] **Step 1: Write the failing tests**

Ajouter à `SettingsEndpointsTests` :

```csharp
[Fact]
public async Task KitchenLanguage_RoundTrips_AndDefaultsToReceiptLanguage()
{
    var admin = await ClientAsync("9999");
    var initial = await admin.GetFromJsonAsync<JsonElement>("/api/settings");
    initial.GetProperty("kitchenTicketLanguage").GetString().Should().Be(initial.GetProperty("receiptLanguage").GetString());

    (await admin.PutAsJsonAsync("/api/settings", new UpdateRestaurantSettingsRequest("fr", "ar"))).StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await admin.GetFromJsonAsync<JsonElement>("/api/settings");
    body.GetProperty("kitchenTicketLanguage").GetString().Should().Be("ar");
}

[Fact]
public async Task Put_WithoutKitchenLanguage_KeepsIt()
{
    var admin = await ClientAsync("9999");
    await admin.PutAsJsonAsync("/api/settings", new UpdateRestaurantSettingsRequest("fr", "ar"));
    (await admin.PutAsJsonAsync("/api/settings", new { receiptLanguage = "en" })).StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await admin.GetFromJsonAsync<JsonElement>("/api/settings");
    body.GetProperty("receiptLanguage").GetString().Should().Be("en");
    body.GetProperty("kitchenTicketLanguage").GetString().Should().Be("ar");
}

[Fact]
public async Task Put_InvalidKitchenLanguage_Returns400()
{
    var admin = await ClientAsync("9999");
    (await admin.PutAsJsonAsync("/api/settings", new UpdateRestaurantSettingsRequest("fr", "de"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

Ajouter à `RestaurantSettingsServiceTests` deux tests sur la mise en place existante du fichier : base vide → `(await service.GetAsync()).KitchenTicketLanguage == "en"` ; base avec une commande → `"fr"`.

`tests/RestaurantPos.Api.Tests/PrintingConfigEndpointsTests.cs` :

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class PrintingConfigEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;
    public PrintingConfigEndpointsTests(PosApiApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string pin)
    {
        var client = _factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest(pin))).Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    private async Task<string> CreateCategoryAsync(HttpClient admin, string? station)
    {
        var res = await admin.PostAsJsonAsync("/api/catalog/categories", new { name = "Famille " + Guid.NewGuid().ToString("N")[..6], colorHex = "#123456", displayOrder = 9, iconName = (string?)null, preparationStationId = station });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private static async Task<string?> StationOfAsync(HttpClient client, string id) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/catalog/categories"))
            .EnumerateArray().Single(c => c.GetProperty("id").GetString() == id).GetProperty("preparationStationId").GetString();

    private async Task<Guid> AnyPrinterIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var printer = await db.PrinterConfigurations.FirstOrDefaultAsync();
        if (printer is not null) return printer.Id;
        printer = new PrinterConfiguration { Name = "Caisse test", IpAddress = "10.0.0.2", AssignedStationIds = ["RECEIPT"] };
        db.PrinterConfigurations.Add(printer);
        await db.SaveChangesAsync();
        return printer.Id;
    }

    [Fact]
    public async Task PutCategory_WithStation_IsReturnedByGet()
    {
        var admin = await ClientAsync("9999");
        var id = await CreateCategoryAsync(admin, null);
        (await admin.PutAsJsonAsync($"/api/catalog/categories/{id}", new { name = "Bar", colorHex = "#123456", displayOrder = 9, isActive = true, preparationStationId = "BAR" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await StationOfAsync(admin, id)).Should().Be("BAR");
    }

    [Fact]
    public async Task PutCategory_WithoutStation_KeepsIt_EmptyClearsIt()
    {
        var admin = await ClientAsync("9999");
        var id = await CreateCategoryAsync(admin, "DESSERT");
        await admin.PutAsJsonAsync($"/api/catalog/categories/{id}", new { name = "Renommée", colorHex = "#123456", displayOrder = 9, isActive = true });
        (await StationOfAsync(admin, id)).Should().Be("DESSERT");
        await admin.PutAsJsonAsync($"/api/catalog/categories/{id}", new { name = "Renommée", colorHex = "#123456", displayOrder = 9, isActive = true, preparationStationId = "" });
        (await StationOfAsync(admin, id)).Should().BeNull();
    }

    [Theory]
    [InlineData("RECEIPT")]
    [InlineData("STATION-HOT")]
    public async Task Category_InvalidStation_Returns400(string station)
    {
        var admin = await ClientAsync("9999");
        var res = await admin.PostAsJsonAsync("/api/catalog/categories", new { name = "X", colorHex = "#123456", displayOrder = 1, preparationStationId = station });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Device_ReceiptPrinter_SetListedAndCleared()
    {
        var admin = await ClientAsync("9999");
        var device = await DeviceTestHelper.PairAsync(_factory, _factory.CreateClient(), "Caisse imprimante");
        var printerId = await AnyPrinterIdAsync();

        (await admin.PutAsJsonAsync($"/api/devices/{device.DeviceId}/receipt-printer", new SetReceiptPrinterRequest(printerId))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/devices");
        list.EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == device.DeviceId).GetProperty("receiptPrinterId").GetGuid().Should().Be(printerId);

        (await admin.PutAsJsonAsync($"/api/devices/{device.DeviceId}/receipt-printer", new SetReceiptPrinterRequest(null))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Device_ReceiptPrinter_UnknownPrinterOrDevice_Returns404()
    {
        var admin = await ClientAsync("9999");
        var device = await DeviceTestHelper.PairAsync(_factory, _factory.CreateClient(), "Caisse 404");
        (await admin.PutAsJsonAsync($"/api/devices/{device.DeviceId}/receipt-printer", new SetReceiptPrinterRequest(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PutAsJsonAsync($"/api/devices/{Guid.NewGuid()}/receipt-printer", new SetReceiptPrinterRequest(null))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Device_ReceiptPrinter_WaiterForbidden()
    {
        var waiter = await ClientAsync("2468");
        (await waiter.PutAsJsonAsync($"/api/devices/{Guid.NewGuid()}/receipt-printer", new SetReceiptPrinterRequest(null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

(`device.DeviceId` : nom de la propriété renvoyée par `IDeviceService.PairAsync` ; ajuster s'il diffère.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RestaurantPos.Api.Tests --filter "FullyQualifiedName~SettingsEndpointsTests|FullyQualifiedName~PrintingConfigEndpointsTests"`
Expected: échec de compilation (`SetReceiptPrinterRequest`, surcharge `UpdateRestaurantSettingsRequest`).

- [ ] **Step 3: Implement**

Réglages :

```csharp
// IRestaurantSettingsService.cs
public record RestaurantSettingsDto(string ReceiptLanguage, string KitchenTicketLanguage);
/// <param name="KitchenTicketLanguage">null = inchangé (clients antérieurs au sous-projet C).</param>
public record UpdateRestaurantSettingsRequest(string ReceiptLanguage, string? KitchenTicketLanguage = null);
```

```csharp
// RestaurantSettingsService.cs
public async Task<RestaurantSettingsDto> GetAsync(CancellationToken ct = default) =>
    ToDto(await LoadOrCreateAsync(ct).ConfigureAwait(false));

public async Task<RestaurantSettingsDto?> UpdateAsync(UpdateRestaurantSettingsRequest request, CancellationToken ct = default)
{
    ArgumentNullException.ThrowIfNull(request);
    if (!IsSupported(request.ReceiptLanguage) || (request.KitchenTicketLanguage is { } k && !IsSupported(k))) return null;
    var settings = await LoadOrCreateAsync(ct).ConfigureAwait(false);
    settings.ReceiptLanguage = request.ReceiptLanguage;
    if (request.KitchenTicketLanguage is not null) settings.KitchenTicketLanguage = request.KitchenTicketLanguage;
    settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    return ToDto(settings);
}

private static bool IsSupported(string language) => Texts.SupportedLanguages.Contains(language, StringComparer.Ordinal);
private static RestaurantSettingsDto ToDto(RestaurantSettings s) => new(s.ReceiptLanguage, s.KitchenTicketLanguage);
```

`SettingsEndpoints` : inchangé (le message `errors.receipt_language_invalid` couvre les deux champs).

Catégories — records `Program.cs` :

```csharp
public record CreateCategoryRequest(string Name, string? ColorHex, int DisplayOrder, string? IconName, string? PreparationStationId = null);
public record UpdateCategoryRequest(string Name, string? ColorHex, int DisplayOrder, string? IconName, bool? IsActive, string? PreparationStationId = null);
```

`BackOfficeCatalogService` : `CreateCategoryAsync(..., string? iconName, string? preparationStationId = null, CancellationToken ct = default)` pose `PreparationStationId = PreparationStations.Normalize(preparationStationId)` ; `UpdateCategoryAsync(..., bool isActive, string? preparationStationId = null, CancellationToken ct = default)` ajoute `if (preparationStationId is not null) category.PreparationStationId = PreparationStations.Normalize(preparationStationId);`. Mettre à jour l'interface. Les appels qui passaient `ct` en positionnel ne compilent plus → les passer en nommé (`ct: ct`).

`CatalogEndpoints` :

```csharp
group.MapPost("/categories", async (CreateCategoryRequest req, IBackOfficeCatalogService catalog) =>
{
    if (!PreparationStations.IsKitchenStationOrEmpty(req.PreparationStationId))
        return Results.BadRequest(new { Message = Texts.T("errors.preparation_station_invalid") });
    var created = await catalog.CreateCategoryAsync(req.Name, req.ColorHex, req.DisplayOrder, req.IconName, req.PreparationStationId);
    return Results.Ok(created);
}).RequireAuthorization("RequireManagerOrAdmin");

group.MapPut("/categories/{id}", async (string id, UpdateCategoryRequest req, IBackOfficeCatalogService catalog) =>
{
    if (!PreparationStations.IsKitchenStationOrEmpty(req.PreparationStationId))
        return Results.BadRequest(new { Message = Texts.T("errors.preparation_station_invalid") });
    var updated = await catalog.UpdateCategoryAsync(id, req.Name, req.ColorHex, req.DisplayOrder, req.IconName, req.IsActive ?? true, req.PreparationStationId);
    return Results.Ok(updated);
}).RequireAuthorization("RequireManagerOrAdmin");
```

Appareils :

```csharp
// DeviceDtos.cs
public record DeviceDto(Guid Id, string Name, string Role, string TerminalId, DateTimeOffset PairedAtUtc, DateTimeOffset? LastSeenUtc, bool IsRevoked, Guid? ReceiptPrinterId);
public record SetReceiptPrinterRequest(Guid? PrinterId);

// IDeviceService.cs
/// <returns>false si l'appareil ou l'imprimante n'existe pas.</returns>
Task<bool> SetReceiptPrinterAsync(Guid deviceId, Guid? printerId, CancellationToken ct = default);

// DeviceService.cs
public async Task<bool> SetReceiptPrinterAsync(Guid deviceId, Guid? printerId, CancellationToken ct = default)
{
    var device = await _db.Devices.FindAsync([deviceId], ct).ConfigureAwait(false);
    if (device is null) return false;
    if (printerId is { } id && await _db.PrinterConfigurations.FindAsync([id], ct).ConfigureAwait(false) is null) return false;
    device.ReceiptPrinterId = printerId;
    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    return true;
}
```

`DeviceEndpoints` : ajouter `d.ReceiptPrinterId` en fin du `new DeviceDto(...)` de la liste et :

```csharp
group.MapPut("/{id:guid}/receipt-printer", async (Guid id, SetReceiptPrinterRequest req, IDeviceService devices, CancellationToken ct) =>
    await devices.SetReceiptPrinterAsync(id, req.PrinterId, ct)
        ? Results.NoContent()
        : Results.NotFound(new { Message = Texts.T("errors.receipt_printer_invalid") }))
    .RequireAuthorization("RequireManagerOrAdmin");
```

`Program.cs:676` : `public record PaymentSettlementRequest(Guid OrderId, string? TableNumber, Guid? OperatorId, List<TenderItemRequest> Tenders, string? TerminalId = null, bool RequestReceiptPrint = false);`

`.resx` (même format `<data name="…" xml:space="preserve"><value>…</value></data>`) :

| Clé | en | fr | ar |
|---|---|---|---|
| `errors.preparation_station_invalid` | Unknown preparation station. | Poste de préparation inconnu. | محطة التحضير غير معروفة. |
| `errors.receipt_printer_invalid` | Device or printer not found. | Appareil ou imprimante introuvable. | الجهاز أو الطابعة غير موجود. |

- [ ] **Step 4: Run tests**

Run: `dotnet format RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS (y compris `TextsTests` sur la parité des clés).

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(impression): langue des bons cuisine, poste des familles, imprimante des caisses"
```

---

### Task 3: Sérialisation des tickets et rendu raster

**Files:**
- Modify: `src/RestaurantPos.Infrastructure/Printing/TicketDocument.cs`
- Create: `src/RestaurantPos.Infrastructure/Printing/TicketDocumentJson.cs`
- Create: `src/RestaurantPos.Infrastructure/Printing/EscPosRasterRenderer.cs`
- Create: `src/RestaurantPos.Infrastructure/Printing/Fonts/{NotoSans-Regular,NotoSans-Bold,NotoSansArabic-Regular,NotoSansArabic-Bold}.ttf`, `Fonts/OFL.txt`
- Modify: `src/RestaurantPos.Infrastructure/RestaurantPos.Infrastructure.csproj`
- Test: `tests/RestaurantPos.Infrastructure.Tests/EscPosRasterRendererTests.cs`, `TicketDocumentJsonTests.cs`

**Interfaces:**
- Produces: `TicketDocumentJson.Serialize(TicketDocument) : string`, `TicketDocumentJson.Deserialize(string) : TicketDocument` ; `MonoImage(int Width, int Height, byte[] Bits)` (`BytesPerRow = Width / 8`, bit 1 = noir, MSB = pixel de gauche, lignes consécutives) ; `EscPosRasterRenderer.DotsFor(int paperWidthMm) : int` ; `EscPosRasterRenderer.Render(TicketDocument doc, int widthDots) : IReadOnlyList<MonoImage>` (un segment par `TicketSeparator(Cut: true)`, segments vides ignorés).

- [ ] **Step 1: Paquets et polices**

```bash
cd src/RestaurantPos.Infrastructure
dotnet add package Topten.RichTextKit
dotnet list package --include-transitive | grep -Ei "skiasharp|harfbuzz"
```
Noter les versions de `SkiaSharp` et `HarfBuzzSharp` tirées par RichTextKit, puis ajouter ces paquets **aux mêmes versions** :

```bash
dotnet add package SkiaSharp --version <version SkiaSharp listée>
dotnet add package SkiaSharp.NativeAssets.Linux.NoDependencies --version <version SkiaSharp listée>
dotnet add package HarfBuzzSharp.NativeAssets.Linux --version <version HarfBuzzSharp listée>
cd ../..
```

Polices (licence OFL) :

```bash
F=src/RestaurantPos.Infrastructure/Printing/Fonts; mkdir -p $F
B=https://github.com/notofonts/notofonts.github.io/raw/main/fonts
curl -fL -o $F/NotoSans-Regular.ttf        $B/NotoSans/hinted/ttf/NotoSans-Regular.ttf
curl -fL -o $F/NotoSans-Bold.ttf           $B/NotoSans/hinted/ttf/NotoSans-Bold.ttf
curl -fL -o $F/NotoSansArabic-Regular.ttf  $B/NotoSansArabic/hinted/ttf/NotoSansArabic-Regular.ttf
curl -fL -o $F/NotoSansArabic-Bold.ttf     $B/NotoSansArabic/hinted/ttf/NotoSansArabic-Bold.ttf
curl -fL -o $F/OFL.txt                     https://raw.githubusercontent.com/notofonts/latin-greek-cyrillic/main/OFL.txt
file $F/*.ttf   # doit afficher « TrueType Font data » (pas du HTML)
```

Dans le `.csproj` :

```xml
<ItemGroup>
  <EmbeddedResource Include="Printing\Fonts\*.ttf" />
</ItemGroup>
```

Run: `dotnet build RestaurantPos.slnx` → 0 avertissement (si NU1608/NU1603 apparaît, aligner les versions sur celles exigées par RichTextKit).

- [ ] **Step 2: Write the failing tests**

`TicketDocumentJsonTests.cs` :

```csharp
using FluentAssertions;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class TicketDocumentJsonTests
{
    [Fact]
    public void RoundTrip_PreservesLineTypes()
    {
        var doc = new TicketDocument("ar", true,
        [
            new TicketText("سفري", TicketAlign.Center, Bold: true, Large: true),
            new TicketColumns("Total", "12.50"),
            new TicketSeparator(Cut: true)
        ]);
        var json = TicketDocumentJson.Serialize(doc);
        json.Should().Contain("\"type\":\"TicketText\"");
        TicketDocumentJson.Deserialize(json).Should().BeEquivalentTo(doc, o => o.RespectingRuntimeTypes());
    }
}
```

`EscPosRasterRendererTests.cs` :

```csharp
using System.Linq;
using System.Numerics;
using FluentAssertions;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class EscPosRasterRendererTests
{
    private static int Ink(MonoImage img) => img.Bits.Sum(b => BitOperations.PopCount(b));

    private static int InkInColumns(MonoImage img, int fromX, int toX)
    {
        var count = 0;
        for (var y = 0; y < img.Height; y++)
            for (var x = fromX; x < toX; x++)
                if ((img.Bits[y * img.BytesPerRow + x / 8] & (0x80 >> (x % 8))) != 0) count++;
        return count;
    }

    private static TicketDocument Doc(bool rtl, params TicketLine[] lines) => new(rtl ? "ar" : "fr", rtl, lines);

    [Theory]
    [InlineData(80, 576)]
    [InlineData(58, 384)]
    [InlineData(0, 576)]
    public void DotsFor_MapsPaperWidth(int mm, int dots) => EscPosRasterRenderer.DotsFor(mm).Should().Be(dots);

    [Theory]
    [InlineData(576)]
    [InlineData(384)]
    public void Render_UsesRequestedWidth_AndPositiveHeight(int width)
    {
        var img = EscPosRasterRenderer.Render(Doc(false, new TicketText("Crème brûlée"), new TicketColumns("Total", "12.50")), width).Single();
        img.Width.Should().Be(width);
        img.Height.Should().BeGreaterThan(0);
        img.Bits.Length.Should().Be(img.BytesPerRow * img.Height);
        Ink(img).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Render_ArabicText_ProducesInk()
    {
        var img = EscPosRasterRenderer.Render(Doc(true, new TicketText("استلام", TicketAlign.Center, Large: true)), 576).Single();
        Ink(img).Should().BeGreaterThan(200);
    }

    [Fact]
    public void Render_MixedArabicLatin_DrawsInkOnRightHalfForRtlStart()
    {
        var img = EscPosRasterRenderer.Render(Doc(true, new TicketText("طاولة T05")), 576).Single();
        InkInColumns(img, 288, 576).Should().BeGreaterThan(0);
        InkInColumns(img, 0, 200).Should().Be(0);
    }

    [Fact]
    public void Render_LtrStart_DrawsInkOnLeftOnly()
    {
        var img = EscPosRasterRenderer.Render(Doc(false, new TicketText("Table 5")), 576).Single();
        InkInColumns(img, 0, 200).Should().BeGreaterThan(0);
        InkInColumns(img, 376, 576).Should().Be(0);
    }

    [Fact]
    public void Render_CutSeparator_SplitsSegments_IgnoringEmpty()
    {
        var images = EscPosRasterRenderer.Render(Doc(false, new TicketText("A"), new TicketSeparator(Cut: true), new TicketText("B"), new TicketSeparator(Cut: true)), 576);
        images.Should().HaveCount(2);
    }

    [Fact]
    public void Render_EmptyText_DoesNotThrow()
    {
        var act = () => EscPosRasterRenderer.Render(Doc(true, new TicketText(""), new TicketColumns("", ""), new TicketSeparator()), 384);
        act.Should().NotThrow();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "FullyQualifiedName~EscPosRasterRendererTests|FullyQualifiedName~TicketDocumentJsonTests"`
Expected: échec de compilation.

- [ ] **Step 4: Implement**

`TicketDocument.cs` — attributs sur `TicketLine` :

```csharp
using System.Text.Json.Serialization;
// …
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TicketText), nameof(TicketText))]
[JsonDerivedType(typeof(TicketColumns), nameof(TicketColumns))]
[JsonDerivedType(typeof(TicketSeparator), nameof(TicketSeparator))]
public abstract record TicketLine;
```

`TicketDocumentJson.cs` :

```csharp
using System.Text.Json;

namespace RestaurantPos.Infrastructure.Printing;

public static class TicketDocumentJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(TicketDocument document) => JsonSerializer.Serialize(document, Options);

    public static TicketDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<TicketDocument>(json, Options) ?? throw new JsonException("Ticket vide");
}
```

`EscPosRasterRenderer.cs` :

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;
using Topten.RichTextKit;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>Image 1 bit prête pour GS v 0 : bit 1 = point noir, MSB = pixel de gauche.</summary>
public sealed record MonoImage(int Width, int Height, byte[] Bits)
{
    public int BytesPerRow => Width / 8;
}

/// <summary>Dessine un TicketDocument en image monochrome (mise en forme bidi et liaison arabe par RichTextKit/HarfBuzz).</summary>
public static class EscPosRasterRenderer
{
    private const int Margin = 8;
    private const float NormalSize = 24f;
    private const float LargeSize = 40f;
    private const string Latin = "Noto Sans";
    private const string Arabic = "Noto Sans Arabic";

    static EscPosRasterRenderer() => FontMapper.Default = EmbeddedFontMapper.Instance;

    public static int DotsFor(int paperWidthMm) => paperWidthMm == 58 ? 384 : 576;

    public static IReadOnlyList<MonoImage> Render(TicketDocument doc, int widthDots)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var segments = new List<List<TicketLine>> { new() };
        foreach (var line in doc.Lines)
        {
            if (line is TicketSeparator { Cut: true }) segments.Add([]);
            else segments[^1].Add(line);
        }
        return segments.Where(s => s.Count > 0).Select(s => RenderSegment(s, doc.RightToLeft, widthDots)).ToList();
    }

    private sealed record Block(float Height, Action<SKCanvas, float> Draw);

    private static MonoImage RenderSegment(List<TicketLine> lines, bool rtl, int width)
    {
        float content = width - 2 * Margin;
        var blocks = lines.Select(l => Layout(l, rtl, content)).ToList();
        var height = (int)Math.Ceiling(blocks.Sum(b => b.Height)) + 2 * Margin;
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            float y = Margin;
            foreach (var block in blocks)
            {
                block.Draw(canvas, y);
                y += block.Height;
            }
        }
        return Threshold(bitmap);
    }

    private static Block Layout(TicketLine line, bool rtl, float content) => line switch
    {
        TicketText t => TextBlockOf(Text(t.Text, t.Bold, t.Large, Map(t.Align, rtl), rtl, content), Margin),
        TicketColumns c => Columns(c, rtl, content),
        _ => new Block(16, (canvas, y) =>
        {
            using var paint = new SKPaint { Color = SKColors.Black, StrokeWidth = 2, PathEffect = SKPathEffect.CreateDash([6, 4], 0) };
            canvas.DrawLine(Margin, y + 8, Margin + content, y + 8, paint);
        })
    };

    private static Block TextBlockOf(TextBlock tb, float x) =>
        new(tb.MeasuredHeight, (canvas, y) => tb.Paint(canvas, new SKPoint(x, y)));

    private static Block Columns(TicketColumns c, bool rtl, float content)
    {
        var value = Text(c.Value, false, false, rtl ? TextAlignment.Left : TextAlignment.Right, rtl, content);
        var labelWidth = Math.Max(content / 3, content - value.MeasuredWidth - 16);
        var label = Text(c.Label, false, false, rtl ? TextAlignment.Right : TextAlignment.Left, rtl, labelWidth);
        var labelX = rtl ? Margin + content - labelWidth : Margin;
        return new Block(Math.Max(label.MeasuredHeight, value.MeasuredHeight), (canvas, y) =>
        {
            label.Paint(canvas, new SKPoint(labelX, y));
            value.Paint(canvas, new SKPoint(Margin, y));
        });
    }

    private static TextAlignment Map(TicketAlign align, bool rtl) => align switch
    {
        TicketAlign.Center => TextAlignment.Center,
        TicketAlign.End => rtl ? TextAlignment.Left : TextAlignment.Right,
        _ => rtl ? TextAlignment.Right : TextAlignment.Left
    };

    private static TextBlock Text(string text, bool bold, bool large, TextAlignment align, bool rtl, float maxWidth)
    {
        var tb = new TextBlock { MaxWidth = maxWidth, Alignment = align, BaseDirection = rtl ? TextDirection.RTL : TextDirection.LTR };
        foreach (var (run, arabic) in ScriptRuns(string.IsNullOrEmpty(text) ? " " : text))
        {
            tb.AddText(run, new Style
            {
                FontFamily = arabic ? Arabic : Latin,
                FontSize = large ? LargeSize : NormalSize,
                FontWeight = bold || large ? 700 : 400,
                TextColor = SKColors.Black
            });
        }
        return tb;
    }

    // Noto Sans n'a pas l'arabe et Noto Sans Arabic pas le latin : découpage en runs par écriture, les espaces suivent le run courant.
    private static IEnumerable<(string Run, bool Arabic)> ScriptRuns(string text)
    {
        var start = 0;
        var current = IsArabic(text[0]);
        for (var i = 1; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]) || IsArabic(text[i]) == current) continue;
            yield return (text[start..i], current);
            start = i;
            current = !current;
        }
        yield return (text[start..], current);
    }

    private static bool IsArabic(char ch) =>
        ch is (>= '؀' and <= 'ۿ') or (>= 'ݐ' and <= 'ݿ') or (>= 'ࢠ' and <= 'ࣿ')
            or (>= 'ﭐ' and <= '﷿') or (>= 'ﹰ' and <= '﻿');

    private static MonoImage Threshold(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var bytesPerRow = width / 8;
        var bits = new byte[bytesPerRow * bitmap.Height];
        var pixels = bitmap.GetPixelSpan();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = (y * width + x) * 4; // BGRA
                var luminance = (pixels[p + 2] * 299 + pixels[p + 1] * 587 + pixels[p] * 114) / 1000;
                if (luminance < 128) bits[y * bytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }
        return new MonoImage(width, bitmap.Height, bits);
    }

    private sealed class EmbeddedFontMapper : FontMapper
    {
        public static readonly EmbeddedFontMapper Instance = new();
        private readonly Dictionary<string, SKTypeface> _faces = new(StringComparer.Ordinal)
        {
            [$"{Latin}|400"] = Load("NotoSans-Regular.ttf"),
            [$"{Latin}|700"] = Load("NotoSans-Bold.ttf"),
            [$"{Arabic}|400"] = Load("NotoSansArabic-Regular.ttf"),
            [$"{Arabic}|700"] = Load("NotoSansArabic-Bold.ttf")
        };

        public override SKTypeface TypefaceFromStyle(IStyle style, bool ignoreFontVariants) =>
            _faces[$"{(style.FontFamily == Arabic ? Arabic : Latin)}|{(style.FontWeight >= 600 ? 700 : 400)}"];

        private static SKTypeface Load(string file)
        {
            using var stream = typeof(EscPosRasterRenderer).Assembly.GetManifestResourceStream("RestaurantPos.Infrastructure.Printing.Fonts." + file)
                ?? throw new InvalidOperationException("Police absente : " + file);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return SKTypeface.FromData(SKData.CreateCopy(memory.ToArray()));
        }
    }
}
```

Si un nom d'API RichTextKit diffère dans la version installée (ex. `MeasuredHeight`, `TextAlignment`, `TextDirection`), corriger d'après la version installée sans changer le comportement.

- [ ] **Step 5: Run tests**

Run: `dotnet format RestaurantPos.slnx && dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "FullyQualifiedName~EscPosRasterRendererTests|FullyQualifiedName~TicketDocumentJsonTests|FullyQualifiedName~TicketDocumentBuilderTests"`
Expected: PASS.

- [ ] **Step 6: Contrôle visuel (non commité)**

Dans un test temporaire, rendre `TicketDocumentBuilder.FiscalReceipt(...)` en `ar` et en `fr`, écrire chaque `MonoImage` en PBM (`P4\n{Width} {Height}\n` + `Bits`) dans `/tmp`, ouvrir les fichiers : lettres arabes liées, alignement à droite en RTL, montants à gauche en arabe, accents français corrects. Supprimer le test temporaire.

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat(impression): rendu raster des tickets (SkiaSharp, RichTextKit, polices Noto)"
```

---

### Task 4: Commandes ESC/POS, envoi TCP, impression de test migrée

**Files:**
- Create: `src/RestaurantPos.Infrastructure/Printing/EscPosCommands.cs`, `EscPosSender.cs`, `PrinterTransport.cs`
- Modify: `src/RestaurantPos.Infrastructure/Printing/TicketDocumentBuilder.cs` (ajout `TestPage`)
- Modify: `src/RestaurantPos.Infrastructure/Services/PrinterConfigurationService.cs` (ctor, `SendTestPrintAsync`, suppression `BuildTestPrintPayload`)
- Modify: `src/RestaurantPos.Api/Program.cs:81` (DI)
- Modify: `.resx` ×3
- Test: `tests/RestaurantPos.Infrastructure.Tests/EscPosCommandsTests.cs`, `EscPosSenderTests.cs`, `PrinterConfigurationServiceTests.cs`, `TicketDocumentBuilderTests.cs`

**Interfaces:**
- Consumes: Task 3 (`MonoImage`, `EscPosRasterRenderer`).
- Produces: `EscPosCommands.BandHeight = 256`, `EscPosCommands.Build(IReadOnlyList<MonoImage> segments, bool openCashDrawer) : byte[]` ; `EscPosSender.SendAsync(string host, int port, byte[] payload, CancellationToken ct, TimeSpan? connectTimeout = null, TimeSpan? writeTimeout = null) : Task` ; `IPrinterTransport.SendAsync(PrinterConfiguration printer, TicketDocument document, bool openCashDrawer, CancellationToken ct) : Task` (lève en cas d'échec) ; `EscPosPrinterTransport : IPrinterTransport` ; `TicketDocumentBuilder.TestPage(PrinterConfiguration printer, string language, DateTimeOffset nowUtc) : TicketDocument` ; `PrinterConfigurationService(AppDbContext, IPrinterTransport, IRestaurantSettingsService)`.

- [ ] **Step 1: Write the failing tests**

`EscPosCommandsTests.cs` :

```csharp
using System;
using System.Linq;
using FluentAssertions;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class EscPosCommandsTests
{
    private static MonoImage Image(int width, int height) => new(width, height, Enumerable.Repeat((byte)0xAA, width / 8 * height).ToArray());

    [Fact]
    public void Build_SplitsIntoBandsOf256_LastBandPartial()
    {
        var bytes = EscPosCommands.Build([Image(576, 600)], openCashDrawer: false);

        bytes.Take(2).Should().Equal((byte)0x1B, (byte)0x40);
        var headers = Enumerable.Range(0, bytes.Length - 3).Where(i => bytes[i] == 0x1D && bytes[i + 1] == 0x76 && bytes[i + 2] == 0x30).ToList();
        headers.Should().HaveCount(3);
        headers.Select(i => bytes[i + 6] | (bytes[i + 7] << 8)).Should().Equal(256, 256, 88);
        headers.Select(i => bytes[i + 4] | (bytes[i + 5] << 8)).Should().AllBeEquivalentTo(72);
        bytes.TakeLast(4).Should().Equal((byte)0x1D, (byte)0x56, (byte)0x42, (byte)0x00);
        bytes.Length.Should().Be(2 + 3 * 8 + 72 * 600 + 4);
    }

    [Fact]
    public void Build_Drawer_KickBeforeFinalCutOnly()
    {
        var bytes = EscPosCommands.Build([Image(384, 10), Image(384, 10)], openCashDrawer: true);
        byte[] kick = [0x1B, 0x70, 0x00, 0x19, 0xFA];
        byte[] cut = [0x1D, 0x56, 0x42, 0x00];
        bytes.TakeLast(9).Should().Equal(kick.Concat(cut));
        CountOf(bytes, kick).Should().Be(1);
        CountOf(bytes, cut).Should().Be(2);
    }

    [Fact]
    public void Build_NoDrawer_NoKick() =>
        CountOf(EscPosCommands.Build([Image(384, 10)], false), [0x1B, 0x70, 0x00, 0x19, 0xFA]).Should().Be(0);

    private static int CountOf(byte[] haystack, byte[] needle) =>
        Enumerable.Range(0, haystack.Length - needle.Length + 1).Count(i => haystack.AsSpan(i, needle.Length).SequenceEqual(needle));
}
```

(Les octets `0xAA` des données d'image ne forment pas la séquence `1D 76 30` ; si un autre motif de remplissage est choisi, vérifier qu'il ne crée pas de faux en-têtes.)

`EscPosSenderTests.cs` :

```csharp
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class EscPosSenderTests
{
    [Fact]
    public async Task SendAsync_DeliversExactBytes()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var received = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var ms = new MemoryStream();
                await client.GetStream().CopyToAsync(ms);
                return ms.ToArray();
            });

            byte[] payload = [0x1B, 0x40, 1, 2, 3, 0x1D, 0x56, 0x42, 0x00];
            await EscPosSender.SendAsync("127.0.0.1", port, payload, CancellationToken.None);

            (await received.WaitAsync(TimeSpan.FromSeconds(5))).Should().Equal(payload);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task SendAsync_ConnectionRefused_Throws()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var act = () => EscPosSender.SendAsync("127.0.0.1", port, [0x1B, 0x40], CancellationToken.None);
        await act.Should().ThrowAsync<SocketException>();
    }

    [Fact]
    public async Task SendAsync_PeerNeverReads_TimesOut()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = listener.AcceptTcpClientAsync();
            var act = () => EscPosSender.SendAsync("127.0.0.1", port, new byte[64 * 1024 * 1024], CancellationToken.None, writeTimeout: TimeSpan.FromMilliseconds(300));
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { listener.Stop(); }
    }
}
```

`TicketDocumentBuilderTests` — ajouter :

```csharp
[Theory]
[InlineData("en", "PRINT TEST")]
[InlineData("fr", "TEST D'IMPRESSION")]
[InlineData("ar", "اختبار الطباعة")]
public void TestPage_IsLocalized_AndShowsPrinter(string lang, string title)
{
    var printer = new PrinterConfiguration { Name = "Cuisine", IpAddress = "10.0.0.5", Port = 9100, PaperWidthMm = 58 };
    var text = AllText(TicketDocumentBuilder.TestPage(printer, lang, DateTimeOffset.UnixEpoch));
    text.Should().Contain(title).And.Contain("Cuisine").And.Contain("10.0.0.5:9100").And.Contain("58 mm");
}
```

`PrinterConfigurationServiceTests` — ajouter un faux transport et construire le service avec :

```csharp
private sealed class FakeTransport : IPrinterTransport
{
    public Exception? Failure { get; set; }
    public List<(PrinterConfiguration Printer, TicketDocument Document, bool Drawer)> Sent { get; } = [];
    public Task SendAsync(PrinterConfiguration printer, TicketDocument document, bool openCashDrawer, CancellationToken ct)
    {
        if (Failure is not null) throw Failure;
        Sent.Add((printer, document, openCashDrawer));
        return Task.CompletedTask;
    }
}
```

`new PrinterConfigurationService(db, transport, new RestaurantSettingsService(db))` partout dans le fichier, puis :

```csharp
[Fact]
public async Task SendTestPrint_RendersTestPage_InReceiptLanguage()
{
    var (db, transport, service) = Create();   // helper local : InMemory + FakeTransport
    await new RestaurantSettingsService(db).UpdateAsync(new UpdateRestaurantSettingsRequest("ar"));
    var printer = await service.RegisterPrinterAsync(new PrinterRegistrationRequest("Caisse", "10.0.0.3", 9100, 80, true, ["RECEIPT"]));

    var result = await service.SendTestPrintAsync(printer.Id);

    result.Success.Should().BeTrue();
    transport.Sent.Single().Document.Language.Should().Be("ar");
    transport.Sent.Single().Drawer.Should().BeTrue();
}

[Fact]
public async Task SendTestPrint_TransportFailure_ReturnsFailure()
{
    var (_, transport, service) = Create();
    var printer = await service.RegisterPrinterAsync(new PrinterRegistrationRequest("Caisse", "10.0.0.3", 9100, 80, false, ["RECEIPT"]));
    transport.Failure = new SocketException((int)SocketError.ConnectionRefused);

    (await service.SendTestPrintAsync(printer.Id)).Success.Should().BeFalse();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "FullyQualifiedName~EscPos|FullyQualifiedName~PrinterConfigurationServiceTests|FullyQualifiedName~TicketDocumentBuilderTests"`
Expected: échec de compilation.

- [ ] **Step 3: Implement**

`EscPosCommands.cs` :

```csharp
using System;
using System.Collections.Generic;

namespace RestaurantPos.Infrastructure.Printing;

public static class EscPosCommands
{
    public const int BandHeight = 256;
    private static readonly byte[] Init = [0x1B, 0x40];
    private static readonly byte[] DrawerKick = [0x1B, 0x70, 0x00, 0x19, 0xFA];
    private static readonly byte[] FeedAndCut = [0x1D, 0x56, 0x42, 0x00];

    /// <summary>ESC @, puis pour chaque segment : bandes GS v 0 de 256 lignes et coupe ; impulsion tiroir avant la dernière coupe.</summary>
    public static byte[] Build(IReadOnlyList<MonoImage> segments, bool openCashDrawer)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var bytes = new List<byte>(Init);
        for (var i = 0; i < segments.Count; i++)
        {
            var img = segments[i];
            for (var top = 0; top < img.Height; top += BandHeight)
            {
                var rows = Math.Min(BandHeight, img.Height - top);
                bytes.AddRange([0x1D, 0x76, 0x30, 0x00, (byte)(img.BytesPerRow & 0xFF), (byte)(img.BytesPerRow >> 8), (byte)(rows & 0xFF), (byte)(rows >> 8)]);
                bytes.AddRange(img.Bits.AsSpan(top * img.BytesPerRow, rows * img.BytesPerRow).ToArray());
            }
            if (openCashDrawer && i == segments.Count - 1) bytes.AddRange(DrawerKick);
            bytes.AddRange(FeedAndCut);
        }
        return [.. bytes];
    }
}
```

`EscPosSender.cs` :

```csharp
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Infrastructure.Printing;

public static class EscPosSender
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);

    public static async Task SendAsync(string host, int port, byte[] payload, CancellationToken ct, TimeSpan? connectTimeout = null, TimeSpan? writeTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var client = new TcpClient();
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connect.CancelAfter(connectTimeout ?? ConnectTimeout);
            await client.ConnectAsync(host, port, connect.Token).ConfigureAwait(false);
        }
        using var write = CancellationTokenSource.CreateLinkedTokenSource(ct);
        write.CancelAfter(writeTimeout ?? WriteTimeout);
        var stream = client.GetStream();
        await stream.WriteAsync(payload, write.Token).ConfigureAwait(false);
        await stream.FlushAsync(write.Token).ConfigureAwait(false);
    }
}
```

`PrinterTransport.cs` :

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>Envoie un ticket à une imprimante ; lève en cas d'échec (réseau, délai).</summary>
public interface IPrinterTransport
{
    Task SendAsync(PrinterConfiguration printer, TicketDocument document, bool openCashDrawer, CancellationToken ct);
}

public sealed class EscPosPrinterTransport : IPrinterTransport
{
    public Task SendAsync(PrinterConfiguration printer, TicketDocument document, bool openCashDrawer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(printer);
        var images = EscPosRasterRenderer.Render(document, EscPosRasterRenderer.DotsFor(printer.PaperWidthMm));
        return EscPosSender.SendAsync(printer.IpAddress, printer.Port, EscPosCommands.Build(images, openCashDrawer), ct);
    }
}
```

`TicketDocumentBuilder.TestPage` :

```csharp
public static TicketDocument TestPage(PrinterConfiguration printer, string language, DateTimeOffset nowUtc)
{
    ArgumentNullException.ThrowIfNull(printer);
    var (lang, c) = Resolve(language);
    List<TicketLine> lines =
    [
        new TicketText(Texts.Get(c, "receipt.test_title"), TicketAlign.Center, Bold: true, Large: true),
        new TicketSeparator(),
        new TicketColumns(Texts.Get(c, "receipt.test_printer"), printer.Name),
        new TicketColumns(Texts.Get(c, "receipt.test_address"), $"{printer.IpAddress}:{printer.Port}"),
        new TicketColumns(Texts.Get(c, "receipt.test_paper"), $"{printer.PaperWidthMm} mm"),
        new TicketColumns(Texts.Get(c, "receipt.date"), nowUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)),
        new TicketSeparator(),
        // Échantillon fixe : vérifie accents et liaison arabe quelle que soit la langue.
        new TicketText("àâçéèêëîïôùû ÀÉÈ — مرحبا بكم — 0123456789", TicketAlign.Center)
    ];
    return new TicketDocument(lang, lang == "ar", lines);
}
```

`PrinterConfigurationService` :

```csharp
private readonly AppDbContext _dbContext;
private readonly IPrinterTransport _transport;
private readonly IRestaurantSettingsService _settings;

public PrinterConfigurationService(AppDbContext dbContext, IPrinterTransport transport, IRestaurantSettingsService settings)
{
    _dbContext = dbContext;
    _transport = transport;
    _settings = settings;
}

public async Task<TestPrintResult> SendTestPrintAsync(Guid printerId, CancellationToken ct = default)
{
    var printer = await _dbContext.PrinterConfigurations.FindAsync([printerId], ct).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Imprimante introuvable: {printerId}");
    var language = (await _settings.GetAsync(ct).ConfigureAwait(false)).ReceiptLanguage;
    var sw = Stopwatch.StartNew();
    try
    {
        var page = TicketDocumentBuilder.TestPage(printer, language, DateTimeOffset.UtcNow);
        await _transport.SendAsync(printer, page, printer.OpenCashDrawerOnReceipt, ct).ConfigureAwait(false);
        sw.Stop();
        return new TestPrintResult(true, Texts.T("messages.test_print_ok", ("name", printer.Name), ("ip", printer.IpAddress), ("port", printer.Port)), sw.Elapsed);
    }
    catch (Exception ex)
    {
        sw.Stop();
        return new TestPrintResult(false, Texts.T("errors.test_print_failed", ("ip", printer.IpAddress), ("port", printer.Port), ("error", ex.Message)), sw.Elapsed);
    }
}
```

Supprimer `BuildTestPrintPayload` et les `using` devenus inutiles (`System.Net.Sockets`, `System.Text`). `Program.cs` : `builder.Services.AddSingleton<IPrinterTransport, EscPosPrinterTransport>();` (à côté de `IPrinterConfigurationService`). Corriger les autres `new PrinterConfigurationService(` (`grep -rn "new PrinterConfigurationService" src tests`).

`.resx` :

| Clé | en | fr | ar |
|---|---|---|---|
| `receipt.test_title` | PRINT TEST | TEST D'IMPRESSION | اختبار الطباعة |
| `receipt.test_printer` | Printer | Imprimante | الطابعة |
| `receipt.test_address` | Address | Adresse | العنوان |
| `receipt.test_paper` | Paper | Papier | الورق |

- [ ] **Step 4: Run tests**

Run: `dotnet format RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(impression): envoi ESC/POS raster et impression de test localisée"
```

---

### Task 5: File d'impression, worker et état des imprimantes (SignalR)

**Files:**
- Create: `src/RestaurantPos.Infrastructure/Printing/PrintQueue.cs` (`PrintSignal`, `PrintQueue`)
- Create: `src/RestaurantPos.Infrastructure/Printing/PrinterStatusTracker.cs`
- Create: `src/RestaurantPos.Infrastructure/Printing/PrintQueueProcessor.cs` (+ `IPrinterStatusNotifier`)
- Create: `src/RestaurantPos.Infrastructure/Printing/PrintLog.cs`
- Create: `src/RestaurantPos.Api/Services/PrintWorker.cs`, `src/RestaurantPos.Api/Services/SignalRPrinterStatusNotifier.cs`
- Modify: `src/RestaurantPos.Api/Hubs/PosHub.cs:10-14`, `src/RestaurantPos.Api/Program.cs:74-102` (DI)
- Test: `tests/RestaurantPos.Infrastructure.Tests/PrintQueueProcessorTests.cs`

**Interfaces:**
- Consumes: Task 1 (`PrintJob`), Task 3 (`TicketDocumentJson`), Task 4 (`IPrinterTransport`).
- Produces:
  - `PrintSignal` (singleton) : `void Notify()`, `Task WaitAsync(TimeSpan timeout, CancellationToken ct)`.
  - `PrintQueue` (scoped) : `PrintQueue(AppDbContext db, PrintSignal signal, TimeProvider time)`, `Task EnqueueAsync(Guid printerId, PrintJobKind kind, TicketDocument document, bool openCashDrawer, CancellationToken ct = default)`.
  - `PrinterState(bool? IsOnline, DateTimeOffset? SinceUtc)` ; `PrinterStatusTracker` (singleton) : `PrinterState Get(Guid printerId)`, `bool Record(Guid printerId, bool success, DateTimeOffset nowUtc)` (true = transition à notifier).
  - `IPrinterStatusNotifier.PrinterStatusChangedAsync(Guid printerId, string printerName, bool isOnline, int pendingCount) : Task`.
  - `PrintQueueProcessor` (singleton) : `static TimeSpan RetryDelay(int attempts)`, `Task<IReadOnlyList<Guid>> PrepareAsync(CancellationToken ct)`, `Task ProcessPrinterAsync(Guid printerId, CancellationToken ct)`, `Task PurgeAsync(CancellationToken ct)`.
  - `PrintLog` (internal) : `SendFailed`, `NoPrinter`, `QueueFailed` (utilisé Task 8).
  - `IPosHubClient.OnPrinterStatusChanged(Guid printerId, string printerName, bool isOnline, int pendingCount)`.

- [ ] **Step 1: Write the failing tests**

`PrintQueueProcessorTests.cs` :

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class PrintQueueProcessorTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeTransport : IPrinterTransport
    {
        public HashSet<Guid> Offline { get; } = [];
        public List<(Guid PrinterId, string FirstLine)> Sent { get; } = [];
        public Task SendAsync(PrinterConfiguration printer, TicketDocument document, bool openCashDrawer, CancellationToken ct)
        {
            if (Offline.Contains(printer.Id)) throw new SocketException((int)SocketError.ConnectionRefused);
            Sent.Add((printer.Id, ((TicketText)document.Lines[0]).Text));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotifier : IPrinterStatusNotifier
    {
        public List<(Guid PrinterId, bool IsOnline, int Pending)> Events { get; } = [];
        public Task PrinterStatusChangedAsync(Guid printerId, string printerName, bool isOnline, int pendingCount)
        {
            Events.Add((printerId, isOnline, pendingCount));
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public ServiceProvider Services { get; }
        public Clock Clock { get; } = new();
        public FakeTransport Transport { get; } = new();
        public FakeNotifier Notifier { get; } = new();
        public PrintQueueProcessor Processor { get; private set; } = null!;

        public Harness()
        {
            var name = "PrintQueue_" + Guid.NewGuid().ToString("N");
            Services = new ServiceCollection()
                .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name))
                .BuildServiceProvider();
            Restart();
        }

        /// <summary>Nouvelle instance (état mémoire vierge), même base : simule un redémarrage du serveur.</summary>
        public void Restart() => Processor = new PrintQueueProcessor(
            Services.GetRequiredService<IServiceScopeFactory>(), Transport, new PrinterStatusTracker(), Notifier, Clock, NullLogger<PrintQueueProcessor>.Instance);

        public T Db<T>(Func<AppDbContext, T> use)
        {
            using var scope = Services.CreateScope();
            return use(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        public PrinterConfiguration AddPrinter(string name, bool active = true) => Db(db =>
        {
            var printer = new PrinterConfiguration { Name = name, IpAddress = "10.0.0.1", IsActive = active };
            db.PrinterConfigurations.Add(printer);
            db.SaveChanges();
            return printer;
        });

        public async Task EnqueueAsync(Guid printerId, string text)
        {
            using var scope = Services.CreateScope();
            var queue = new PrintQueue(scope.ServiceProvider.GetRequiredService<AppDbContext>(), new PrintSignal(), Clock);
            await queue.EnqueueAsync(printerId, PrintJobKind.KitchenTicket, new TicketDocument("fr", false, [new TicketText(text)]), false);
            Clock.Now = Clock.Now.AddMilliseconds(1);
        }

        public async Task CycleAsync()
        {
            foreach (var id in await Processor.PrepareAsync(CancellationToken.None))
                await Processor.ProcessPrinterAsync(id, CancellationToken.None);
        }

        public List<PrintJob> Jobs() => Db(db => db.PrintJobs.AsNoTracking().ToList().OrderBy(j => j.CreatedAtUtc).ToList());
    }

    [Fact]
    public async Task Enqueue_SetsDeadlineThirtyMinutes()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        var start = h.Clock.Now;
        await h.EnqueueAsync(p.Id, "A");
        var job = h.Jobs().Single();
        job.Status.Should().Be(PrintJobStatus.Pending);
        job.DeadlineAtUtc.Should().Be(start + TimeSpan.FromMinutes(30));
        job.NextAttemptAtUtc.Should().Be(start);
    }

    [Fact]
    public async Task Fifo_SendsJobsInCreationOrder()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        foreach (var t in new[] { "1", "2", "3" }) await h.EnqueueAsync(p.Id, t);
        await h.CycleAsync();
        h.Transport.Sent.Select(s => s.FirstLine).Should().Equal("1", "2", "3");
        h.Jobs().Should().OnlyContain(j => j.Status == PrintJobStatus.Sent && j.SentAtUtc != null);
    }

    [Fact]
    public async Task HeadFailure_BlocksFollowingJobs_OfSamePrinterOnly()
    {
        var h = new Harness();
        var down = h.AddPrinter("Cuisine");
        var up = h.AddPrinter("Bar");
        h.Transport.Offline.Add(down.Id);
        await h.EnqueueAsync(down.Id, "d1");
        await h.EnqueueAsync(down.Id, "d2");
        await h.EnqueueAsync(up.Id, "u1");

        await h.CycleAsync();

        h.Transport.Sent.Select(s => s.FirstLine).Should().Equal("u1");
        var downJobs = h.Jobs().Where(j => j.PrinterId == down.Id).ToList();
        downJobs[0].Attempts.Should().Be(1);
        downJobs[0].NextAttemptAtUtc.Should().Be(h.Clock.Now + TimeSpan.FromSeconds(5));
        downJobs[1].Attempts.Should().Be(0);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 15)]
    [InlineData(3, 30)]
    [InlineData(4, 60)]
    [InlineData(9, 60)]
    public void RetryDelay_Schedule(int attempts, int seconds) =>
        PrintQueueProcessor.RetryDelay(attempts).Should().Be(TimeSpan.FromSeconds(seconds));

    [Fact]
    public async Task FailedJob_NotRetriedBeforeDelay()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        h.Transport.Offline.Add(p.Id);
        await h.EnqueueAsync(p.Id, "A");
        await h.CycleAsync();
        h.Clock.Now += TimeSpan.FromSeconds(2);
        await h.CycleAsync();
        h.Jobs().Single().Attempts.Should().Be(1);
        h.Clock.Now += TimeSpan.FromSeconds(4);
        await h.CycleAsync();
        h.Jobs().Single().Attempts.Should().Be(2);
    }

    [Fact]
    public async Task Recovery_PrintsBacklogInOrder_AndNotifiesOnline()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        h.Transport.Offline.Add(p.Id);
        await h.EnqueueAsync(p.Id, "1");
        await h.EnqueueAsync(p.Id, "2");
        await h.CycleAsync();
        h.Notifier.Events.Should().ContainSingle().Which.Should().Be((p.Id, false, 2));

        h.Transport.Offline.Clear();
        h.Clock.Now += TimeSpan.FromSeconds(6);
        await h.CycleAsync();

        h.Transport.Sent.Select(s => s.FirstLine).Should().Equal("1", "2");
        h.Notifier.Events.Last().IsOnline.Should().BeTrue();
    }

    [Fact]
    public async Task FirstSuccessFromUnknown_DoesNotNotify()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        await h.EnqueueAsync(p.Id, "A");
        await h.CycleAsync();
        h.Notifier.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task DeadlinePassed_MarksFailed_AndNotifies()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        h.Transport.Offline.Add(p.Id);
        await h.EnqueueAsync(p.Id, "A");
        await h.EnqueueAsync(p.Id, "B");
        await h.CycleAsync();
        h.Notifier.Events.Clear();

        h.Clock.Now += TimeSpan.FromMinutes(31);
        await h.CycleAsync();

        h.Jobs().Should().OnlyContain(j => j.Status == PrintJobStatus.Failed);
        h.Notifier.Events.Should().NotBeEmpty();
        h.Notifier.Events.Last().Should().Be((p.Id, false, 0));
    }

    [Fact]
    public async Task PendingJobs_AreResumedAfterRestart()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        await h.EnqueueAsync(p.Id, "A");
        h.Restart();
        await h.CycleAsync();
        h.Transport.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task InactiveOrDeletedPrinter_CancelsPendingJobs()
    {
        var h = new Harness();
        var inactive = h.AddPrinter("Désactivée", active: false);
        await h.EnqueueAsync(inactive.Id, "A");
        await h.EnqueueAsync(Guid.NewGuid(), "B");
        await h.CycleAsync();
        h.Jobs().Should().OnlyContain(j => j.Status == PrintJobStatus.Cancelled && j.LastError != null);
        h.Transport.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Purge_RemovesOldSentAndCancelled_KeepsFailedAndRecent()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        var old = h.Clock.Now.AddDays(-8);
        h.Db(db =>
        {
            foreach (var status in new[] { PrintJobStatus.Sent, PrintJobStatus.Cancelled, PrintJobStatus.Failed })
                db.PrintJobs.Add(new PrintJob { PrinterId = p.Id, DocumentJson = "{}", Status = status, CreatedAtUtc = old, NextAttemptAtUtc = old, DeadlineAtUtc = old });
            db.PrintJobs.Add(new PrintJob { PrinterId = p.Id, DocumentJson = "{}", Status = PrintJobStatus.Sent, CreatedAtUtc = h.Clock.Now.AddDays(-1), NextAttemptAtUtc = old, DeadlineAtUtc = old });
            return db.SaveChanges();
        });

        await h.Processor.PurgeAsync(CancellationToken.None);

        h.Jobs().Select(j => j.Status).Should().BeEquivalentTo([PrintJobStatus.Failed, PrintJobStatus.Sent]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter PrintQueueProcessorTests`
Expected: échec de compilation.

- [ ] **Step 3: Implement**

`PrintLog.cs` :

```csharp
using System;
using Microsoft.Extensions.Logging;

namespace RestaurantPos.Infrastructure.Printing;

internal static partial class PrintLog
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Impression échouée sur {PrinterName} (job {JobId}, essai {Attempt}).")]
    public static partial void SendFailed(ILogger logger, Exception exception, string printerName, Guid jobId, int attempt);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "Aucune imprimante pour {Target} : rien mis en file.")]
    public static partial void NoPrinter(ILogger logger, string target);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Error, Message = "Mise en file d'impression impossible.")]
    public static partial void QueueFailed(ILogger logger, Exception exception);
}
```

`PrintQueue.cs` :

```csharp
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>Réveille le PrintWorker dès qu'un job est mis en file (sinon scrutation toutes les 5 s).</summary>
public sealed class PrintSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Délai de scrutation écoulé.
        }
    }
}

public sealed class PrintQueue
{
    private readonly AppDbContext _db;
    private readonly PrintSignal _signal;
    private readonly TimeProvider _time;

    public PrintQueue(AppDbContext db, PrintSignal signal, TimeProvider time)
    {
        _db = db;
        _signal = signal;
        _time = time;
    }

    public async Task EnqueueAsync(Guid printerId, PrintJobKind kind, TicketDocument document, bool openCashDrawer, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        _db.PrintJobs.Add(new PrintJob
        {
            PrinterId = printerId,
            Kind = kind,
            DocumentJson = TicketDocumentJson.Serialize(document),
            OpenCashDrawer = openCashDrawer,
            NextAttemptAtUtc = now,
            DeadlineAtUtc = now + PrintJob.Lifetime,
            CreatedAtUtc = now
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        _signal.Notify();
    }
}
```

`PrinterStatusTracker.cs` :

```csharp
using System;
using System.Collections.Concurrent;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>IsOnline null = aucun envoi depuis le démarrage.</summary>
public sealed record PrinterState(bool? IsOnline, DateTimeOffset? SinceUtc);

public sealed class PrinterStatusTracker
{
    private static readonly PrinterState Unknown = new(null, null);
    private readonly ConcurrentDictionary<Guid, PrinterState> _states = new();

    public PrinterState Get(Guid printerId) => _states.GetValueOrDefault(printerId, Unknown);

    /// <returns>true si la transition doit être notifiée : en ligne ↔ hors ligne, ou premier échec. Le premier succès après démarrage est silencieux.</returns>
    public bool Record(Guid printerId, bool success, DateTimeOffset nowUtc)
    {
        var previous = Get(printerId);
        if (previous.IsOnline == success) return false;
        _states[printerId] = new PrinterState(success, nowUtc);
        return previous.IsOnline is not null || !success;
    }
}
```

`PrintQueueProcessor.cs` :

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Printing;

public interface IPrinterStatusNotifier
{
    Task PrinterStatusChangedAsync(Guid printerId, string printerName, bool isOnline, int pendingCount);
}

/// <summary>
/// Logique du PrintWorker. Par imprimante : jobs dus dans l'ordre de création, un à la fois ; le premier échec
/// arrête la file de cette imprimante (ordre préservé). Filtres de dates en mémoire (limite EF Core SQLite).
/// </summary>
public sealed class PrintQueueProcessor
{
    public static readonly TimeSpan PurgeAge = TimeSpan.FromDays(7);
    private readonly IServiceScopeFactory _scopes;
    private readonly IPrinterTransport _transport;
    private readonly PrinterStatusTracker _tracker;
    private readonly IPrinterStatusNotifier _notifier;
    private readonly TimeProvider _time;
    private readonly ILogger<PrintQueueProcessor> _logger;

    public PrintQueueProcessor(IServiceScopeFactory scopes, IPrinterTransport transport, PrinterStatusTracker tracker,
        IPrinterStatusNotifier notifier, TimeProvider time, ILogger<PrintQueueProcessor> logger)
    {
        _scopes = scopes;
        _transport = transport;
        _tracker = tracker;
        _notifier = notifier;
        _time = time;
        _logger = logger;
    }

    public static TimeSpan RetryDelay(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(15),
        3 => TimeSpan.FromSeconds(30),
        _ => TimeSpan.FromSeconds(60)
    };

    /// <summary>Annule les jobs d'imprimantes inactives/supprimées, passe Failed les jobs échus, renvoie les imprimantes ayant un job dû.</summary>
    public async Task<IReadOnlyList<Guid>> PrepareAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.PrintJobs.Where(j => j.Status == PrintJobStatus.Pending).ToListAsync(ct).ConfigureAwait(false);
        if (pending.Count == 0) return [];

        var now = _time.GetUtcNow();
        var printers = await db.PrinterConfigurations.AsNoTracking().ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        var expiredOn = new HashSet<Guid>();
        foreach (var job in pending)
        {
            if (!printers.TryGetValue(job.PrinterId, out var printer) || !printer.IsActive)
            {
                job.Status = PrintJobStatus.Cancelled;
                job.LastError = printer is null ? "Printer deleted" : "Printer disabled";
            }
            else if (now >= job.DeadlineAtUtc)
            {
                job.Status = PrintJobStatus.Failed;
                job.LastError ??= "Deadline exceeded";
                expiredOn.Add(job.PrinterId);
            }
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var id in expiredOn) await NotifyAsync(db, printers[id], ct).ConfigureAwait(false);

        return pending.Where(j => j.Status == PrintJobStatus.Pending && j.NextAttemptAtUtc <= now).Select(j => j.PrinterId).Distinct().ToList();
    }

    public async Task ProcessPrinterAsync(Guid printerId, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var printer = await db.PrinterConfigurations.AsNoTracking().FirstOrDefaultAsync(p => p.Id == printerId, ct).ConfigureAwait(false);
        if (printer is null || !printer.IsActive) return;

        var jobs = (await db.PrintJobs.Where(j => j.PrinterId == printerId && j.Status == PrintJobStatus.Pending).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(j => j.CreatedAtUtc).ThenBy(j => j.Id).ToList();

        foreach (var job in jobs)
        {
            var now = _time.GetUtcNow();
            if (job.NextAttemptAtUtc > now) break;
            job.Attempts++;
            try
            {
                await _transport.SendAsync(printer, TicketDocumentJson.Deserialize(job.DocumentJson), job.OpenCashDrawer, ct).ConfigureAwait(false);
                job.Status = PrintJobStatus.Sent;
                job.SentAtUtc = now;
                job.LastError = null;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                if (_tracker.Record(printerId, true, now)) await NotifyAsync(db, printer, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                PrintLog.SendFailed(_logger, ex, printer.Name, job.Id, job.Attempts);
                job.LastError = ex.Message;
                if (now >= job.DeadlineAtUtc) job.Status = PrintJobStatus.Failed;
                else job.NextAttemptAtUtc = now + RetryDelay(job.Attempts);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                if (_tracker.Record(printerId, false, now) || job.Status == PrintJobStatus.Failed)
                    await NotifyAsync(db, printer, ct).ConfigureAwait(false);
                break;
            }
        }
    }

    public async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var limit = _time.GetUtcNow() - PurgeAge;
        var old = (await db.PrintJobs.Where(j => j.Status == PrintJobStatus.Sent || j.Status == PrintJobStatus.Cancelled).ToListAsync(ct).ConfigureAwait(false))
            .Where(j => j.CreatedAtUtc < limit).ToList();
        db.PrintJobs.RemoveRange(old);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task NotifyAsync(AppDbContext db, PrinterConfiguration printer, CancellationToken ct)
    {
        var pending = await db.PrintJobs.CountAsync(j => j.PrinterId == printer.Id && j.Status == PrintJobStatus.Pending, ct).ConfigureAwait(false);
        await _notifier.PrinterStatusChangedAsync(printer.Id, printer.Name, _tracker.Get(printer.Id).IsOnline != false, pending).ConfigureAwait(false);
    }
}
```

`PosHub.cs` — ajouter à `IPosHubClient` :

```csharp
Task OnPrinterStatusChanged(Guid printerId, string printerName, bool isOnline, int pendingCount);
```

`src/RestaurantPos.Api/Services/SignalRPrinterStatusNotifier.cs` :

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using RestaurantPos.Api.Hubs;
using RestaurantPos.Infrastructure.Printing;

namespace RestaurantPos.Api.Services;

public sealed class SignalRPrinterStatusNotifier : IPrinterStatusNotifier
{
    private readonly IHubContext<PosHub, IPosHubClient> _hub;
    public SignalRPrinterStatusNotifier(IHubContext<PosHub, IPosHubClient> hub) => _hub = hub;

    public Task PrinterStatusChangedAsync(Guid printerId, string printerName, bool isOnline, int pendingCount) =>
        _hub.Clients.All.OnPrinterStatusChanged(printerId, printerName, isOnline, pendingCount);
}
```

`src/RestaurantPos.Api/Services/PrintWorker.cs` :

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestaurantPos.Infrastructure.Printing;

namespace RestaurantPos.Api.Services;

/// <summary>Dépile PrintJobs : une tâche par imprimante (imprimantes en parallèle), purge quotidienne.</summary>
public sealed partial class PrintWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private readonly PrintQueueProcessor _processor;
    private readonly PrintSignal _signal;
    private readonly TimeProvider _time;
    private readonly ILogger<PrintWorker> _logger;
    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    public PrintWorker(PrintQueueProcessor processor, PrintSignal signal, TimeProvider time, ILogger<PrintWorker> logger)
    {
        _processor = processor;
        _signal = signal;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextPurge = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var printerId in await _processor.PrepareAsync(stoppingToken).ConfigureAwait(false))
                {
                    if (_running.TryGetValue(printerId, out var running) && !running.IsCompleted) continue;
                    _running[printerId] = RunPrinterAsync(printerId, stoppingToken);
                }
                if (_time.GetUtcNow() >= nextPurge)
                {
                    await _processor.PurgeAsync(stoppingToken).ConfigureAwait(false);
                    nextPurge = _time.GetUtcNow().AddDays(1);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogCycleFailed(_logger, ex);
            }
            await _signal.WaitAsync(PollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunPrinterAsync(Guid printerId, CancellationToken ct)
    {
        try
        {
            await _processor.ProcessPrinterAsync(printerId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogCycleFailed(_logger, ex);
        }
    }

    [LoggerMessage(EventId = 3010, Level = LogLevel.Error, Message = "Service d'impression en erreur.")]
    private static partial void LogCycleFailed(ILogger logger, Exception exception);
}
```

`Program.cs` (DI, près de l.98) :

```csharp
builder.Services.AddSingleton<PrintSignal>();
builder.Services.AddSingleton<PrinterStatusTracker>();
builder.Services.AddSingleton<IPrinterStatusNotifier, SignalRPrinterStatusNotifier>();
builder.Services.AddSingleton<PrintQueueProcessor>();
builder.Services.AddScoped<PrintQueue>();
```

et dans le bloc `if (!builder.Environment.IsEnvironment("Testing"))` de la l.99 : `builder.Services.AddHostedService<PrintWorker>();`.

- [ ] **Step 4: Run tests**

Run: `dotnet format RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(impression): file PrintJobs, service d'impression avec relances et état des imprimantes"
```

---

### Task 6: Endpoints jobs et état des imprimantes

**Files:**
- Create: `src/RestaurantPos.Api/Endpoints/PrintJobEndpoints.cs`
- Modify: `src/RestaurantPos.Api/Program.cs:394` (`app.MapPrintJobEndpoints();` après `MapPrinterEndpoints`)
- Modify: `.resx` ×3
- Test: `tests/RestaurantPos.Api.Tests/PrintJobEndpointsTests.cs`

**Interfaces:**
- Consumes: Task 5 (`PrinterStatusTracker`, `PrintSignal`), Task 1 (`PrintJob`).
- Produces:
  - `GET /api/printers/status` (authentifié) → `PrinterStatusDto[]` : `(Guid PrinterId, string Name, bool IsActive, bool? IsOnline, DateTimeOffset? SinceUtc, int PendingCount, int FailedCount)`.
  - `GET /api/printers/{id}/jobs?status=Pending,Failed` (manager/admin) → `PrintJobDto[]` : `(Guid Id, Guid PrinterId, string Kind, string Status, int Attempts, DateTimeOffset CreatedAtUtc, DateTimeOffset? SentAtUtc, string? LastError)` ; 100 plus récents ; `status` absent = `Pending` + `Failed`.
  - `POST /api/print-jobs/{id}/retry` (manager/admin) : `Failed` → `Pending`, `Attempts = 0`, échéance +30 min → 200 `PrintJobDto` ; 404 / 409.
  - `POST /api/print-jobs/{id}/cancel` (manager/admin) : `Pending`/`Failed` → `Cancelled` → 200 ; 404 / 409.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class PrintJobEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;
    public PrintJobEndpointsTests(PosApiApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string pin)
    {
        var client = _factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest(pin))).Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    private async Task<(Guid PrinterId, Guid JobId)> SeedJobAsync(PrintJobStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var printer = new PrinterConfiguration { Name = "P-" + Guid.NewGuid().ToString("N")[..6], IpAddress = "10.0.0.9" };
        db.PrinterConfigurations.Add(printer);
        var now = DateTimeOffset.UtcNow;
        var job = new PrintJob { PrinterId = printer.Id, DocumentJson = "{}", Status = status, Attempts = 7, CreatedAtUtc = now, NextAttemptAtUtc = now, DeadlineAtUtc = now.AddMinutes(-1), LastError = "refused" };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();
        return (printer.Id, job.Id);
    }

    private async Task<PrintJob> JobAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().PrintJobs.AsNoTracking().SingleAsync(j => j.Id == id);
    }

    [Fact]
    public async Task Retry_FailedJob_BecomesPendingWithNewDeadline()
    {
        var manager = await ClientAsync("1234");
        var (_, jobId) = await SeedJobAsync(PrintJobStatus.Failed);
        (await manager.PostAsync($"/api/print-jobs/{jobId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var job = await JobAsync(jobId);
        job.Status.Should().Be(PrintJobStatus.Pending);
        job.Attempts.Should().Be(0);
        job.DeadlineAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(30), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Retry_PendingJob_Returns409()
    {
        var manager = await ClientAsync("1234");
        var (_, jobId) = await SeedJobAsync(PrintJobStatus.Pending);
        (await manager.PostAsync($"/api/print-jobs/{jobId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(PrintJobStatus.Pending, HttpStatusCode.OK)]
    [InlineData(PrintJobStatus.Failed, HttpStatusCode.OK)]
    [InlineData(PrintJobStatus.Sent, HttpStatusCode.Conflict)]
    public async Task Cancel_RespectsStatus(PrintJobStatus status, HttpStatusCode expected)
    {
        var manager = await ClientAsync("1234");
        var (_, jobId) = await SeedJobAsync(status);
        (await manager.PostAsync($"/api/print-jobs/{jobId}/cancel", null)).StatusCode.Should().Be(expected);
        if (expected == HttpStatusCode.OK) (await JobAsync(jobId)).Status.Should().Be(PrintJobStatus.Cancelled);
    }

    [Fact]
    public async Task UnknownJob_Returns404()
    {
        var manager = await ClientAsync("1234");
        (await manager.PostAsync($"/api/print-jobs/{Guid.NewGuid()}/retry", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Jobs_DefaultsToPendingAndFailed()
    {
        var manager = await ClientAsync("1234");
        var (printerId, failedId) = await SeedJobAsync(PrintJobStatus.Failed);
        var body = await manager.GetFromJsonAsync<JsonElement>($"/api/printers/{printerId}/jobs");
        body.EnumerateArray().Select(j => j.GetProperty("id").GetGuid()).Should().Equal(failedId);
        body[0].GetProperty("status").GetString().Should().Be("Failed");
        (await manager.GetFromJsonAsync<JsonElement>($"/api/printers/{printerId}/jobs?status=Sent")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Status_ListsPrintersWithCounts_ForAnyAuthenticatedUser()
    {
        var waiter = await ClientAsync("2468");
        var (printerId, _) = await SeedJobAsync(PrintJobStatus.Failed);
        var body = await waiter.GetFromJsonAsync<JsonElement>("/api/printers/status");
        var row = body.EnumerateArray().Single(p => p.GetProperty("printerId").GetGuid() == printerId);
        row.GetProperty("failedCount").GetInt32().Should().Be(1);
        row.GetProperty("isOnline").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Waiter_CannotRetryOrListJobs()
    {
        var waiter = await ClientAsync("2468");
        var (printerId, jobId) = await SeedJobAsync(PrintJobStatus.Failed);
        (await waiter.PostAsync($"/api/print-jobs/{jobId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.GetAsync($"/api/printers/{printerId}/jobs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RestaurantPos.Api.Tests --filter PrintJobEndpointsTests`
Expected: FAIL (routes absentes).

- [ ] **Step 3: Implement**

`PrintJobEndpoints.cs` :

```csharp
using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;

namespace RestaurantPos.Api.Endpoints;

public record PrinterStatusDto(Guid PrinterId, string Name, bool IsActive, bool? IsOnline, DateTimeOffset? SinceUtc, int PendingCount, int FailedCount);
public record PrintJobDto(Guid Id, Guid PrinterId, string Kind, string Status, int Attempts, DateTimeOffset CreatedAtUtc, DateTimeOffset? SentAtUtc, string? LastError);

public static class PrintJobEndpoints
{
    private static readonly PrintJobStatus[] OpenStatuses = [PrintJobStatus.Pending, PrintJobStatus.Failed];

    public static void MapPrintJobEndpoints(this IEndpointRouteBuilder app)
    {
        var printers = app.MapGroup("/api/printers").WithTags("Printers").RequireAuthorization();

        printers.MapGet("/status", async (AppDbContext db, PrinterStatusTracker tracker) =>
        {
            var list = await db.PrinterConfigurations.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
            var open = await db.PrintJobs.AsNoTracking()
                .Where(j => j.Status == PrintJobStatus.Pending || j.Status == PrintJobStatus.Failed)
                .Select(j => new { j.PrinterId, j.Status }).ToListAsync();
            return Results.Ok(list.Select(p =>
            {
                var state = tracker.Get(p.Id);
                return new PrinterStatusDto(p.Id, p.Name, p.IsActive, state.IsOnline, state.SinceUtc,
                    open.Count(j => j.PrinterId == p.Id && j.Status == PrintJobStatus.Pending),
                    open.Count(j => j.PrinterId == p.Id && j.Status == PrintJobStatus.Failed));
            }));
        });

        printers.MapGet("/{id:guid}/jobs", async (Guid id, string? status, AppDbContext db) =>
        {
            var wanted = string.IsNullOrWhiteSpace(status)
                ? OpenStatuses
                : status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => Enum.TryParse<PrintJobStatus>(s, ignoreCase: true, out var st) ? st : (PrintJobStatus?)null)
                    .OfType<PrintJobStatus>().ToArray();
            var jobs = (await db.PrintJobs.AsNoTracking().Where(j => j.PrinterId == id && wanted.Contains(j.Status)).ToListAsync())
                .OrderByDescending(j => j.CreatedAtUtc).Take(100).Select(ToDto);
            return Results.Ok(jobs);
        }).RequireAuthorization("RequireManagerOrAdmin");

        var jobsGroup = app.MapGroup("/api/print-jobs").WithTags("Printers").RequireAuthorization("RequireManagerOrAdmin");

        jobsGroup.MapPost("/{id:guid}/retry", async (Guid id, AppDbContext db, PrintSignal signal, TimeProvider time) =>
        {
            var job = await db.PrintJobs.FindAsync(id);
            if (job is null) return Results.NotFound(new { Message = Texts.T("errors.print_job_not_found") });
            if (job.Status != PrintJobStatus.Failed) return Results.Conflict(new { Message = Texts.T("errors.print_job_not_retryable") });
            var now = time.GetUtcNow();
            job.Status = PrintJobStatus.Pending;
            job.Attempts = 0;
            job.NextAttemptAtUtc = now;
            job.DeadlineAtUtc = now + PrintJob.Lifetime;
            job.LastError = null;
            await db.SaveChangesAsync();
            signal.Notify();
            return Results.Ok(ToDto(job));
        });

        jobsGroup.MapPost("/{id:guid}/cancel", async (Guid id, AppDbContext db) =>
        {
            var job = await db.PrintJobs.FindAsync(id);
            if (job is null) return Results.NotFound(new { Message = Texts.T("errors.print_job_not_found") });
            if (!OpenStatuses.Contains(job.Status)) return Results.Conflict(new { Message = Texts.T("errors.print_job_not_cancellable") });
            job.Status = PrintJobStatus.Cancelled;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(job));
        });
    }

    private static PrintJobDto ToDto(PrintJob j) =>
        new(j.Id, j.PrinterId, j.Kind.ToString(), j.Status.ToString(), j.Attempts, j.CreatedAtUtc, j.SentAtUtc, j.LastError);
}
```

`.resx` :

| Clé | en | fr | ar |
|---|---|---|---|
| `errors.print_job_not_found` | Print job not found. | Impression introuvable. | مهمة الطباعة غير موجودة. |
| `errors.print_job_not_retryable` | Only failed print jobs can be retried. | Seules les impressions en échec peuvent être relancées. | يمكن إعادة محاولة مهام الطباعة الفاشلة فقط. |
| `errors.print_job_not_cancellable` | This print job can no longer be cancelled. | Cette impression ne peut plus être annulée. | لم يعد بالإمكان إلغاء مهمة الطباعة هذه. |

- [ ] **Step 4: Run tests**

Run: `dotnet format RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(impression): endpoints état des imprimantes, relance et annulation des impressions"
```

---

### Task 7: Résolution des postes et bon cuisine

**Files:**
- Modify: `src/RestaurantPos.Infrastructure/Services/KitchenRoutingService.cs:43-75`
- Modify: `src/RestaurantPos.Infrastructure/Printing/TicketDocumentBuilder.cs` (`Receipt`, `KitchenTicket`)
- Modify: `src/RestaurantPos.Api/Program.cs:460-464` (seed : postes des familles)
- Modify: `.resx` ×3
- Test: `tests/RestaurantPos.Infrastructure.Tests/KitchenRoutingServiceTests.cs`, `MultiStationRoutingTests.cs`, `TicketDocumentBuilderTests.cs`

**Interfaces:**
- Consumes: Task 1 (`PreparationStations.Resolve`, `Category.PreparationStationId`).
- Produces: `TicketDocumentBuilder.KitchenTicket(KitchenTicket ticket, Order order, string language) : TicketDocument` ; `TicketDocumentBuilder.Receipt(FiscalReceipt receipt, Order order, string language) : TicketDocument` (ticket de caisse seul, sans bon de retrait) ; `KitchenTicket.StationId` ∈ `PreparationStations.Kitchen`.

- [ ] **Step 1: Write the failing tests**

`KitchenRoutingServiceTests` : remplacer les attentes `STATION-BAR`/`STATION-HOT` du test existant (poser `PreparationStationId = "BAR"` sur la catégorie `CAT-DRINKS` qu'il crée, attendre `BAR` et `HOT_KITCHEN`) et ajouter :

```csharp
[Fact]
public async Task Resolve_UsesCategory_WhenItemAndProductEmpty()
{
    var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("KdsRouting_" + Guid.NewGuid().ToString("N")).Options;
    using var db = new AppDbContext(options);
    db.Categories.Add(new Category { Id = "CAT-DRINKS", Name = "Boissons", PreparationStationId = "BAR" });
    db.Categories.Add(new Category { Id = "CAT-MAINS", Name = "Plats" });
    var mojito = new Product { Name = "Mojito", CategoryId = "CAT-DRINKS", Price = Money.FromDecimal(8m) };
    var steak = new Product { Name = "Entrecôte", CategoryId = "CAT-MAINS", Price = Money.FromDecimal(22m) };
    var tiramisu = new Product { Name = "Tiramisu", CategoryId = "CAT-MAINS", Price = Money.FromDecimal(7m), PreparationStationId = "DESSERT" };
    db.Products.AddRange(mojito, steak, tiramisu);
    var order = new Order { TableNumber = "T05" };
    order.Items.Add(new OrderItem { ProductId = mojito.Id, ProductName = mojito.Name });
    order.Items.Add(new OrderItem { ProductId = steak.Id, ProductName = steak.Name });
    order.Items.Add(new OrderItem { ProductId = tiramisu.Id, ProductName = tiramisu.Name });
    order.Items.Add(new OrderItem { ProductId = steak.Id, ProductName = "Steak grill", PreparationStationId = "GRILL" });
    db.Orders.Add(order);
    await db.SaveChangesAsync();

    var tickets = await new KitchenRoutingService(db).SplitAndRouteOrderAsync(order.Id);

    tickets.Select(t => t.StationId).Should().BeEquivalentTo(["BAR", "HOT_KITCHEN", "DESSERT", "GRILL"]);
}
```

`MultiStationRoutingTests` : `ExpectedStations = ["BAR", "HOT_KITCHEN", "DESSERT"]` et poser `PreparationStationId` (`BAR`, `DESSERT`) sur les catégories boissons/desserts créées par le test au lieu de compter sur leur nom.

`TicketDocumentBuilderTests` — ajouter :

```csharp
private static readonly DateTimeOffset Dispatched = new(2026, 9, 29, 12, 34, 0, TimeSpan.Zero);

private static KitchenTicket SampleKitchenTicket(Guid productId) => new()
{
    TableNumber = "stocké, ignoré",
    ServerName = "Julie",
    CoversCount = 4,
    StationId = "HOT_KITCHEN",
    DispatchedAtUtc = Dispatched,
    Items = { new KitchenTicketItem { ProductId = productId, ProductName = "Burger Rossini", Quantity = 2, ModifiersSummary = "Saignant", KitchenComment = "Sans oignon" } }
};

[Theory]
[InlineData("en", "TABLE T05", "Next course", "Covers")]
[InlineData("fr", "TABLE T05", "Suite", "Couverts")]
[InlineData("ar", "طاولة T05", "الطبق التالي", "عدد الأشخاص")]
public void KitchenTicket_EatIn_ShowsTableCourseAndDetails(string lang, string header, string course, string covers)
{
    var productId = Guid.NewGuid();
    var order = new Order { TableNumber = "T05", Destination = OrderDestination.EatIn, Items = { new OrderItem { ProductId = productId, ProductName = "Burger Rossini", Course = CourseType.Suite } } };
    var doc = TicketDocumentBuilder.KitchenTicket(SampleKitchenTicket(productId), order, lang);
    var text = AllText(doc);

    doc.RightToLeft.Should().Be(lang == "ar");
    doc.Lines[0].Should().BeOfType<TicketText>().Which.Should().Match<TicketText>(t => t.Text == header && t.Large);
    text.Should().Contain("2x Burger Rossini").And.Contain("+ Saignant").And.Contain("Sans oignon")
        .And.Contain(course).And.Contain(covers).And.Contain("Julie")
        .And.Contain(Dispatched.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture))
        .And.NotContain("stocké, ignoré");
}

[Theory]
[InlineData("en", "TAKEAWAY", "Buzzer 7")]
[InlineData("fr", "À EMPORTER", "Bipeur 7")]
[InlineData("ar", "سفري", "جهاز النداء 7")]
public void KitchenTicket_Takeaway_ShowsPickupAndBuzzer(string lang, string header, string buzzer)
{
    var order = new Order { TableNumber = "Comptoir", Destination = OrderDestination.Takeaway, PickupNumber = "A-12", PickupBuzzer = "7" };
    var text = AllText(TicketDocumentBuilder.KitchenTicket(SampleKitchenTicket(Guid.NewGuid()), order, lang));
    text.Should().StartWith(header).And.Contain("A-12").And.Contain(buzzer);
}

[Fact]
public void Receipt_HasFiscalContent_WithoutPickupCoupon()
{
    var doc = TicketDocumentBuilder.Receipt(SampleReceipt(), SampleOrder(), "fr");
    var text = AllText(doc);
    text.Should().Contain("T01-000042").And.Contain("abc123").And.NotContain("RETRAIT");
    doc.Lines.OfType<TicketSeparator>().Should().NotContain(s => s.Cut);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "FullyQualifiedName~KitchenRouting|FullyQualifiedName~MultiStationRouting|FullyQualifiedName~TicketDocumentBuilderTests"`
Expected: FAIL (stations `STATION-*`, méthodes absentes).

- [ ] **Step 3: Implement**

`KitchenRoutingService` — remplacer le chargement `productMap` et le `GroupBy` :

```csharp
var products = await _dbContext.Products.AsNoTracking()
    .ToDictionaryAsync(p => p.Id, cancellationToken).ConfigureAwait(false);
var categoryStations = await _dbContext.Categories.AsNoTracking()
    .ToDictionaryAsync(c => c.Id, c => c.PreparationStationId, cancellationToken).ConfigureAwait(false);

// …

var stationGroups = pendingItems.GroupBy(item =>
{
    var product = products.GetValueOrDefault(item.ProductId);
    var categoryStation = product is null ? null : categoryStations.GetValueOrDefault(product.CategoryId);
    return PreparationStations.Resolve(item.PreparationStationId, product?.PreparationStationId, categoryStation);
});
```

Supprimer le commentaire devenu faux (« Group items by station: CAT-DRINKS -> STATION-BAR… »).

`TicketDocumentBuilder` — extraire les lignes du ticket de caisse dans `ReceiptLines` (corps actuel de `FiscalReceipt`, depuis `new List<TicketLine> { new TicketSeparator() }` jusqu'à la ligne `receipt.SignatureHash` incluse, sans le `TicketSeparator(Cut: true)`), puis :

```csharp
public static TicketDocument Receipt(FiscalReceipt receipt, Order order, string language)
{
    ArgumentNullException.ThrowIfNull(receipt);
    ArgumentNullException.ThrowIfNull(order);
    var (lang, c) = Resolve(language);
    return new TicketDocument(lang, lang == "ar", ReceiptLines(receipt, order, c));
}

public static TicketDocument FiscalReceipt(FiscalReceipt receipt, Order order, string pickupNumber, string? buzzer, string language)
{
    ArgumentNullException.ThrowIfNull(receipt);
    ArgumentNullException.ThrowIfNull(order);
    var (lang, c) = Resolve(language);
    var lines = ReceiptLines(receipt, order, c);
    lines.Add(new TicketSeparator(Cut: true));
    AppendPickup(lines, c, order, pickupNumber, buzzer, receipt.CreatedAtUtc);
    return new TicketDocument(lang, lang == "ar", lines);
}

private static List<TicketLine> ReceiptLines(FiscalReceipt receipt, Order order, CultureInfo c)
{
    var lines = new List<TicketLine> { new TicketSeparator() };
    foreach (var h in Header) lines.Add(new TicketText(h, TicketAlign.Center));
    lines.Add(new TicketSeparator());
    lines.Add(new TicketColumns(Texts.Get(c, "receipt.ticket"), receipt.ReceiptNumber));
    lines.Add(new TicketColumns(Texts.Get(c, "receipt.date"), receipt.CreatedAtUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)));
    lines.Add(new TicketColumns(Texts.Get(c, "receipt.terminal"), receipt.TerminalId));
    lines.Add(new TicketColumns(Texts.Get(c, "receipt.mode"), Mode(c, order)));
    lines.Add(new TicketSeparator());
    foreach (var item in order.Items)
        lines.Add(new TicketColumns($"{item.Quantity}x {item.ProductName}", Amount(item.CalculateTotalTtc().AmountInCents)));
    lines.Add(new TicketSeparator());
    lines.Add(new TicketColumns(Texts.Get(c, "receipt.total_ttc"), Amount(receipt.TotalTtcAmount.AmountInCents)));
    lines.Add(new TicketColumns(Texts.Get(c, "receipt.total_ht"), Amount(receipt.TotalHtAmount.AmountInCents)));
    lines.Add(new TicketSeparator());
    lines.Add(new TicketText(Texts.Get(c, "receipt.vat_breakdown"), Bold: true));
    lines.Add(new TicketText(receipt.TaxBreakdownJson));
    lines.Add(new TicketText(Texts.Get(c, "receipt.fiscal_signature"), Bold: true));
    lines.Add(new TicketText(receipt.SignatureHash));
    return lines;
}

public static TicketDocument KitchenTicket(KitchenTicket ticket, Order order, string language)
{
    ArgumentNullException.ThrowIfNull(ticket);
    ArgumentNullException.ThrowIfNull(order);
    var (lang, c) = Resolve(language);
    var lines = new List<TicketLine>
    {
        new TicketText(order.Destination == OrderDestination.Takeaway
            ? Texts.Get(c, "kitchen_ticket.takeaway")
            : Texts.Get(c, "kitchen_ticket.table", ("table", order.TableNumber)), TicketAlign.Center, Large: true)
    };
    if (!string.IsNullOrWhiteSpace(order.PickupNumber))
        lines.Add(new TicketText(Texts.Get(c, "kitchen_ticket.pickup", ("number", order.PickupNumber)), TicketAlign.Center, Large: true));
    if (!string.IsNullOrWhiteSpace(order.PickupBuzzer))
        lines.Add(new TicketText(Texts.Get(c, "kitchen_ticket.buzzer", ("buzzer", order.PickupBuzzer)), TicketAlign.Center, Bold: true));
    lines.Add(new TicketColumns(Texts.Get(c, "kitchen_ticket.server"), ticket.ServerName));
    lines.Add(new TicketColumns(Texts.Get(c, "kitchen_ticket.covers"), ticket.CoversCount.ToString(CultureInfo.InvariantCulture)));
    lines.Add(new TicketColumns(Texts.Get(c, "kitchen_ticket.time"), ticket.DispatchedAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)));
    lines.Add(new TicketSeparator());
    foreach (var item in ticket.Items)
    {
        lines.Add(new TicketText($"{item.Quantity}x {item.ProductName}", Large: true));
        if (!string.IsNullOrWhiteSpace(item.ModifiersSummary)) lines.Add(new TicketText("+ " + item.ModifiersSummary));
        if (!string.IsNullOrWhiteSpace(item.KitchenComment))
            lines.Add(new TicketText(Texts.Get(c, "kitchen_ticket.comment", ("comment", item.KitchenComment)), Bold: true));
        // ponytail: service retrouvé par ProductId (KitchenTicketItem ne le stocke pas) ; deux lignes du même article à des services différents affichent le premier. Stocker Course sur KitchenTicketItem si besoin.
        var course = order.Items.FirstOrDefault(i => i.ProductId == item.ProductId)?.Course ?? CourseType.Direct;
        lines.Add(new TicketText(Texts.Get(c, CourseKey(course))));
        lines.Add(new TicketSeparator());
    }
    return new TicketDocument(lang, lang == "ar", lines);
}

private static string CourseKey(CourseType course) => course switch
{
    CourseType.Suite => "kitchen_ticket.course_suite",
    CourseType.Dessert => "kitchen_ticket.course_dessert",
    CourseType.OnDemand => "kitchen_ticket.course_on_demand",
    _ => "kitchen_ticket.course_direct"
};
```

Mettre à jour le résumé XML de la classe (« Aucun rendu ni envoi imprimante (sous-projet C) » est devenu faux → « Construit les tickets ; le rendu est fait par EscPosRasterRenderer. »).

`.resx` :

| Clé | en | fr | ar |
|---|---|---|---|
| `kitchen_ticket.table` | TABLE {table} | TABLE {table} | طاولة {table} |
| `kitchen_ticket.takeaway` | TAKEAWAY | À EMPORTER | سفري |
| `kitchen_ticket.pickup` | Pickup #{number} | Retrait n° {number} | استلام رقم {number} |
| `kitchen_ticket.buzzer` | Buzzer {buzzer} | Bipeur {buzzer} | جهاز النداء {buzzer} |
| `kitchen_ticket.server` | Server | Serveur | النادل |
| `kitchen_ticket.covers` | Covers | Couverts | عدد الأشخاص |
| `kitchen_ticket.time` | Time | Heure | الوقت |
| `kitchen_ticket.comment` | Note: {comment} | Note : {comment} | ملاحظة: {comment} |
| `kitchen_ticket.course_direct` | Now | Direct | فوري |
| `kitchen_ticket.course_suite` | Next course | Suite | الطبق التالي |
| `kitchen_ticket.course_dessert` | Dessert | Dessert | حلوى |
| `kitchen_ticket.course_on_demand` | On demand | À la demande | عند الطلب |

Seed `Program.cs:460-464` : passer `preparationStationId: "COLD"` à `catEntrees`, `"DESSERT"` à `catDesserts`, `"BAR"` à `catBoissons` (conserve le routage de la démo sans l'heuristique par nom).

- [ ] **Step 4: Run tests**

Run: `dotnet format RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(impression): postes article → famille → cuisine chaude et bon cuisine localisé"
```

---

### Task 8: Déclencheurs — comptoir, table, cuisine

**Files:**
- Create: `src/RestaurantPos.Infrastructure/Printing/PrintDispatcher.cs`
- Modify: `src/RestaurantPos.Api/Endpoints/CounterSaleEndpoints.cs:222-292`
- Modify: `src/RestaurantPos.Application/DTOs/CounterSaleDtos.cs:55-67` (`PrintQueued`)
- Modify: `src/RestaurantPos.Api/Endpoints/CheckoutEndpoints.cs:24-110`
- Modify: `src/RestaurantPos.Api/Endpoints/TableEndpoints.cs:57-79`, `HospitalityEndpoints.cs:77-110`, `Hubs/KitchenHub.cs:10-36`
- Modify: `src/RestaurantPos.Api/Program.cs` (DI `PrintDispatcher` scoped)
- Test: `tests/RestaurantPos.Infrastructure.Tests/PrintDispatcherTests.cs`, `tests/RestaurantPos.Api.Tests/PrintTriggerTests.cs`

**Interfaces:**
- Consumes: Task 5 (`PrintQueue`, `PrintLog`), Task 7 (`TicketDocumentBuilder.Receipt`, `.KitchenTicket`), Task 2 (`RestaurantSettingsDto.KitchenTicketLanguage`, `Device.ReceiptPrinterId`, `PaymentSettlementRequest.RequestReceiptPrint`).
- Produces:
  - `PrintDispatcher(AppDbContext db, PrintQueue queue, IRestaurantSettingsService settings, TimeProvider time, ILogger<PrintDispatcher> logger)`.
  - `Task<bool> QueueCounterSaleAsync(Guid orderId, string terminalId, string receiptNumber, bool withFiscalReceipt, bool hasCash, CancellationToken ct = default)`.
  - `Task<bool> QueueTableReceiptAsync(Guid orderId, string terminalId, string receiptNumber, bool hasCash, CancellationToken ct = default)`.
  - `Task QueueKitchenTicketsAsync(IReadOnlyCollection<Guid> ticketIds, CancellationToken ct = default)`.
  - Réponses : `CounterCheckoutResponse(..., bool OpenCashDrawer, bool PrintQueued)` ; réponse de `/api/checkout/pay` + `PrintQueued`.

- [ ] **Step 1: Write the failing tests**

`PrintDispatcherTests.cs` :

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class PrintDispatcherTests
{
    private static (PrintDispatcher Dispatcher, AppDbContext Db) Create()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("Dispatch_" + Guid.NewGuid().ToString("N")).Options);
        var dispatcher = new PrintDispatcher(db, new PrintQueue(db, new PrintSignal(), TimeProvider.System), new RestaurantSettingsService(db), TimeProvider.System, NullLogger<PrintDispatcher>.Instance);
        return (dispatcher, db);
    }

    private static PrinterConfiguration Printer(AppDbContext db, string name, string[] stations, bool drawer = false, bool active = true)
    {
        var p = new PrinterConfiguration { Name = name, IpAddress = "10.0.0.1", OpenCashDrawerOnReceipt = drawer, IsActive = active, AssignedStationIds = [.. stations] };
        db.PrinterConfigurations.Add(p);
        db.SaveChanges();
        return p;
    }

    private static Order PaidCounterOrder(AppDbContext db)
    {
        var order = new Order { TableNumber = "Comptoir", Destination = OrderDestination.Takeaway, PickupNumber = "A-01", Items = { new OrderItem { ProductName = "Wrap", Quantity = 1, UnitPrice = Money.FromCents(650) } } };
        db.Orders.Add(order);
        db.FiscalReceipts.Add(new FiscalReceipt { TerminalId = "T01", ReceiptNumber = "T01-000001", OrderId = order.Id, TotalTtcAmount = Money.FromCents(650), TotalHtAmount = Money.FromCents(591), TaxBreakdownJson = "{}", SignatureHash = "sig" });
        db.SaveChanges();
        return order;
    }

    [Fact]
    public async Task CounterSale_PickupVoucherByDefault_OnReceiptStationPrinter()
    {
        var (d, db) = Create();
        var caisse = Printer(db, "Caisse", ["RECEIPT"], drawer: true);
        var order = PaidCounterOrder(db);

        (await d.QueueCounterSaleAsync(order.Id, "T01", "T01-000001", withFiscalReceipt: false, hasCash: false)).Should().BeTrue();

        var job = db.PrintJobs.Single();
        job.PrinterId.Should().Be(caisse.Id);
        job.Kind.Should().Be(PrintJobKind.PickupVoucher);
        job.OpenCashDrawer.Should().BeFalse();
    }

    [Fact]
    public async Task CounterSale_FiscalReceiptRequested_CombinedTicket_DrawerWithCash()
    {
        var (d, db) = Create();
        Printer(db, "Caisse", ["RECEIPT"], drawer: true);
        var order = PaidCounterOrder(db);

        await d.QueueCounterSaleAsync(order.Id, "T01", "T01-000001", withFiscalReceipt: true, hasCash: true);

        var job = db.PrintJobs.Single();
        job.Kind.Should().Be(PrintJobKind.Receipt);
        job.OpenCashDrawer.Should().BeTrue();
        TicketDocumentJson.Deserialize(job.DocumentJson).Lines.OfType<TicketSeparator>().Should().Contain(s => s.Cut);
    }

    [Fact]
    public async Task CounterSale_CashButPrinterWithoutDrawer_NoKick()
    {
        var (d, db) = Create();
        Printer(db, "Caisse", ["RECEIPT"], drawer: false);
        await d.QueueCounterSaleAsync(PaidCounterOrder(db).Id, "T01", "T01-000001", false, hasCash: true);
        db.PrintJobs.Single().OpenCashDrawer.Should().BeFalse();
    }

    [Fact]
    public async Task CounterSale_DeviceReceiptPrinter_WinsOverReceiptStation()
    {
        var (d, db) = Create();
        Printer(db, "A-Caisse", ["RECEIPT"]);
        var own = Printer(db, "Caisse terrasse", []);
        db.Devices.Add(new Device { Name = "iPad terrasse", TerminalId = "T01", TokenHash = "h", ReceiptPrinterId = own.Id });
        db.SaveChanges();

        await d.QueueCounterSaleAsync(PaidCounterOrder(db).Id, "T01", "T01-000001", false, false);

        db.PrintJobs.Single().PrinterId.Should().Be(own.Id);
    }

    [Fact]
    public async Task CounterSale_NoReceiptPrinter_ReturnsFalse_NoJob()
    {
        var (d, db) = Create();
        Printer(db, "Caisse désactivée", ["RECEIPT"], active: false);
        Printer(db, "Cuisine", ["HOT_KITCHEN"]);
        (await d.QueueCounterSaleAsync(PaidCounterOrder(db).Id, "T01", "T01-000001", false, true)).Should().BeFalse();
        db.PrintJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task CounterSale_UnknownReceipt_ReturnsFalse_NoThrow()
    {
        var (d, db) = Create();
        Printer(db, "Caisse", ["RECEIPT"]);
        (await d.QueueCounterSaleAsync(PaidCounterOrder(db).Id, "T01", "INCONNU", withFiscalReceipt: true, hasCash: false)).Should().BeFalse();
    }

    [Fact]
    public async Task TableReceipt_QueuesReceiptOnly_InReceiptLanguage()
    {
        var (d, db) = Create();
        Printer(db, "Caisse", ["RECEIPT"]);
        var order = PaidCounterOrder(db);
        await new RestaurantSettingsService(db).UpdateAsync(new UpdateRestaurantSettingsRequest("ar", "fr"));

        (await d.QueueTableReceiptAsync(order.Id, "T01", "T01-000001", hasCash: false)).Should().BeTrue();

        var job = db.PrintJobs.Single();
        job.Kind.Should().Be(PrintJobKind.Receipt);
        var doc = TicketDocumentJson.Deserialize(job.DocumentJson);
        doc.Language.Should().Be("ar");
        doc.Lines.OfType<TicketSeparator>().Should().NotContain(s => s.Cut);
    }

    [Fact]
    public async Task Kitchen_OneCopyPerActivePrinterOfStation_InKitchenLanguage()
    {
        var (d, db) = Create();
        var hot1 = Printer(db, "Chaud 1", ["HOT_KITCHEN", "GRILL"]);
        var hot2 = Printer(db, "Chaud 2", ["HOT_KITCHEN"]);
        Printer(db, "Chaud éteinte", ["HOT_KITCHEN"], active: false);
        Printer(db, "Bar", ["BAR"]);
        var order = new Order { TableNumber = "T05", Destination = OrderDestination.EatIn };
        db.Orders.Add(order);
        var ticket = new KitchenTicket { OrderId = order.Id, TableNumber = "T05", StationId = "HOT_KITCHEN", Items = { new KitchenTicketItem { ProductName = "Burger" } } };
        db.KitchenTickets.Add(ticket);
        db.SaveChanges();
        await new RestaurantSettingsService(db).UpdateAsync(new UpdateRestaurantSettingsRequest("fr", "ar"));

        await d.QueueKitchenTicketsAsync([ticket.Id]);

        db.PrintJobs.Select(j => j.PrinterId).Should().BeEquivalentTo([hot1.Id, hot2.Id]);
        db.PrintJobs.ToList().Should().OnlyContain(j => j.Kind == PrintJobKind.KitchenTicket && !j.OpenCashDrawer
            && TicketDocumentJson.Deserialize(j.DocumentJson).Language == "ar");
    }

    [Fact]
    public async Task Kitchen_StationWithoutPrinter_QueuesNothing()
    {
        var (d, db) = Create();
        Printer(db, "Bar", ["BAR"]);
        var order = new Order { TableNumber = "T05" };
        db.Orders.Add(order);
        var ticket = new KitchenTicket { OrderId = order.Id, TableNumber = "T05", StationId = "DESSERT" };
        db.KitchenTickets.Add(ticket);
        db.SaveChanges();

        await d.QueueKitchenTicketsAsync([ticket.Id]);

        db.PrintJobs.Should().BeEmpty();
    }
}
```

`PrintTriggerTests.cs` (bout en bout HTTP ; une fabrique par test pour isoler la table `Comptoir` et les jobs) — lire `CheckoutE2ETests.cs` pour l'ouverture de table / l'ajout de lignes / le paiement, et écrire les trois tests complets :

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class PrintTriggerTests
{
    private static async Task<HttpClient> CashierAsync(PosApiApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"))).Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        await DeviceTestHelper.PairAsync(factory, client);
        return client;
    }

    /// <summary>Garantit une imprimante RECEIPT et une HOT_KITCHEN actives (le seed peut ne pas tourner en Testing).</summary>
    private static void EnsurePrinters(PosApiApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var printers = db.PrinterConfigurations.ToList();
        if (!printers.Any(p => p.IsActive && p.AssignedStationIds.Contains("RECEIPT")))
            db.PrinterConfigurations.Add(new PrinterConfiguration { Name = "Caisse", IpAddress = "10.0.0.1", AssignedStationIds = ["RECEIPT"] });
        if (!printers.Any(p => p.IsActive && p.AssignedStationIds.Contains("HOT_KITCHEN")))
            db.PrinterConfigurations.Add(new PrinterConfiguration { Name = "Cuisine", IpAddress = "10.0.0.2", AssignedStationIds = ["HOT_KITCHEN"] });
        db.SaveChanges();
    }

    private static List<PrintJob> Jobs(PosApiApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().PrintJobs.AsNoTracking().ToList();
    }

    [Theory]
    [InlineData(false, PrintJobKind.PickupVoucher)]
    [InlineData(true, PrintJobKind.Receipt)]
    public async Task CounterCheckout_QueuesOneJob_AndReportsPrintQueued(bool fiscal, PrintJobKind expected)
    {
        using var factory = new PosApiApplicationFactory();
        var client = await CashierAsync(factory);
        EnsurePrinters(factory);
        var order = await (await client.PostAsJsonAsync("/api/orders/counter/direct", new DirectCounterOpenRequest("T01", OrderDestination.Takeaway))).Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        (await client.PostAsJsonAsync("/api/tables/Comptoir/items", new AddOrderItemsRequest(
        [
            new(Guid.NewGuid(), "Sandwich Poulet", 1, 6.50m, 10.0m, null, null, CourseType.Direct, 0m, 10.0m, true)
        ]))).EnsureSuccessStatusCode();

        var res = await client.PostAsJsonAsync("/api/orders/counter/checkout", new CounterCheckoutRequest(
            order!.OrderId, null, OrderDestination.Takeaway, null, null, 0m, fiscal, null, [new CounterPaymentTender(PaymentMethod.Card, 6.50m, 6.50m)]));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("printQueued").GetBoolean().Should().BeTrue();
        Jobs(factory).Should().ContainSingle().Which.Kind.Should().Be(expected);
    }

    // TablePay_ReceiptOnlyWhenRequestedAndFullyPaid :
    //   table A : ligne 20,00 € ; POST /api/checkout/pay 10,00 € avec RequestReceiptPrint = true → printQueued false, 0 job ;
    //             POST /api/checkout/pay 10,00 € avec RequestReceiptPrint = true → printQueued true, 1 job Receipt ;
    //   table B : ligne 5,00 € ; POST /api/checkout/pay 5,00 € sans RequestReceiptPrint → printQueued false, toujours 1 job.
    // Dispatch_QueuesKitchenTicketOnStationPrinter :
    //   ouvrir T05, ajouter une ligne avec PreparationStationId = "HOT_KITCHEN", POST /api/tables/T05/dispatch
    //   → exactement un job KitchenTicket par imprimante active ayant HOT_KITCHEN.
    // Écrire ces deux tests en entier avec les appels de CheckoutE2ETests.cs (mêmes routes et records).
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test RestaurantPos.slnx --filter "FullyQualifiedName~PrintDispatcherTests|FullyQualifiedName~PrintTriggerTests"`
Expected: échec de compilation.

- [ ] **Step 3: Implement**

`PrintDispatcher.cs` :

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>
/// Met en file les tickets après le succès d'une opération métier. Ne lève jamais :
/// toute erreur est journalisée et donne false (printQueued), la vente reste valide.
/// </summary>
public sealed class PrintDispatcher
{
    private readonly AppDbContext _db;
    private readonly PrintQueue _queue;
    private readonly IRestaurantSettingsService _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<PrintDispatcher> _logger;

    public PrintDispatcher(AppDbContext db, PrintQueue queue, IRestaurantSettingsService settings, TimeProvider time, ILogger<PrintDispatcher> logger)
    {
        _db = db;
        _queue = queue;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    public async Task<bool> QueueCounterSaleAsync(Guid orderId, string terminalId, string receiptNumber, bool withFiscalReceipt, bool hasCash, CancellationToken ct = default)
    {
        try
        {
            var printer = await ReceiptPrinterAsync(terminalId, ct).ConfigureAwait(false);
            if (printer is null) return false;
            var order = await LoadOrderAsync(orderId, ct).ConfigureAwait(false);
            var language = (await _settings.GetAsync(ct).ConfigureAwait(false)).ReceiptLanguage;
            var pickup = order.PickupNumber ?? string.Empty;
            var (kind, document) = withFiscalReceipt
                ? (PrintJobKind.Receipt, TicketDocumentBuilder.FiscalReceipt(await LoadReceiptAsync(terminalId, receiptNumber, ct).ConfigureAwait(false), order, pickup, order.PickupBuzzer, language))
                : (PrintJobKind.PickupVoucher, TicketDocumentBuilder.PickupCoupon(order, pickup, order.PickupBuzzer, language, _time.GetUtcNow()));
            await _queue.EnqueueAsync(printer.Id, kind, document, hasCash && printer.OpenCashDrawerOnReceipt, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            PrintLog.QueueFailed(_logger, ex);
            return false;
        }
    }

    public async Task<bool> QueueTableReceiptAsync(Guid orderId, string terminalId, string receiptNumber, bool hasCash, CancellationToken ct = default)
    {
        try
        {
            var printer = await ReceiptPrinterAsync(terminalId, ct).ConfigureAwait(false);
            if (printer is null) return false;
            var order = await LoadOrderAsync(orderId, ct).ConfigureAwait(false);
            var receipt = await LoadReceiptAsync(terminalId, receiptNumber, ct).ConfigureAwait(false);
            var language = (await _settings.GetAsync(ct).ConfigureAwait(false)).ReceiptLanguage;
            await _queue.EnqueueAsync(printer.Id, PrintJobKind.Receipt, TicketDocumentBuilder.Receipt(receipt, order, language), hasCash && printer.OpenCashDrawerOnReceipt, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            PrintLog.QueueFailed(_logger, ex);
            return false;
        }
    }

    public async Task QueueKitchenTicketsAsync(IReadOnlyCollection<Guid> ticketIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticketIds);
        if (ticketIds.Count == 0) return;
        try
        {
            var ids = ticketIds.ToList();
            var tickets = await _db.KitchenTickets.AsNoTracking().Include(t => t.Items).Where(t => ids.Contains(t.Id)).ToListAsync(ct).ConfigureAwait(false);
            var printers = await _db.PrinterConfigurations.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct).ConfigureAwait(false);
            var language = (await _settings.GetAsync(ct).ConfigureAwait(false)).KitchenTicketLanguage;
            foreach (var ticket in tickets)
            {
                var targets = printers.Where(p => p.AssignedStationIds.Contains(ticket.StationId)).ToList();
                if (targets.Count == 0)
                {
                    PrintLog.NoPrinter(_logger, ticket.StationId);
                    continue;
                }
                var document = TicketDocumentBuilder.KitchenTicket(ticket, await LoadOrderAsync(ticket.OrderId, ct).ConfigureAwait(false), language);
                foreach (var printer in targets)
                    await _queue.EnqueueAsync(printer.Id, PrintJobKind.KitchenTicket, document, false, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            PrintLog.QueueFailed(_logger, ex);
        }
    }

    /// <summary>Imprimante propre au poste (active) sinon première imprimante active du poste RECEIPT.</summary>
    private async Task<PrinterConfiguration?> ReceiptPrinterAsync(string terminalId, CancellationToken ct)
    {
        var printers = await _db.PrinterConfigurations.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct).ConfigureAwait(false);
        var ownId = await _db.Devices.AsNoTracking().Where(d => d.TerminalId == terminalId).Select(d => d.ReceiptPrinterId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var printer = printers.FirstOrDefault(p => p.Id == ownId)
            ?? printers.Where(p => p.AssignedStationIds.Contains(PreparationStations.Receipt)).OrderBy(p => p.Name, StringComparer.Ordinal).FirstOrDefault();
        if (printer is null) PrintLog.NoPrinter(_logger, terminalId);
        return printer;
    }

    private Task<Order> LoadOrderAsync(Guid orderId, CancellationToken ct) =>
        _db.Orders.AsNoTracking().Include(o => o.Items).FirstAsync(o => o.Id == orderId, ct);

    private Task<FiscalReceipt> LoadReceiptAsync(string terminalId, string receiptNumber, CancellationToken ct) =>
        _db.FiscalReceipts.AsNoTracking().FirstAsync(r => r.TerminalId == terminalId && r.ReceiptNumber == receiptNumber, ct);
}
```

`Program.cs` : `builder.Services.AddScoped<PrintDispatcher>();`.

Comptoir (`CounterSaleEndpoints`, handler `/checkout`) : ajouter le paramètre `PrintDispatcher printing`, et avant le `return` :

```csharp
bool printQueued = checkoutResult.IsSuccess && checkoutResult.RemainingBalanceCents == 0
    && await printing.QueueCounterSaleAsync(order.Id, terminalId, checkoutResult.ReceiptNumber, printFiscalReceipt, hasCash);
```

puis passer `printQueued` en dernier argument de `CounterCheckoutResponse` (ajouter `bool PrintQueued` en fin de record dans `CounterSaleDtos.cs`).

Table (`CheckoutEndpoints`, `/pay`) : ajouter `PrintDispatcher printing` aux paramètres, puis après le contrôle `!result.IsSuccess` :

```csharp
var hasCash = (req.Tenders ?? []).Any(t => t.Method == PaymentMethod.Cash);
var printQueued = req.RequestReceiptPrint && result.RemainingBalanceCents == 0
    && await printing.QueueTableReceiptAsync(orderId, terminalId, result.ReceiptNumber, hasCash);
```

et ajouter `PrintQueued = printQueued` à l'objet anonyme de réponse.

Cuisine :
- `TableEndpoints` `/dispatch` : injecter `PrintDispatcher printing` ; `var tickets = await kds.SplitAndRouteOrderAsync(table.ActiveOrderId.Value);` puis `await printing.QueueKitchenTicketsAsync(tickets.Select(t => t.TicketId).ToList());`.
- `KitchenHub` : ajouter `PrintDispatcher printing` au constructeur (champ `_printing`) ; dans `DispatchOrderToKitchen`, après la boucle de diffusion : `await _printing.QueueKitchenTicketsAsync(tickets.Select(t => t.TicketId).ToList()).ConfigureAwait(false);`.
- `HospitalityEndpoints` `/fire-suite` : injecter `PrintDispatcher printing` ; après `await db.SaveChangesAsync();` : `await printing.QueueKitchenTicketsAsync([ticket.Id]);`.

- [ ] **Step 4: Run tests**

Run: `dotnet format RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS.

- [ ] **Step 5: Essai de bout en bout local (sans imprimante)**

```bash
nc -lk 9100 > /tmp/print.bin &     # fausse imprimante
# Lancer l'API (commande CLAUDE.md) ; Gestion → Imprimantes : IP 127.0.0.1, port 9100, poste RECEIPT.
# Faire une vente comptoir depuis le web, puis :
xxd /tmp/print.bin | head -2        # doit commencer par 1b40 1d76 30
```
Arrêter `nc` → la vente suivante réussit quand même (`printQueued: true`, job `Pending`) ; relancer `nc` → le job sort à la relance suivante.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(impression): mise en file automatique au paiement et à l'envoi en cuisine"
```

---

### Task 9: Client web

**Files:**
- Modify: `src/RestaurantPos.Api/wwwroot/app.js` (l.924, 989 : poste des lignes ; l.1928-1944 : paiement à table ; l.2750, 3508 : poste article ; l.2827-2870 : imprimantes ; l.3891-3920 : appareils ; l.496-499 et 4099-4116 : famille ; l.4781-4805 : SignalR)
- Modify: `src/RestaurantPos.Api/wwwroot/index.html` (modale paiement, `editCategoryModal` l.1424-1440, sélecteurs de poste l.333 et l.1402, section imprimantes l.425)
- Modify: `src/RestaurantPos.Api/wwwroot/i18n/en.json`, `fr.json`, `ar.json`
- Test: `tests/RestaurantPos.Web.E2ETests/tests/printing.spec.ts`

**Interfaces:**
- Consumes: Tasks 2, 6, 8 (champs JSON `kitchenTicketLanguage`, `preparationStationId`, `receiptPrinterId`, `requestReceiptPrint`, `printQueued` ; routes `/api/printers/status`, `/api/printers/{id}/jobs`, `/api/print-jobs/{id}/retry|cancel`, `/api/devices/{id}/receipt-printer` ; événement `OnPrinterStatusChanged(printerId, printerName, isOnline, pendingCount)`).

- [ ] **Step 1: Write the failing Playwright spec**

`tests/RestaurantPos.Web.E2ETests/tests/printing.spec.ts` — reprendre la connexion, l'ouverture de Gestion et l'ouverture d'une table depuis un spec existant (ex. `split-bill.spec.ts`) :

```ts
import { test, expect } from '@playwright/test';
// importer / recopier les helpers de connexion et de navigation des specs existants

test('réglages : langue des bons cuisine enregistrée', async ({ page }) => {
  await loginAs(page, '1234');
  await openAdminSection(page, 'printers');
  await page.locator('#settingsKitchenLanguage').selectOption('ar');
  await expect.poll(async () => (await (await page.request.get('/api/settings')).json()).kitchenTicketLanguage).toBe('ar');
  await page.locator('#settingsKitchenLanguage').selectOption('fr');
});

test('famille : poste de préparation modifiable', async ({ page }) => {
  await loginAs(page, '1234');
  await openAdminSection(page, 'catalog');
  await openFirstCategoryEditor(page);
  await page.locator('#editCatStation').selectOption('BAR');
  await page.locator('#formEditCategory [type=submit]').click();
  await openFirstCategoryEditor(page);
  await expect(page.locator('#editCatStation')).toHaveValue('BAR');
});

test('paiement à table : la case envoie requestReceiptPrint', async ({ page }) => {
  await loginAs(page, '2468');
  await openTableWithOneLine(page, 'T05');
  await openPaymentModal(page);
  const request = page.waitForRequest(r => r.url().endsWith('/api/checkout/pay'));
  await page.locator('#paymentPrintReceipt').check();
  await confirmPayment(page);
  expect((await request).postDataJSON().requestReceiptPrint).toBe(true);
});

test('état des imprimantes affiché', async ({ page }) => {
  await loginAs(page, '1234');
  await openAdminSection(page, 'printers');
  await expect(page.locator('.printer-status').first()).toBeVisible();
});

test('notification hors ligne sur vraie transition', async ({ page }) => {
  // Déclarer via l'API une imprimante 127.0.0.1:1 poste RECEIPT (injoignable), faire une vente comptoir,
  // attendre le toast « hors ligne » (≤ 15 s : cycle 5 s + connexion refusée immédiate).
  test.setTimeout(60_000);
  await loginAs(page, '1234');
  // … création imprimante + vente comptoir via les helpers existants …
  await expect(page.locator('.toast').filter({ hasText: /hors ligne/i })).toBeVisible({ timeout: 15_000 });
});
```

Les helpers (`openFirstCategoryEditor`, `openTableWithOneLine`, `openPaymentModal`, `confirmPayment`) s'écrivent avec les sélecteurs réels d'`index.html` (les lire avant d'écrire le spec). Le dernier test désactive ensuite l'imprimante créée (`PUT /api/printers/{id}` avec `isActive: false`) pour ne pas polluer les autres specs.

Run: `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npx playwright test tests/printing.spec.ts --project='iPad Pro 11'`
Expected: FAIL (éléments absents).

- [ ] **Step 2: Implement**

Poste des lignes (`app.js:924`, `app.js:989`) : `preparationStationId: product.preparationStationId || null` (ne plus forcer `'HOT_KITCHEN'` : la famille doit pouvoir s'appliquer).

Sélecteurs de poste article (`index.html:333` et `:1402`) : ajouter en tête `<option value="" data-i18n="admin.station_from_category">Poste de la famille</option>` ; `app.js:2750` : `setSelectValue('editProdStation', p.preparationStationId || '')` ; `app.js:3508` : `const station = stationEl ? (stationEl.value || null) : null;`.

Famille (`editCategoryModal`, après le champ couleur) :

```html
<div class="form-group">
    <label for="editCatStation" data-i18n="admin.category_station_label">Poste de préparation :</label>
    <select id="editCatStation">
        <option value="" data-i18n="admin.station_default_hot">Cuisine chaude (défaut)</option>
        <option value="HOT_KITCHEN" data-i18n="admin.station_hot_kitchen">Cuisine Chaude / Grill</option>
        <option value="COLD" data-i18n="admin.station_cold">Froid</option>
        <option value="GRILL" data-i18n="admin.station_grill">Grill</option>
        <option value="DESSERT" data-i18n="admin.station_dessert">Desserts</option>
        <option value="BAR" data-i18n="admin.station_bar">Bar</option>
    </select>
</div>
```

Réutiliser les clés `admin.station_*` existantes (celles des options du sélecteur article) ; ne créer que les manquantes. `app.js:496-499` : `document.getElementById('editCatStation').value = cat.preparationStationId || '';`. Corps du `PUT` (`app.js:4104`) : ajouter `preparationStationId: document.getElementById('editCatStation').value` (chaîne vide = effacer).

Paiement à table (modale paiement) :

```html
<label class="checkbox-row" id="paymentPrintReceiptRow"><input type="checkbox" id="paymentPrintReceipt" /> <span data-i18n="payment.print_receipt">Imprimer le ticket de caisse</span></label>
```

masquée en vente comptoir (qui a déjà sa propre case). `app.js:1928` : ajouter `requestReceiptPrint: document.getElementById('paymentPrintReceipt')?.checked === true` au `payload` ; après succès, décocher ; si `payload.requestReceiptPrint && resData.printQueued === false`, `showToast(t('payment.print_not_queued'), 'warning')`. Même avertissement dans le flux comptoir quand la réponse porte `printQueued === false`.

Appareils (`loadAdminDevices`) : si `state.printers` est vide, `state.printers = await (await fetch('/api/printers')).json();`. Dans chaque ligne non révoquée :

```js
`<select class="device-receipt-printer" data-device-id="${d.id}" aria-label="${t('admin.device_receipt_printer_label')}">
    <option value="">${t('admin.device_receipt_printer_default')}</option>
    ${(state.printers || []).filter(p => p.isActive !== false).map(p => `<option value="${p.id}" ${p.id === d.receiptPrinterId ? 'selected' : ''}>${escapeHtml(p.name)}</option>`).join('')}
</select>`
```

et après le rendu :

```js
list.querySelectorAll('.device-receipt-printer').forEach(sel => sel.addEventListener('change', async () => {
    const r = await fetch(`/api/devices/${sel.dataset.deviceId}/receipt-printer`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ printerId: sel.value || null })
    });
    showToast(r.ok ? t('admin.device_receipt_printer_saved') : await readApiError(r, t('admin.device_receipt_printer_error')), r.ok ? 'success' : 'error');
}));
```

Réglages (section imprimantes, au-dessus de `#adminPrintersList`) :

```html
<div class="form-row">
    <label for="settingsReceiptLanguage" data-i18n="admin.receipt_language">Langue des tickets</label>
    <select id="settingsReceiptLanguage"><option value="en">English</option><option value="fr">Français</option><option value="ar">العربية</option></select>
    <label for="settingsKitchenLanguage" data-i18n="admin.kitchen_ticket_language">Langue des bons cuisine</label>
    <select id="settingsKitchenLanguage"><option value="en">English</option><option value="fr">Français</option><option value="ar">العربية</option></select>
</div>
```

```js
async function loadPrintSettings() {
    const res = await fetch('/api/settings');
    if (!res.ok) return;
    const s = await res.json();
    document.getElementById('settingsReceiptLanguage').value = s.receiptLanguage;
    document.getElementById('settingsKitchenLanguage').value = s.kitchenTicketLanguage;
}

async function savePrintSettings() {
    const res = await fetch('/api/settings', {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            receiptLanguage: document.getElementById('settingsReceiptLanguage').value,
            kitchenTicketLanguage: document.getElementById('settingsKitchenLanguage').value
        })
    });
    showToast(res.ok ? t('admin.settings_saved') : await readApiError(res, t('admin.settings_error')), res.ok ? 'success' : 'error');
}
```

Brancher `change` des deux `select` sur `savePrintSettings` une seule fois (dans `setupDeviceAdminHandlers` ou l'initialisation admin équivalente), appeler `loadPrintSettings()` au début de `loadAdminPrinters()`.

État des imprimantes (`loadAdminPrinters`) : après `state.printers = …`, `const statuses = await (await fetch('/api/printers/status')).json();` ; dans chaque ligne :

```js
const st = statuses.find(s => s.printerId === pr.id);
const status = !st || st.isOnline === null ? t('admin.printer_status_unknown')
    : st.isOnline ? t('admin.printer_status_online')
    : t('admin.printer_status_offline_since', { time: new Date(st.sinceUtc).toLocaleTimeString(window.i18n.locale, { hour: '2-digit', minute: '2-digit' }) });
const pending = st?.pendingCount ? ' · ' + t('admin.printer_pending', { count: st.pendingCount }) : '';
// dans le innerHTML : <span class="printer-status">${status}${pending}</span>
// si (st?.pendingCount || 0) + (st?.failedCount || 0) > 0 : bouton .btn-printer-jobs (t('admin.printer_jobs_btn'))
```

Bouton `.btn-printer-jobs` : charge `/api/printers/${pr.id}/jobs`, insère sous la ligne un `<div class="printer-jobs">` avec, par job, type (`admin.print_job_kind_{pickupvoucher|receipt|kitchenticket}`), heure de création, `lastError` (échappé), et un bouton `admin.print_job_retry` (`status === 'Failed'` → `POST /api/print-jobs/{id}/retry`) ou `admin.print_job_cancel` (`status === 'Pending'` → `POST /api/print-jobs/{id}/cancel`) ; après chaque action, toast puis `loadAdminPrinters()`.

Notification (`app.js`, après le gestionnaire `OnHappyHourStatusChanged`) :

```js
connection.on('OnPrinterStatusChanged', (printerId, printerName, isOnline, pendingCount) => {
    showToast(isOnline
        ? t('messages.printer_back_online', { name: printerName })
        : t('messages.printer_offline', { name: printerName, count: pendingCount }), isOnline ? 'success' : 'warning');
    if (document.getElementById('adminPrintersList')?.offsetParent) loadAdminPrinters();
});
```

Chaînes (`en` / `fr` / `ar`) — vérifier par `grep` qu'une clé n'existe pas déjà avant de l'ajouter :

| Clé | en | fr | ar |
|---|---|---|---|
| `admin.station_from_category` | Category station | Poste de la famille | محطة الفئة |
| `admin.category_station_label` | Preparation station: | Poste de préparation : | محطة التحضير: |
| `admin.station_default_hot` | Hot kitchen (default) | Cuisine chaude (défaut) | المطبخ الساخن (افتراضي) |
| `admin.receipt_language` | Receipt language | Langue des tickets | لغة الإيصالات |
| `admin.kitchen_ticket_language` | Kitchen ticket language | Langue des bons cuisine | لغة تذاكر المطبخ |
| `admin.settings_saved` | Settings saved | Réglages enregistrés | تم حفظ الإعدادات |
| `admin.settings_error` | Could not save settings | Enregistrement des réglages refusé | تعذر حفظ الإعدادات |
| `admin.device_receipt_printer_label` | Receipt printer | Imprimante de ticket | طابعة الإيصالات |
| `admin.device_receipt_printer_default` | Receipt station printer | Imprimante du poste caisse | طابعة محطة الصندوق |
| `admin.device_receipt_printer_saved` | Receipt printer saved | Imprimante de ticket enregistrée | تم حفظ طابعة الإيصالات |
| `admin.device_receipt_printer_error` | Could not save receipt printer | Imprimante de ticket refusée | تعذر حفظ طابعة الإيصالات |
| `admin.printer_status_unknown` | Status unknown | État inconnu | الحالة غير معروفة |
| `admin.printer_status_online` | Online | En ligne | متصلة |
| `admin.printer_status_offline_since` | Offline since {time} | Hors ligne depuis {time} | غير متصلة منذ {time} |
| `admin.printer_pending` | {count} pending | {count} en attente | {count} قيد الانتظار |
| `admin.printer_jobs_btn` | Print jobs | Impressions | مهام الطباعة |
| `admin.print_job_kind_pickupvoucher` | Pickup voucher | Bon de retrait | قسيمة الاستلام |
| `admin.print_job_kind_receipt` | Receipt | Ticket de caisse | الإيصال |
| `admin.print_job_kind_kitchenticket` | Kitchen ticket | Bon cuisine | تذكرة المطبخ |
| `admin.print_job_retry` | Retry | Relancer | إعادة المحاولة |
| `admin.print_job_cancel` | Cancel | Annuler | إلغاء |
| `payment.print_receipt` | Print receipt | Imprimer le ticket de caisse | طباعة الإيصال |
| `payment.print_not_queued` | No printer available: nothing was printed | Aucune imprimante disponible : rien n'a été imprimé | لا توجد طابعة متاحة: لم تتم الطباعة |
| `messages.printer_offline` | Printer {name} offline — {count} tickets waiting | Imprimante {name} hors ligne — {count} bons en attente | الطابعة {name} غير متصلة — {count} تذاكر في الانتظار |
| `messages.printer_back_online` | Printer {name} back online | Imprimante {name} rétablie | عادت الطابعة {name} للعمل |

- [ ] **Step 3: Run tests**

Run: `node scripts/i18n-check.mjs` (parité des clés + chaînes FR résiduelles) puis toute la suite : `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npm test`
Expected: PASS, specs existants compris.

- [ ] **Step 4: Commit**

```bash
git add src/RestaurantPos.Api/wwwroot tests/RestaurantPos.Web.E2ETests
git commit -m "feat(impression): web — postes des familles, imprimante des caisses, état des imprimantes, ticket à table"
```

---

### Task 10: iPad — PosKit (modèles, API, temps réel, stores)

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Models/OperationsModels.swift` (`RestaurantSettings` l.345, près de `Printer` l.298), `Models/CatalogModels.swift` (`MenuCategory` l.3, `Product.station` l.131), `Models/OrderModels.swift` (`PaymentRequest` l.237, `PaymentResult` l.253, `CounterCheckoutResult` l.311)
- Modify: `Networking/PosAPI.swift` (l.39-42, 96-98, 118-119), `Networking/HTTPPosAPI.swift`, `Testing/InMemoryPosAPI.swift`
- Modify: `Realtime/SignalRClient.swift:81-101`, `Stores/AdminStores.swift`, `Stores/TicketStore.swift:360`, `Stores/AppModel.swift:114`
- Modify: `Core/OrderMath.swift:30,56` (poste optionnel)
- Modify/Create fixtures: `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/settings.json`, `categories.json`, `printer_status.json`, `print_jobs.json`, `pay_partial.json`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Resources/Localizable.xcstrings`
- Test: `ios/Packages/PosKit/Tests/PosKitTests/ContractDecodingTests.swift`, `StoreTests.swift`, `NetworkingTests.swift`

**Interfaces:**
- Consumes: routes et JSON des Tasks 2, 6, 8.
- Produces (Swift) :
  - `RestaurantSettings.kitchenTicketLanguage: String` (décodage `decodeIfPresent ?? receiptLanguage`) ; `init(receiptLanguage:kitchenTicketLanguage: String? = nil)`.
  - `MenuCategory.preparationStationId: String?`.
  - `PaymentRequest.requestReceiptPrint: Bool` (défaut `false`) ; `PaymentResult.printQueued: Bool?` ; `CounterCheckoutResult.printQueued: Bool?`.
  - `PrinterStatus` et `PrintJobInfo` (ci-dessous).
  - `RealtimeEvent.printerStatusChanged(printerId: UUID?, name: String, isOnline: Bool, pendingCount: Int)`.
  - `PosAPI` : `printerStatuses() async throws -> [PrinterStatus]`, `printJobs(printerId: UUID) async throws -> [PrintJobInfo]`, `retryPrintJob(id: UUID) async throws`, `cancelPrintJob(id: UUID) async throws` ; `updateCategory(id:name:colorHex:displayOrder:preparationStationId:)`.
  - `SettingsStore.setKitchenTicketLanguage(_:) async` ; store imprimantes : `statuses: [PrinterStatus]`, `jobs: [UUID: [PrintJobInfo]]`, `loadStatuses()`, `loadJobs(printerId:)`, `retry(_:)`, `cancel(_:)`.
  - `TicketStore.pay(method:amount:tendered:printReceipt: Bool = false)`, `TicketStore.lastPrintQueued: Bool?`.
  - `AppModel.printerAlert: String?`.

- [ ] **Step 1: Capturer les fixtures réelles**

Avec l'API lancée (Task 8 terminée) :

```bash
TOKEN=$(curl -s localhost:5080/api/auth/login -H 'Content-Type: application/json' -d '{"pin":"1234"}' | jq -r .token)
F=ios/Packages/PosKit/Tests/PosKitTests/Fixtures
curl -s -H "Authorization: Bearer $TOKEN" localhost:5080/api/settings | jq . > $F/settings.json
curl -s localhost:5080/api/catalog/categories | jq . > $F/categories.json
curl -s -H "Authorization: Bearer $TOKEN" localhost:5080/api/printers/status | jq . > $F/printer_status.json
PID=$(jq -r '.[0].printerId' $F/printer_status.json)
curl -s -H "Authorization: Bearer $TOKEN" "localhost:5080/api/printers/$PID/jobs?status=Pending,Failed,Sent" | jq . > $F/print_jobs.json
```
`print_jobs.json` ne doit pas être vide : faire d'abord une vente comptoir avec l'imprimante de caisse pointée sur `127.0.0.1:1`. `categories.json` doit contenir au moins une famille avec `preparationStationId` (seed Task 7). Recapturer `pay_partial.json` (réponse partielle réelle, contient `printQueued`).

- [ ] **Step 2: Write the failing tests**

`ContractDecodingTests.swift` (utiliser les helpers de décodage déjà présents dans le fichier) :

```swift
@Test func decodesSettingsWithKitchenLanguage() throws {
    let s = try decodeFixture(RestaurantSettings.self, "settings")
    #expect(["en", "fr", "ar"].contains(s.kitchenTicketLanguage))
}

@Test func settingsWithoutKitchenLanguageFallsBackToReceipt() throws {
    let s = try JSONDecoder().decode(RestaurantSettings.self, from: Data(#"{"receiptLanguage":"ar"}"#.utf8))
    #expect(s.kitchenTicketLanguage == "ar")
}

@Test func decodesCategoryStation() throws {
    let cats = try decodeFixture([MenuCategory].self, "categories")
    #expect(cats.contains { $0.preparationStationId == "BAR" })
}

@Test func decodesPrinterStatusAndJobs() throws {
    #expect(!(try decodeFixture([PrinterStatus].self, "printer_status")).isEmpty)
    let jobs = try decodeFixture([PrintJobInfo].self, "print_jobs")
    #expect(!jobs.isEmpty)
    #expect(jobs.allSatisfy { ["Pending", "Failed", "Sent", "Cancelled"].contains($0.status) })
}

@Test func decodesPrintQueuedOnPayment() throws {
    #expect(try decodeFixture(PaymentResult.self, "pay_partial").printQueued == false)
}
```

`NetworkingTests.swift` :

```swift
@Test func realtimePrinterStatusEvent() {
    let id = "3f2504e0-4f89-11d3-9a0c-0305e82c3301"
    let e = RealtimeEvent.from(target: "OnPrinterStatusChanged", arguments: [.string(id), .string("Cuisine chaude"), .bool(false), .number(3)])
    #expect(e == .printerStatusChanged(printerId: UUID(uuidString: id), name: "Cuisine chaude", isOnline: false, pendingCount: 3))
}

@Test func paymentRequestEncodesReceiptFlag() throws {
    let req = PaymentRequest(orderId: nil, tableNumber: "T05", operatorId: nil, terminalId: "T01", tenders: [], requestReceiptPrint: true)
    let json = try #require(try JSONSerialization.jsonObject(with: JSONEncoder().encode(req)) as? [String: Any])
    #expect(json["requestReceiptPrint"] as? Bool == true)
}

@Test func cartLineWithoutProductStationSendsNil() {
    let line = CartLine(productId: UUID(), name: "Mojito", unitPrice: Money(cents: 800), taxRatePercent: 10)
    #expect(line.station == nil)
}
```

(Adapter `.string/.bool/.number` aux cas réels de `JSONValue`.)

`StoreTests.swift` (via `InMemoryPosAPI`, sur le modèle des tests de store existants) :

```swift
@Test func kitchenLanguageSaved() async {
    let api = InMemoryPosAPI()
    let store = SettingsStore(api: api)
    await store.load()
    await store.setKitchenTicketLanguage("ar")
    #expect(store.settings?.kitchenTicketLanguage == "ar")
    #expect(store.settings?.receiptLanguage == "fr")
}

@Test func retryFailedJobReloads() async throws {
    let api = InMemoryPosAPI()
    let store = PrintersStore(api: api)            // nom réel du store imprimantes
    let printer = try #require(try await api.printers().first)
    await api.seedFailedPrintJob(printerId: printer.id)
    await store.loadJobs(printerId: printer.id)
    let job = try #require(store.jobs[printer.id]?.first)
    await store.retry(job)
    #expect(store.jobs[printer.id]?.first?.status == "Pending")
}

@Test func tablePaymentSendsReceiptFlag() async throws {
    // Mise en place d'un test de paiement à table existant (table ouverte + une ligne), puis :
    // _ = await ticket.pay(method: .card, amount: total, tendered: total, printReceipt: true)
    // #expect(await api.lastPaymentRequest?.requestReceiptPrint == true)
}
```

Run: `cd ios && ./scripts/test.sh unit`
Expected: FAIL (compilation).

- [ ] **Step 3: Implement**

Modèles :

```swift
// OperationsModels.swift — RestaurantSettings
public var receiptLanguage: String
public var kitchenTicketLanguage: String

public init(receiptLanguage: String, kitchenTicketLanguage: String? = nil) {
    self.receiptLanguage = receiptLanguage
    self.kitchenTicketLanguage = kitchenTicketLanguage ?? receiptLanguage
}

public init(from decoder: Decoder) throws {
    let c = try decoder.container(keyedBy: CodingKeys.self)
    receiptLanguage = try c.decode(String.self, forKey: .receiptLanguage)
    kitchenTicketLanguage = try c.decodeIfPresent(String.self, forKey: .kitchenTicketLanguage) ?? receiptLanguage
}

/// État d'une imprimante (`GET /api/printers/status`). `isOnline` nil = aucun envoi depuis le démarrage du serveur.
public struct PrinterStatus: Codable, Identifiable, Hashable, Sendable {
    public var printerId: UUID
    public var name: String
    public var isActive: Bool
    public var isOnline: Bool?
    public var sinceUtc: Date?
    public var pendingCount: Int
    public var failedCount: Int
    public var id: UUID { printerId }
}

/// Impression en file (`GET /api/printers/{id}/jobs`). `kind` : PickupVoucher/Receipt/KitchenTicket ; `status` : Pending/Sent/Failed/Cancelled.
public struct PrintJobInfo: Codable, Identifiable, Hashable, Sendable {
    public var id: UUID
    public var printerId: UUID
    public var kind: String
    public var status: String
    public var attempts: Int
    public var createdAtUtc: Date
    public var sentAtUtc: Date?
    public var lastError: String?
}
```

`MenuCategory` : `public var preparationStationId: String?` + paramètre d'init `preparationStationId: String? = nil`.
`PaymentRequest` : `public var requestReceiptPrint: Bool` + paramètre d'init `requestReceiptPrint: Bool = false`.
`PaymentResult`, `CounterCheckoutResult` : `public var printQueued: Bool?`.

Poste optionnel : `CartLine.station: String?` (paramètre d'init `station: String? = nil`) ; `Product.station` renvoie `preparationStationId` (optionnel) ; corriger les erreurs de compilation en propageant l'optionnel. Là où un poste concret est requis (regroupement KDS `InMemoryPosAPI.swift:320`, affichage), utiliser `?? "HOT_KITCHEN"`. Le corps envoyé au serveur porte `null` quand l'article n'a pas de poste.

Temps réel (`SignalRClient.swift`) :

```swift
case printerStatusChanged(printerId: UUID?, name: String, isOnline: Bool, pendingCount: Int)
// …
case "OnPrinterStatusChanged":
    guard arguments.count >= 4 else { return nil }
    return .printerStatusChanged(
        printerId: arguments[0].decode(UUID.self),
        name: arguments[1].decode(String.self) ?? "",
        isOnline: arguments[2].decode(Bool.self) ?? false,
        pendingCount: arguments[3].decode(Int.self) ?? 0)
```

API (`PosAPI.swift` + `HTTPPosAPI.swift`) :

```swift
// PosAPI
func printerStatuses() async throws -> [PrinterStatus]
func printJobs(printerId: UUID) async throws -> [PrintJobInfo]
func retryPrintJob(id: UUID) async throws
func cancelPrintJob(id: UUID) async throws
func updateCategory(id: String, name: String, colorHex: String, displayOrder: Int, preparationStationId: String?) async throws

// HTTPPosAPI
public func printerStatuses() async throws -> [PrinterStatus] { try await call("GET", "printers/status") }
public func printJobs(printerId: UUID) async throws -> [PrintJobInfo] { try await call("GET", "printers/\(printerId.uuidString.lowercased())/jobs") }
```

`retryPrintJob` / `cancelPrintJob` : `POST print-jobs/{id}/retry|cancel` avec le helper sans corps de réponse déjà utilisé par `savePrinter` (l.365). `updateCategory` HTTP : ajouter `"preparationStationId": preparationStationId ?? ""` au corps (chaîne vide = effacer, comme le serveur).

`InMemoryPosAPI` : `preparationStationId` sur les catégories ; `kitchenTicketLanguage` dans `settingsStore` (validation `en/fr/ar`) ; `printJobsStore: [PrintJobInfo]` + `seedFailedPrintJob(printerId:)` ; `retryPrintJob` (Failed → Pending, sinon erreur 409) ; `cancelPrintJob` ; `printerStatuses()` dérivé de `printersStore` (`isOnline: nil`, compteurs depuis `printJobsStore`) ; `lastPaymentRequest` ; `printQueued` dans les réponses de paiement = ticket demandé ET imprimante `RECEIPT` présente.

Stores (`AdminStores.swift`) :
- `SettingsStore.setKitchenTicketLanguage(_ lang: String)` sur le modèle de `setReceiptLanguage` (l.482) : `settings = try await api.saveSettings(RestaurantSettings(receiptLanguage: settings?.receiptLanguage ?? "en", kitchenTicketLanguage: lang))`. Adapter `setReceiptLanguage` pour conserver `kitchenTicketLanguage` courant.
- Store imprimantes existant : `statuses`, `jobs`, `loadStatuses()`, `loadJobs(printerId:)`, `retry(_:)`, `cancel(_:)` (après chaque action : recharger jobs + statuts ; erreurs dans la propriété d'erreur existante du store).
- `saveCategory(id:name:colorHex:)` → ajouter `preparationStationId: String?`.

`TicketStore.pay(method:amount:tendered:printReceipt: Bool = false)` : poser `requestReceiptPrint: printReceipt` ; enregistrer `lastPrintQueued = result.printQueued`. Faire de même pour `counterCheckout` (`lastPrintQueued` depuis `CounterCheckoutResult.printQueued`).

`AppModel` (l.114) :

```swift
case let .printerStatusChanged(_, name, isOnline, pending):
    printerAlert = isOnline
        ? String(localized: "messages.printer_back_online \(name)", bundle: .module)
        : String(localized: "messages.printer_offline \(name) \(pending)", bundle: .module)
    Task { await printersStore.loadStatuses() }
```

(adapter au mécanisme d'accès aux chaînes déjà utilisé dans PosKit ; ajouter ces deux clés en/fr/ar au `Localizable.xcstrings` de PosKit avec les textes de la Task 9).

- [ ] **Step 4: Run tests**

Run: `cd ios && ./scripts/test.sh unit`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(impression): PosKit — langue cuisine, poste des familles, état et file des imprimantes"
```

---

### Task 11: iPad — écrans

**Files:**
- Modify: `ios/RestaurantPOS/Features/Admin/AdminScreen.swift` (`ReceiptLanguagePicker` l.581-600, `PrintersAdminView`, édition de famille dans `CatalogAdminView`, `stations` l.487)
- Modify: `ios/RestaurantPOS/Features/Payment/PaymentSheet.swift:250-265`
- Modify: la vue racine qui observe `AppModel` (bandeau global) pour `printerAlert`
- Modify: `ios/RestaurantPOS/Resources/Localizable.xcstrings`
- Test: `ios/RestaurantPOSUITests/PrintingUITests.swift`

**Interfaces:**
- Consumes: Task 10 (stores, modèles, `AppModel.printerAlert`, `TicketStore.lastPrintQueued`).

Écart assumé : l'iPad n'a pas d'écran Gestion → Appareils (appairage géré au back-office web), donc pas de sélecteur « Imprimante de ticket » par caisse côté iPad.

- [ ] **Step 1: Write the failing UI tests**

`PrintingUITests.swift` (lancement `-UITestMode -UITestPaired -UITestPin 1234 -UITestSection admin`, sur le modèle des tests admin existants de `PosUITestCase`) :

```swift
final class PrintingUITests: PosUITestCase {
    func testKitchenLanguagePickerAndPrinterStatus() {
        let app = launch(section: "admin")                      // helper existant
        app.buttons["admin.section.printers"].tap()             // identifiant réel de la section
        XCTAssertTrue(app.buttons["admin.kitchenTicketLanguage"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts.matching(identifier: "printer.status").firstMatch.waitForExistence(timeout: 5))
    }

    func testCategoryStationPicker() {
        let app = launch(section: "admin")
        app.buttons["admin.section.catalog"].tap()
        app.buttons.matching(identifier: "category.edit").firstMatch.tap()
        XCTAssertTrue(app.buttons["category.station"].waitForExistence(timeout: 5))
    }

    func testTablePaymentHasPrintReceiptToggle() {
        let app = launch(section: "floor")
        openTableWithOneLine(app)                                // helper des tests de paiement existants
        app.buttons["ticket.pay"].tap()
        XCTAssertTrue(app.switches["payment.printReceipt"].waitForExistence(timeout: 5))
    }
}
```

Remplacer les identifiants de section, `launch(section:)` et `openTableWithOneLine` par ceux réellement définis dans `AdminScreen.swift` et `PosUITestCase.swift`.

Run: `cd ios && xcodegen generate && xcodebuild test -project RestaurantPOS.xcodeproj -scheme RestaurantPOS -destination 'platform=iOS Simulator,name=iPad Pro 11-inch (M4)' -only-testing:RestaurantPOSUITests/PrintingUITests` (ou la commande équivalente de `scripts/test.sh`)
Expected: FAIL.

- [ ] **Step 2: Implement**

Réglages : à côté de `ReceiptLanguagePicker`, `KitchenTicketLanguagePicker` identique, lié à `settings?.kitchenTicketLanguage` / `setKitchenTicketLanguage`, `accessibilityIdentifier("admin.kitchenTicketLanguage")`, libellé `admin.kitchen_ticket_language`.

Famille : dans la feuille d'édition de famille, `Picker("admin.category_station_label", selection: $station)` avec l'option `""` (`admin.station_default_hot`) puis `ProductDraft.stations` ; `accessibilityIdentifier("category.station")` ; transmis à `saveCategory(..., preparationStationId:)`. Dans le sélecteur de poste des articles, ajouter l'option vide `admin.station_from_category`.

Imprimantes (`PrintersAdminView`) : `.task { await store.loadStatuses() }` ; par ligne, un `Text` avec `accessibilityIdentifier("printer.status")` : « En ligne » / « Hors ligne depuis HH:mm » / « État inconnu » + « n en attente » ; si `pendingCount + failedCount > 0`, un `DisclosureGroup("admin.printer_jobs_btn")` qui appelle `loadJobs(printerId:)` à l'ouverture et liste les jobs (type, heure, `lastError`) avec « Relancer » (`Failed`) ou « Annuler » (`Pending`).

Paiement (`PaymentSheet`) : `@State private var printTableReceipt = false` ; `Toggle("payment.print_receipt", isOn: $printTableReceipt).accessibilityIdentifier("payment.printReceipt")` affiché quand `!isCounter` ; `outcome = await model.ticket.pay(method: method, amount: amount, tendered: tendered, printReceipt: printTableReceipt)` ; si la demande d'impression (table ou comptoir) donne `model.ticket.lastPrintQueued == false`, afficher `payment.print_not_queued` avec le mécanisme d'alerte déjà utilisé par la feuille.

Notification : dans la vue racine, `.overlay(alignment: .top)` (ou le bandeau global existant) affichant `model.printerAlert`, effacé après 5 s (`try? await Task.sleep(for: .seconds(5)); model.printerAlert = nil` dans un `.task(id: model.printerAlert)`).

Chaînes app (`Localizable.xcstrings`, en/fr/ar) : reprendre clés et textes de la Task 9 utilisés ici (`admin.kitchen_ticket_language`, `admin.category_station_label`, `admin.station_default_hot`, `admin.station_from_category`, `admin.printer_status_*`, `admin.printer_pending`, `admin.printer_jobs_btn`, `admin.print_job_kind_*`, `admin.print_job_retry`, `admin.print_job_cancel`, `payment.print_receipt`, `payment.print_not_queued`).

- [ ] **Step 3: Run tests**

Run: `cd ios && xcodegen generate && ./scripts/test.sh unit && ./scripts/test.sh ui`
Puis : `POS_API_URL=http://localhost:5080 ./scripts/test.sh contract`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add ios
git commit -m "feat(impression): iPad — langue cuisine, poste des familles, état des imprimantes, ticket à table"
```

---

### Task 12: Documentation et essai manuel

**Files:**
- Create: `docs/impression.md`
- Modify: `CLAUDE.md` (sections *Realtime* et *Backend architecture*)

- [ ] **Step 1: Écrire `docs/impression.md`**

Contenu (en français) :

1. **Principe** : file `PrintJobs`, service d'impression, relances (+5 s, +15 s, +30 s, puis 60 s), échéance 30 min, purge 7 jours.
2. **Configuration** : déclarer les imprimantes (IP fixe, port 9100, largeur 58/80 mm, tiroir), postes (`RECEIPT` pour la caisse ; `HOT_KITCHEN`, `COLD`, `GRILL`, `DESSERT`, `BAR`), poste des familles et des articles (ordre de résolution ligne → article → famille → cuisine chaude), imprimante propre à une caisse (Gestion → Appareils, web), langues des tickets et des bons cuisine.
3. **Ce qui s'imprime quand** : tableau comptoir / table / cuisine (reprendre la section *Déclencheurs* de la spec).
4. **Dépannage** : Gestion → Imprimantes (état, jobs en échec, Relancer/Annuler) ; `printQueued: false` = aucune imprimante résolue ; bons cuisine absents = poste sans imprimante active.
5. **Essai manuel sur imprimante réelle** (checklist) :
   - [ ] impression de test en `fr` : accents corrects ; en `ar` : lettres liées, alignement à droite ;
   - [ ] vente comptoir espèces, sans ticket : bon de retrait + ouverture tiroir ;
   - [ ] vente comptoir carte, ticket demandé : ticket de caisse, coupe, bon de retrait ; pas de tiroir ;
   - [ ] paiement à table case cochée : ticket de caisse seul ; case décochée : rien ;
   - [ ] envoi en cuisine d'une table de 6 lignes sur 2 postes : un bon par poste, une copie par imprimante du poste ;
   - [ ] débrancher l'imprimante cuisine, envoyer 3 bons : notification « hors ligne — 3 bons en attente » ; rebrancher : les 3 bons sortent dans l'ordre, notification « rétablie » ;
   - [ ] chronométrer un ticket de caisse de 15 lignes : noter modèle et durée ; au-delà de ~3 s, ouvrir le sous-projet « mode texte `en`/`fr` » prévu par la spec A.
6. **Licence des polices** : Noto Sans / Noto Sans Arabic, OFL (`src/RestaurantPos.Infrastructure/Printing/Fonts/OFL.txt`).

- [ ] **Step 2: `CLAUDE.md`**

*Realtime* : ajouter `OnPrinterStatusChanged(printerId, printerName, isOnline, pendingCount)` aux événements consommés par les deux clients. *Backend architecture* : ajouter « Impression : `PrintDispatcher` met en file des `PrintJobs` après paiement / envoi en cuisine ; `PrintWorker` (hors `Testing`) les rend en raster ESC/POS (`Infrastructure/Printing/`) et les envoie en TCP 9100. Voir `docs/impression.md`. »

- [ ] **Step 3: Vérification finale**

Run: `dotnet format RestaurantPos.slnx --verify-no-changes && dotnet build RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Run: `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npm test`
Run: `cd ios && ./scripts/test.sh unit && ./scripts/test.sh ui`
Expected: tout PASS. Dérouler la checklist 1.5 sur une vraie imprimante et consigner modèle et durée dans `docs/impression.md`.

- [ ] **Step 4: Commit**

```bash
git add docs/impression.md CLAUDE.md
git commit -m "docs(impression): guide de configuration, dépannage et essai manuel"
```
