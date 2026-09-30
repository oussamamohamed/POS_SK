# Impression texte, rapports X/Z imprimés, pourboire à table — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Imprimer en texte ESC/POS (réglage par imprimante, fr/en), imprimer les rapports X/Z avec articles et pourboires par serveur, saisir le pourboire à table, bloquer la Z tant que des commandes sont en cours et interdire l'annulation d'une période clôturée.

**Architecture:** Un second rendu `EscPosTextRenderer` produit les octets à partir du même `TicketDocument` ; `EscPosPrinterTransport` choisit texte ou image selon `PrinterConfiguration.TextMode` et le sens du document. Les rapports passent par la file existante (`PrintJobKind.Report`) via `PrintDispatcher`, avec des données calculées par `ReportPrintDataService`. Les règles de clôture sont des méthodes du service fiscal appelées par les endpoints avant toute écriture ; la chaîne NF525 ne change pas.

**Tech Stack:** .NET 9 (Minimal API, EF Core SQLite/InMemory, xUnit + FluentAssertions), `System.Text.Encoding.CodePages` (inclus dans le framework), JS vanilla + Playwright, Swift 6 / SwiftUI / Swift Testing / XCUITest.

**Spec:** `docs/superpowers/specs/2026-09-30-impression-texte-rapports-design.md`

## Global Constraints

- Mode texte : `printer.TextMode && !document.RightToLeft` → texte ; sinon image (inchangé). Défaut `TextMode = false`.
- Texte : `ESC @`, `ESC t 19` (PC858), 48 colonnes (80 mm) / 32 (58 mm), grande taille = moitié ; caractère non encodable → `?`, jamais d'exception.
- Fin de document identique au rendu image : tiroir `1B 70 00 19 FA` (si demandé) puis coupe `1D 56 42 00`.
- `PUT` imprimante sans `textMode` → valeur existante conservée ; `POST` sans `textMode` → `false`.
- Pourboire à table accepté seulement si le paiement solde la commande (montant encaissé ≥ restant dû + pourboire) ; sinon 400, rien d'écrit. Pourboire négatif → 400.
- Montant fiscal d'un reçu : jamais le pourboire (reçu d'un paiement qui solde = TTC de la commande).
- Clôture Z refusée (409) s'il existe une commande en cours : au moins un article, statut ni `Paid` ni `Cancelled`, reste à encaisser > 0, panier en attente non annulé.
- Annulation refusée (409) pour un reçu couvert par une clôture : clôture de son terminal, ou clôture du terminal principal (`POS_MAIN_TERM`, `POS01`, vide), dont `PeriodEndUtc ≥ CreatedAtUtc` du reçu.
- Rapports : langue `ReceiptLanguage`, imprimante de caisse du terminal (règle existante `ReceiptPrinterAsync`), dates en heure locale du serveur `dd/MM/yyyy HH:mm`.
- Articles : familles triées par nom (`fr`, insensible à la casse), « Autres » en dernier ; articles triés par nom ; sous-total par famille. Pourboires : par `Order.OperatorId`, triés par nom, « Inconnu » en dernier ; section omise si total nul.
- Montants en centimes jusqu'au rendu, affichés `0.00` en culture invariante.
- Filtres et tris sur `DateTimeOffset` en mémoire (EF Core SQLite ne les traduit pas).
- `[LoggerMessage]` pour toute journalisation ; `TreatWarningsAsErrors` : `dotnet format RestaurantPos.slnx` avant build.
- Schéma : chaque colonne nouvelle a son `ALTER TABLE` idempotent dans le bloc non-`Testing` de `Program.cs`.
- `ios/RestaurantPOS.xcodeproj` est généré : modifier `ios/project.yml` si besoin, jamais le `.xcodeproj`.
- Chaînes nouvelles en en/fr/ar sur chaque couche (`.resx`, `i18n/*.json`, PosKit et app `.xcstrings`).
- Commits en français (`feat(impression): …`, `feat(fiscal): …`, `feat(paiement): …`).

## Review Focus

1. **Commande à 0 € ou panier en attente annulé qui bloque la Z pour toujours** (commande entièrement offerte, panier « annulé » dont la commande reste `Open`). → `FindOpenOrdersAsync` ignore reste dû nul et paniers `IsVoided` ; test Task 3 `OpenOrders_IgnoresEmptyZeroRemainingAndVoidedHeld_LabelsHeldByCustomer`.
2. **Reçu d'un iPad (`T01`) couvert par la Z du terminal principal** : la Z de `POS_MAIN_TERM` inclut les reçus de tous les terminaux ; ce reçu ne doit plus être annulable. → Test Task 3 `ClosedPeriod_OwnTerminalAndMainTerminalClosures`.
3. **Pourboire sur une part de partage** : il finirait dans le montant fiscal. → Refus serveur (test Task 2 `Pay_TipOnPartialPayment_Returns400_NothingWritten`) ; clients : pourboire seulement hors partage.
4. **Ancien client qui envoie un `PUT` imprimante sans `textMode`** : le mode texte ne doit pas se désactiver. → Test Task 1 `PutPrinter_WithoutTextMode_KeepsIt`.
5. **Libellé ou valeur plus longs que la ligne en texte** (signature 64 caractères, nom d'article long, 58 mm) : rien ne doit être perdu ni lever. → Tests Task 1 `Columns_TooLong_WrapsLabel_ValueOnNextLine`, `Text_WordLongerThanLine_IsHardSplit`.

Écart assumé : les articles d'un rapport sont sommés au TTC ligne (`CalculateTotalTtc`) ; une remise globale de commande n'y est pas répartie, donc la somme des articles peut différer du total fiscal. Le ticket l'indique par une ligne « montants avant remises sur note » seulement si une commande retenue a une remise globale (`GlobalDiscountType != null`).

---

## File Structure

**Domain** : Modify `Entities/PrinterConfiguration.cs` (`TextMode`), `Entities/PrintJob.cs` (`PrintJobKind.Report`).

**Application** : Modify `Common/Interfaces/IPrinterAndLayoutServices.cs` (`PrinterRegistrationRequest.TextMode`), `Common/Interfaces/INF525FiscalAuditService.cs` (`DailyFiscalClosureDto`, `OpenOrderDto`, `FindOpenOrdersAsync`, `IsInClosedPeriodAsync`).

**Infrastructure / Printing** : Create `EscPosTextRenderer.cs`, `ReportPrintDataService.cs` ; Modify `PrinterTransport.cs`, `TicketDocumentBuilder.cs` (`XReport`, `ZClosure`), `PrintDispatcher.cs` (`QueueXReportAsync`, `QueueZClosureAsync`).

**Infrastructure / Services** : Modify `PrinterConfigurationService.cs`, `NF525FiscalAuditService.cs`.

**Api** : Modify `Program.cs` (schéma, records `UpdatePrinterRequest`/`PaymentSettlementRequest`, DI), `Endpoints/PrinterEndpoints.cs`, `Endpoints/FiscalEndpoints.cs`, `Endpoints/CheckoutEndpoints.cs`, `Endpoints/CounterSaleEndpoints.cs`.

**Localisation** : `SharedResource.resx`, `.fr.resx`, `.ar.resx`.

**Web** : `wwwroot/app.js`, `index.html`, `i18n/{en,fr,ar}.json`, `tests/RestaurantPos.Web.E2ETests/tests/printing-reports.spec.ts`.

**iOS** : PosKit `Models/OperationsModels.swift`, `Models/OrderModels.swift`, `Networking/PosAPI.swift`, `HTTPPosAPI.swift`, `Testing/InMemoryPosAPI.swift`, `Stores/OperationsStores.swift`, `Stores/TicketStore.swift`, fixtures ; app `Features/Admin/AdminScreen.swift`, `Features/Fiscal/FiscalScreen.swift`, `Features/Payment/PaymentSheet.swift`, `Resources/Localizable.xcstrings`, UI tests.

**Docs** : `docs/impression.md`.

---

### Task 1: Mode texte par imprimante

**Files:**
- Modify: `src/RestaurantPos.Domain/Entities/PrinterConfiguration.cs`
- Modify: `src/RestaurantPos.Application/Common/Interfaces/IPrinterAndLayoutServices.cs` (`PrinterRegistrationRequest`)
- Modify: `src/RestaurantPos.Infrastructure/Services/PrinterConfigurationService.cs` (`RegisterPrinterAsync`, `UpdatePrinterAsync`)
- Modify: `src/RestaurantPos.Api/Program.cs` (schéma ; record `UpdatePrinterRequest` ~l.672)
- Modify: `src/RestaurantPos.Api/Endpoints/PrinterEndpoints.cs` (PUT)
- Create: `src/RestaurantPos.Infrastructure/Printing/EscPosTextRenderer.cs`
- Modify: `src/RestaurantPos.Infrastructure/Printing/PrinterTransport.cs`
- Test: `tests/RestaurantPos.Infrastructure.Tests/EscPosTextRendererTests.cs`, `PrinterTransportTests.cs`, `PrinterConfigurationServiceTests.cs` ; `tests/RestaurantPos.Api.Tests/PrintingConfigEndpointsTests.cs`

**Interfaces:**
- Produces: `PrinterConfiguration.TextMode : bool` ; `PrinterRegistrationRequest(..., List<string> AssignedStationIds, bool? TextMode = null)` ; `UpdatePrinterRequest(..., bool? IsActive, bool? TextMode = null)` ; `EscPosTextRenderer.ColumnsFor(int paperWidthMm) : int`, `EscPosTextRenderer.Render(TicketDocument doc, int paperWidthMm, bool openCashDrawer) : byte[]`, `EscPosTextRenderer.Wrap(string text, int width) : List<string>` ; `EscPosPrinterTransport.BuildPayload(PrinterConfiguration printer, TicketDocument document, bool openCashDrawer) : byte[]`.

- [ ] **Step 1: Write the failing tests**

`tests/RestaurantPos.Infrastructure.Tests/EscPosTextRendererTests.cs` :

```csharp
using System;
using System.Linq;
using System.Text;
using FluentAssertions;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class EscPosTextRendererTests
{
    private static readonly byte[] Header = [0x1B, 0x40, 0x1B, 0x74, 19];
    private static readonly byte[] Cut = [0x1D, 0x56, 0x42, 0x00];
    private static readonly byte[] Kick = [0x1B, 0x70, 0x00, 0x19, 0xFA];

    private static TicketDocument Doc(params TicketLine[] lines) => new("fr", false, lines);

    private static byte[] Body(byte[] bytes) => bytes[Header.Length..^Cut.Length];

    private static bool Contains(byte[] haystack, byte[] needle) =>
        Enumerable.Range(0, haystack.Length - needle.Length + 1).Any(i => haystack.AsSpan(i, needle.Length).SequenceEqual(needle));

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    [Theory]
    [InlineData(80, 48)]
    [InlineData(58, 32)]
    [InlineData(0, 48)]
    public void ColumnsFor_MapsPaperWidth(int mm, int cols) => EscPosTextRenderer.ColumnsFor(mm).Should().Be(cols);

    [Fact]
    public void Render_StartsWithInitAndCodePage_EndsWithCut()
    {
        var bytes = EscPosTextRenderer.Render(Doc(new TicketText("A")), 80, openCashDrawer: false);
        bytes.Take(5).Should().Equal(Header);
        bytes.TakeLast(4).Should().Equal(Cut);
        Contains(bytes, Kick).Should().BeFalse();
    }

    [Fact]
    public void Render_Drawer_KickBeforeFinalCut()
    {
        var bytes = EscPosTextRenderer.Render(Doc(new TicketText("A")), 80, openCashDrawer: true);
        bytes.TakeLast(9).Should().Equal(Kick.Concat(Cut));
    }

    [Fact]
    public void Text_CenteredBold_UsesAlignAndEmphasis()
    {
        var body = Body(EscPosTextRenderer.Render(Doc(new TicketText("TOTAL", TicketAlign.Center, Bold: true)), 80, false));
        body.Should().Equal([0x1B, 0x61, 1, 0x1B, 0x45, 1, .. Ascii("TOTAL"), 0x0A, 0x1B, 0x45, 0]);
    }

    [Fact]
    public void Text_Large_DoubleSize_AndHalfWidthWrap()
    {
        var body = Body(EscPosTextRenderer.Render(Doc(new TicketText(new string('A', 20) + " " + new string('B', 5), Large: true)), 58, false));
        body.Should().Equal([0x1B, 0x61, 0, 0x1D, 0x21, 0x11, .. Ascii(new string('A', 16)), 0x0A, .. Ascii("AAAA BBBBB"), 0x0A, 0x1D, 0x21, 0x00]);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(58)]
    public void Columns_PaddedToFullWidth(int mm)
    {
        var cols = EscPosTextRenderer.ColumnsFor(mm);
        var body = Body(EscPosTextRenderer.Render(Doc(new TicketColumns("Total", "12.50")), mm, false));
        body.Should().Equal([0x1B, 0x61, 0, .. Ascii("Total" + new string(' ', cols - 10) + "12.50"), 0x0A]);
    }

    [Fact]
    public void Columns_TooLong_WrapsLabel_ValueOnNextLine()
    {
        var label = "3x Entrecote grillee sauce bearnaise maison";   // 43 caractères ASCII
        var body = Body(EscPosTextRenderer.Render(Doc(new TicketColumns(label, "72.00")), 58, false));
        var lines = Encoding.ASCII.GetString(body[3..]).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().Equal("3x Entrecote grillee sauce", "bearnaise maison", new string(' ', 27) + "72.00");
    }

    [Fact]
    public void Text_WordLongerThanLine_IsHardSplit() =>
        EscPosTextRenderer.Wrap(new string('F', 64), 32).Should().Equal(new string('F', 32), new string('F', 32));

    [Fact]
    public void Wrap_EmptyOrSpaces_GivesOneEmptyLine() =>
        EscPosTextRenderer.Wrap("   ", 32).Should().Equal("");

    [Fact]
    public void Encoding_Pc858_AccentsEuroAndUnknown()
    {
        var body = Body(EscPosTextRenderer.Render(Doc(new TicketText("é è à ç € سفري")), 80, false));
        var textBytes = body[3..^1];
        textBytes.Take(10).Should().Equal(new byte[] { 0x82, 0x20, 0x8A, 0x20, 0x85, 0x20, 0x87, 0x20, 0xD5, 0x20 });
        textBytes.Skip(10).Should().OnlyContain(b => b == (byte)'?');
    }

    [Fact]
    public void Separator_DashesAcrossWidth_CutSeparatorCuts()
    {
        var body = Body(EscPosTextRenderer.Render(Doc(new TicketSeparator(), new TicketText("B"), new TicketSeparator(Cut: true), new TicketText("C")), 58, false));
        Contains(body, [0x1B, 0x61, 0, .. Ascii(new string('-', 32)), 0x0A]).Should().BeTrue();
        Contains(body, Cut).Should().BeTrue();
    }

    [Fact]
    public void TrailingCutSeparator_DoesNotDoubleCut()
    {
        var bytes = EscPosTextRenderer.Render(Doc(new TicketText("A"), new TicketSeparator(Cut: true)), 80, false);
        Enumerable.Range(0, bytes.Length - 3).Count(i => bytes.AsSpan(i, 4).SequenceEqual(Cut)).Should().Be(1);
    }
}
```

`tests/RestaurantPos.Infrastructure.Tests/PrinterTransportTests.cs` :

```csharp
using System.Linq;
using FluentAssertions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class PrinterTransportTests
{
    private static PrinterConfiguration Printer(bool textMode) => new() { Name = "P", IpAddress = "10.0.0.1", PaperWidthMm = 80, TextMode = textMode };
    private static TicketDocument Doc(string lang) => new(lang, lang == "ar", [new TicketText(lang == "ar" ? "سفري" : "Crème")]);

    private static bool IsText(byte[] payload) => payload.Take(5).SequenceEqual(new byte[] { 0x1B, 0x40, 0x1B, 0x74, 19 });

    [Theory]
    [InlineData(true, "fr", true)]
    [InlineData(true, "en", true)]
    [InlineData(true, "ar", false)]
    [InlineData(false, "fr", false)]
    public void BuildPayload_ChoosesTextOnlyForLtrWhenEnabled(bool textMode, string lang, bool expectText) =>
        IsText(EscPosPrinterTransport.BuildPayload(Printer(textMode), Doc(lang), false)).Should().Be(expectText);
}
```

`PrinterConfigurationServiceTests` — ajouter (helper `Create()` du fichier) :

```csharp
[Fact]
public async Task TextMode_DefaultsFalse_SetOnRegister_KeptWhenNullOnUpdate()
{
    var (_, _, service) = Create();
    var plain = await service.RegisterPrinterAsync(new PrinterRegistrationRequest("Caisse", "10.0.0.3", 9100, 80, false, ["RECEIPT"]));
    plain.TextMode.Should().BeFalse();

    var text = await service.RegisterPrinterAsync(new PrinterRegistrationRequest("Cuisine", "10.0.0.4", 9100, 58, false, ["HOT_KITCHEN"], TextMode: true));
    text.TextMode.Should().BeTrue();

    var updated = await service.UpdatePrinterAsync(text.Id, new PrinterRegistrationRequest("Cuisine 2", "10.0.0.4", 9100, 58, false, ["HOT_KITCHEN"]), isActive: true);
    updated.TextMode.Should().BeTrue();

    updated = await service.UpdatePrinterAsync(text.Id, new PrinterRegistrationRequest("Cuisine 2", "10.0.0.4", 9100, 58, false, ["HOT_KITCHEN"], TextMode: false), isActive: true);
    updated.TextMode.Should().BeFalse();
}
```

`PrintingConfigEndpointsTests` — ajouter :

```csharp
[Fact]
public async Task PutPrinter_WithoutTextMode_KeepsIt()
{
    var admin = await ClientAsync("9999");
    var created = await (await admin.PostAsJsonAsync("/api/printers", new { name = "Texte", ipAddress = "10.0.0.8", port = 9100, paperWidthMm = 80, openCashDrawerOnReceipt = false, assignedStationIds = new[] { "RECEIPT" }, textMode = true }))
        .Content.ReadFromJsonAsync<JsonElement>();
    var id = created.GetProperty("id").GetGuid();
    created.GetProperty("textMode").GetBoolean().Should().BeTrue();

    (await admin.PutAsJsonAsync($"/api/printers/{id}", new { name = "Texte", ipAddress = "10.0.0.8", port = 9100, paperWidthMm = 80, hasCashDrawer = false, targetStations = new[] { "RECEIPT" }, isActive = true }))
        .StatusCode.Should().Be(HttpStatusCode.OK);

    var list = await admin.GetFromJsonAsync<JsonElement>("/api/printers");
    list.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id).GetProperty("textMode").GetBoolean().Should().BeTrue();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test RestaurantPos.slnx --filter "FullyQualifiedName~EscPosTextRendererTests|FullyQualifiedName~PrinterTransportTests|FullyQualifiedName~PrinterConfigurationServiceTests|FullyQualifiedName~PrintingConfigEndpointsTests"`
Expected: échec de compilation (`TextMode`, `EscPosTextRenderer`, `BuildPayload`).

- [ ] **Step 3: Implement**

`PrinterConfiguration.cs`, après `OpenCashDrawerOnReceipt` :

```csharp
/// <summary>Documents LTR (fr/en) imprimés en texte ESC/POS au lieu d'une image ; l'arabe reste en image.</summary>
public bool TextMode { get; set; }
```

`IPrinterAndLayoutServices.cs` :

```csharp
public record PrinterRegistrationRequest(
    string Name,
    string IpAddress,
    int Port,
    int PaperWidthMm,
    bool OpenCashDrawerOnReceipt,
    List<string> AssignedStationIds,
    bool? TextMode = null
);
```

`PrinterConfigurationService` : `RegisterPrinterAsync` → `TextMode = request.TextMode ?? false,` ; `UpdatePrinterAsync` → `if (request.TextMode is { } textMode) printer.TextMode = textMode;`.

`Program.cs` : `public record UpdatePrinterRequest(string Name, string IpAddress, int Port, int PaperWidthMm, bool HasCashDrawer, List<string> TargetStations, bool? IsActive, bool? TextMode = null);` et dans le bloc schéma :

```csharp
try { dbContext.Database.ExecuteSqlRaw("ALTER TABLE PrinterConfigurations ADD COLUMN TextMode INTEGER NOT NULL DEFAULT 0;"); } catch { }
```

`PrinterEndpoints` PUT : `new PrinterRegistrationRequest(req.Name, req.IpAddress, req.Port, req.PaperWidthMm, req.HasCashDrawer, req.TargetStations ?? [], req.TextMode)`.

`EscPosTextRenderer.cs` :

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>Rendu texte ESC/POS (page de code PC858) d'un TicketDocument LTR. Plus rapide que l'image sur les imprimantes lentes.</summary>
public static class EscPosTextRenderer
{
    private static readonly Encoding Pc858 = CreateEncoding();
    private static readonly byte[] Init = [0x1B, 0x40, 0x1B, 0x74, 19];
    private static readonly byte[] DrawerKick = [0x1B, 0x70, 0x00, 0x19, 0xFA];
    private static readonly byte[] FeedAndCut = [0x1D, 0x56, 0x42, 0x00];

    public static int ColumnsFor(int paperWidthMm) => paperWidthMm == 58 ? 32 : 48;

    public static byte[] Render(TicketDocument doc, int paperWidthMm, bool openCashDrawer)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var cols = ColumnsFor(paperWidthMm);
        var bytes = new List<byte>(Init);
        var lines = doc.Lines.ToList();
        // La coupe finale est toujours ajoutée : une coupe en fin de document ferait une coupe à vide.
        while (lines.Count > 0 && lines[^1] is TicketSeparator { Cut: true }) lines.RemoveAt(lines.Count - 1);
        foreach (var line in lines)
        {
            switch (line)
            {
                case TicketText t:
                    AppendText(bytes, t, cols);
                    break;
                case TicketColumns c:
                    AppendColumns(bytes, c, cols);
                    break;
                case TicketSeparator { Cut: true }:
                    bytes.AddRange(FeedAndCut);
                    break;
                default:
                    bytes.AddRange([0x1B, 0x61, 0]);
                    AppendLine(bytes, new string('-', cols));
                    break;
            }
        }
        if (openCashDrawer) bytes.AddRange(DrawerKick);
        bytes.AddRange(FeedAndCut);
        return [.. bytes];
    }

    /// <summary>Coupe aux espaces ; un mot plus long que la ligne est coupé net. Toujours au moins une ligne.</summary>
    public static List<string> Wrap(string text, int width)
    {
        var result = new List<string>();
        var line = new StringBuilder();
        foreach (var word in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = word;
            while (rest.Length > width)
            {
                if (line.Length > 0) { result.Add(line.ToString()); line.Clear(); }
                result.Add(rest[..width]);
                rest = rest[width..];
            }
            if (rest.Length == 0) continue;
            if (line.Length > 0 && line.Length + 1 + rest.Length > width) { result.Add(line.ToString()); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(rest);
        }
        if (line.Length > 0) result.Add(line.ToString());
        if (result.Count == 0) result.Add(string.Empty);
        return result;
    }

    private static void AppendText(List<byte> bytes, TicketText t, int cols)
    {
        bytes.AddRange([0x1B, 0x61, t.Align switch { TicketAlign.Center => (byte)1, TicketAlign.End => (byte)2, _ => (byte)0 }]);
        if (t.Bold) bytes.AddRange([0x1B, 0x45, 1]);
        if (t.Large) bytes.AddRange([0x1D, 0x21, 0x11]);
        foreach (var l in Wrap(t.Text, t.Large ? cols / 2 : cols)) AppendLine(bytes, l);
        if (t.Large) bytes.AddRange([0x1D, 0x21, 0x00]);
        if (t.Bold) bytes.AddRange([0x1B, 0x45, 0]);
    }

    private static void AppendColumns(List<byte> bytes, TicketColumns c, int cols)
    {
        bytes.AddRange([0x1B, 0x61, 0]);
        if (c.Label.Length + 1 + c.Value.Length <= cols)
        {
            AppendLine(bytes, c.Label + new string(' ', cols - c.Label.Length - c.Value.Length) + c.Value);
            return;
        }
        foreach (var l in Wrap(c.Label, cols)) AppendLine(bytes, l);
        foreach (var l in Wrap(c.Value, cols)) AppendLine(bytes, l.PadLeft(cols));
    }

    private static void AppendLine(List<byte> bytes, string text)
    {
        bytes.AddRange(Pc858.GetBytes(text));
        bytes.Add(0x0A);
    }

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(858, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);
    }
}
```

`PrinterTransport.cs` :

```csharp
public sealed class EscPosPrinterTransport : IPrinterTransport
{
    public Task SendAsync(PrinterConfiguration printer, TicketDocument document, bool openCashDrawer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return EscPosSender.SendAsync(printer.IpAddress, printer.Port, BuildPayload(printer, document, openCashDrawer), ct);
    }

    /// <summary>Texte si l'imprimante est en mode texte et le document LTR ; sinon image (l'arabe exige la mise en forme HarfBuzz).</summary>
    public static byte[] BuildPayload(PrinterConfiguration printer, TicketDocument document, bool openCashDrawer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(document);
        return printer.TextMode && !document.RightToLeft
            ? EscPosTextRenderer.Render(document, printer.PaperWidthMm, openCashDrawer)
            : EscPosCommands.Build(EscPosRasterRenderer.Render(document, EscPosRasterRenderer.DotsFor(printer.PaperWidthMm)), openCashDrawer);
    }
}
```

L'impression de test passe déjà par `IPrinterTransport` : elle suit automatiquement la règle.

- [ ] **Step 4: Run tests**

Run: `dotnet format RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(impression): mode texte ESC/POS par imprimante (fr/en, page PC858)"
```

---

### Task 2: Pourboire à table et attribution au comptoir

**Files:**
- Modify: `src/RestaurantPos.Api/Program.cs` (record `PaymentSettlementRequest` ~l.676)
- Modify: `src/RestaurantPos.Api/Endpoints/CheckoutEndpoints.cs` (`/pay`)
- Modify: `src/RestaurantPos.Api/Endpoints/CounterSaleEndpoints.cs` (`/checkout`)
- Modify: `.resx` ×3
- Test: `tests/RestaurantPos.Api.Tests/TableTipTests.cs`, `tests/RestaurantPos.Infrastructure.Tests/CheckoutPaymentServiceTests.cs`

**Interfaces:**
- Produces: `PaymentSettlementRequest(..., bool RequestReceiptPrint = false, decimal TipAmount = 0m)` ; erreurs `errors.tip_invalid`, `errors.tip_only_on_final_payment` ; au comptoir, `Order.OperatorId` = opérateur connecté si vide.

- [ ] **Step 1: Write the failing tests**

`CheckoutPaymentServiceTests` — ajouter (mise en place du fichier : base InMemory, service, commande) :

```csharp
[Fact]
public async Task FullPayment_WithTip_ReceiptTtcExcludesTip()
{
    // commande : 1 ligne 20.00 TTC ; order.TipAmount = Money.FromCents(200) ; SaveChanges
    var result = await service.ProcessPaymentTendersAsync(order.Id, "T01", [new PaymentTenderRequest(PaymentMethod.CreditCard, 2200, 2200)]);
    result.RemainingBalanceCents.Should().Be(0);
    var receipt = await db.FiscalReceipts.SingleAsync(r => r.ReceiptNumber == result.ReceiptNumber);
    receipt.TotalTtcAmount.AmountInCents.Should().Be(2000);
}
```

`tests/RestaurantPos.Api.Tests/TableTipTests.cs` — fabrique neuve par test. Lire `CheckoutE2ETests.cs` pour la séquence ouverture de table (`POST /api/tables/{n}/open` + `OpenTableRequest(WaiterName, CoversCount, OperatorId)`) → lignes (`POST /api/tables/{n}/items`) → paiement (`POST /api/checkout/pay`), et écrire en entier :

| Test | Mise en place | Attendu |
|---|---|---|
| `Pay_FullWithTip_RecordsTipOnOrder_AttributedToTableServer` | T21 ouverte avec `OperatorId` = opérateur connecté, ligne 20.00 ; pay `amount 22.00`, `tipAmount 2.00` | 200, `remainingBalance` 0 ; `Order.TipAmount` = 200 cents ; `Order.OperatorId` = opérateur |
| `Pay_TipOnPartialPayment_Returns400_NothingWritten` | T22, ligne 20.00 ; pay `amount 10.00`, `tipAmount 1.00` | 400 (message traduit) ; `TipAmount` 0 ; aucun `FiscalReceipt` pour la commande |
| `Pay_NegativeTip_Returns400` | T23, ligne 20.00 ; pay `amount 20.00`, `tipAmount -1` | 400 ; aucun reçu |
| `CounterCheckout_SetsOperatorFromToken_WhenOrderHasNone` | comptoir (flux de `PrintTriggerTests`), ligne 6.50, `/checkout` avec `tipAmount 1.00`, tender 7.50 | `Order.OperatorId` = identifiant de l'opérateur connecté (claim `NameIdentifier`) ; `TipAmount` = 100 |

Lire l'état via `factory.Services.CreateScope()` → `AppDbContext` (`AsNoTracking`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test RestaurantPos.slnx --filter "FullyQualifiedName~TableTipTests|FullyQualifiedName~FullPayment_WithTip"`
Expected: `TableTipTests` FAIL (`tipAmount` ignoré ; `OperatorId` vide au comptoir). `FullPayment_WithTip_ReceiptTtcExcludesTip` peut déjà passer : il fige le comportement fiscal.

- [ ] **Step 3: Implement**

`Program.cs` : `public record PaymentSettlementRequest(Guid OrderId, string? TableNumber, Guid? OperatorId, List<TenderItemRequest> Tenders, string? TerminalId = null, bool RequestReceiptPrint = false, decimal TipAmount = 0m);`

`CheckoutEndpoints` `/pay`, après le calcul de `tenderRequests` et avant `ProcessPaymentTendersAsync` :

```csharp
if (req.TipAmount < 0)
{
    return Results.BadRequest(new { Message = Texts.T("errors.tip_invalid") });
}

Order? tippedOrder = null;
long tipCents = (long)Math.Round(req.TipAmount * 100);
if (tipCents > 0)
{
    tippedOrder = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderId);
    var paidCents = (await db.FiscalReceipts.Include(r => r.Tenders).Where(r => r.OrderId == orderId && !r.IsVoid).ToListAsync())
        .SelectMany(r => r.Tenders).Sum(t => t.Amount.AmountInCents);
    var remainingCents = tippedOrder.TotalTtc.AmountInCents + tippedOrder.TipAmount.AmountInCents - paidCents;
    // Un pourboire sur un paiement partiel entrerait dans le montant fiscal du reçu (ratio) : refusé.
    if (tenderRequests.Sum(t => t.AmountInCents) < remainingCents + tipCents)
    {
        return Results.BadRequest(new { Message = Texts.T("errors.tip_only_on_final_payment") });
    }
    tippedOrder.TipAmount = Money.FromCents(tippedOrder.TipAmount.AmountInCents + tipCents);
    await db.SaveChangesAsync();
}
```

Dans la branche `if (!result.IsSuccess)` existante, avant le `return` :

```csharp
if (tippedOrder is not null)
{
    tippedOrder.TipAmount = Money.FromCents(tippedOrder.TipAmount.AmountInCents - tipCents);
    await db.SaveChangesAsync();
}
```

`CounterSaleEndpoints` `/checkout`, juste après `order.PickupScheduledAtUtc = req.PickupScheduledAtUtc;` :

```csharp
if (order.OperatorId == Guid.Empty && Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var cashierId))
{
    order.OperatorId = cashierId;   // pourboire comptoir attribué à l'opérateur qui encaisse
}
```

`.resx` :

| Clé | en | fr | ar |
|---|---|---|---|
| `errors.tip_invalid` | Invalid tip amount. | Montant de pourboire invalide. | مبلغ الإكرامية غير صالح. |
| `errors.tip_only_on_final_payment` | A tip can only be added to the payment that settles the bill. | Le pourboire ne peut être ajouté qu'au paiement qui solde la note. | لا يمكن إضافة الإكرامية إلا إلى الدفعة التي تسدد الفاتورة. |

- [ ] **Step 4: Run tests**

Run: `dotnet format RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(paiement): pourboire à table sur le paiement qui solde, attribution comptoir à l'encaisseur"
```

---

### Task 3: Règles de clôture — Z bloquée, pas d'annulation après Z

**Files:**
- Modify: `src/RestaurantPos.Application/Common/Interfaces/INF525FiscalAuditService.cs`
- Modify: `src/RestaurantPos.Infrastructure/Services/NF525FiscalAuditService.cs`
- Modify: `src/RestaurantPos.Api/Endpoints/FiscalEndpoints.cs` (`/z-closure`)
- Modify: `src/RestaurantPos.Api/Endpoints/CheckoutEndpoints.cs` (`/void/{receiptId}`)
- Modify: `.resx` ×3
- Test: `tests/RestaurantPos.Infrastructure.Tests/ClosureRulesTests.cs`, `tests/RestaurantPos.Api.Tests/ClosureRulesApiTests.cs` ; existant `FiscalAndDashboardReportApiTests.cs`

**Interfaces:**
- Produces: `OpenOrderDto(Guid OrderId, string Label, long RemainingTtcCents)` ; `INF525FiscalAuditService.FindOpenOrdersAsync(CancellationToken cancellationToken = default) : Task<IReadOnlyList<OpenOrderDto>>` ; `INF525FiscalAuditService.IsInClosedPeriodAsync(string terminalId, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default) : Task<bool>` ; 409 `{ code: "open_orders", message, openOrders: [{ tableNumber, remainingTtc }] }` ; 409 `{ code: "void_after_closure", message }`.

- [ ] **Step 1: Write the failing tests**

`tests/RestaurantPos.Infrastructure.Tests/ClosureRulesTests.cs` (construire `NF525FiscalAuditService` comme dans `NF525FiscalAuditTests.cs`) :

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class ClosureRulesTests
{
    private static (NF525FiscalAuditService Fiscal, AppDbContext Db) Create()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("Closure_" + Guid.NewGuid().ToString("N")).Options);
        return (new NF525FiscalAuditService(db), db);   // mêmes dépendances que dans NF525FiscalAuditTests
    }

    private static Order AddOrder(AppDbContext db, string table, long unitCents, OrderStatus status = OrderStatus.Open, bool comp = false)
    {
        var order = new Order { TableNumber = table, Status = status };
        order.Items.Add(new OrderItem { ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(unitCents), IsComp = comp });
        db.Orders.Add(order);
        db.SaveChanges();
        return order;
    }

    [Fact]
    public async Task OpenOrders_ListsUnpaidTablesAndPartialPayments_WithRemaining()
    {
        var (fiscal, db) = Create();
        AddOrder(db, "T5", 1250);
        var partial = AddOrder(db, "T6", 2000);
        var receipt = new FiscalReceipt { TerminalId = "T01", ReceiptNumber = "T01-000001", OrderId = partial.Id, TotalTtcAmount = Money.FromCents(800) };
        receipt.Tenders.Add(new PaymentTender { Method = PaymentMethod.Cash, Amount = Money.FromCents(800) });
        db.FiscalReceipts.Add(receipt);
        AddOrder(db, "T7", 900, OrderStatus.Paid);
        AddOrder(db, "T8", 900, OrderStatus.Cancelled);
        db.SaveChanges();

        var open = await fiscal.FindOpenOrdersAsync();

        open.Select(o => (o.Label, o.RemainingTtcCents)).Should().Equal(("T5", 1250L), ("T6", 1200L));
    }

    [Fact]
    public async Task OpenOrders_IgnoresEmptyZeroRemainingAndVoidedHeld_LabelsHeldByCustomer()
    {
        var (fiscal, db) = Create();
        db.Orders.Add(new Order { TableNumber = "T9" });                         // table ouverte sans article
        db.SaveChanges();
        AddOrder(db, "T10", 1500, comp: true);                                    // entièrement offerte
        var voided = AddOrder(db, "Comptoir", 650);
        db.HeldOrders.Add(new HeldOrder { TerminalId = "T01", OrderId = voided.Id, CustomerLabel = "Client 1", OrderSnapshotJson = "{}", IsVoided = true });
        var held = AddOrder(db, "Comptoir", 700);
        db.HeldOrders.Add(new HeldOrder { TerminalId = "T01", OrderId = held.Id, CustomerLabel = "Client 2", OrderSnapshotJson = "{}" });
        db.SaveChanges();

        var open = await fiscal.FindOpenOrdersAsync();

        open.Should().ContainSingle().Which.Label.Should().Be("Client 2");
    }

    [Fact]
    public async Task ClosedPeriod_OwnTerminalAndMainTerminalClosures()
    {
        var (fiscal, db) = Create();
        var end = new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero);
        db.DailyFiscalClosures.Add(new DailyFiscalClosure { TerminalId = "POS_MAIN_TERM", ClosureSequence = 1, PeriodStartUtc = end.AddDays(-1), PeriodEndUtc = end, SignatureHash = "h", PreviousSignatureHash = "g" });
        db.SaveChanges();

        (await fiscal.IsInClosedPeriodAsync("T01", end.AddMinutes(-5))).Should().BeTrue();   // couvert par la Z du terminal principal
        (await fiscal.IsInClosedPeriodAsync("T01", end.AddMinutes(5))).Should().BeFalse();
        (await fiscal.IsInClosedPeriodAsync("POS_MAIN_TERM", end)).Should().BeTrue();
    }

    [Fact]
    public async Task ClosedPeriod_OtherTerminalClosureDoesNotCover()
    {
        var (fiscal, db) = Create();
        var end = DateTimeOffset.UtcNow;
        db.DailyFiscalClosures.Add(new DailyFiscalClosure { TerminalId = "T02", ClosureSequence = 1, PeriodStartUtc = end.AddDays(-1), PeriodEndUtc = end, SignatureHash = "h", PreviousSignatureHash = "g" });
        db.SaveChanges();
        (await fiscal.IsInClosedPeriodAsync("T01", end.AddMinutes(-5))).Should().BeFalse();
    }
}
```

(Ajuster les initialiseurs `required` de `DailyFiscalClosure`, `HeldOrder`, `FiscalReceipt` à leurs définitions réelles.)

`tests/RestaurantPos.Api.Tests/ClosureRulesApiTests.cs` (fabrique neuve par test ; manager `1234`) — écrire en entier :

| Test | Mise en place | Attendu |
|---|---|---|
| `ZClosure_WithOpenOrder_Returns409_WithList_NothingWritten` | seed direct (`AppDbContext` de la fabrique) : commande `Open` « T30 » avec une ligne 12.50 ; `POST /api/fiscal/z-closure` (`ZClosureRequest("POS_MAIN_TERM", Guid.NewGuid(), "Gérant")`) | 409 ; `code` = `open_orders` ; `openOrders[0].tableNumber` = « T30 », `remainingTtc` = 12.50 ; `message` contient « T30 » ; aucune `DailyFiscalClosure` écrite |
| `Void_ReceiptBeforeLatestClosure_Returns409` | appareil appairé (`DeviceTestHelper`) ; encaisser une commande (flux `CheckoutE2ETests`) → reçu R ; seed `DailyFiscalClosure` `POS_MAIN_TERM` avec `PeriodEndUtc` = maintenant + 1 s ; `POST /api/checkout/void/{R.Id}` | 409, `code` = `void_after_closure` ; `R.IsVoid` reste `false` |
| `Void_ReceiptAfterLatestClosure_StillAllowed` | seed d'abord la clôture avec `PeriodEndUtc` = maintenant − 1 h, puis encaisser et annuler | 200 |

Test existant qui exécute une Z par l'API (`FiscalAndDashboardReportApiTests.ZClosure_And_LatestClosure_Endpoints_ExecuteAndVerifyAccurateTotals`) : s'il laisse (ou si le seed laisse) des commandes ouvertes avec articles, les solder d'abord (`POST /api/checkout/pay`) avant la Z. Ne jamais affaiblir la règle. `ExecuteDailyZClosureAsync` n'est pas modifié (le contrôle est dans l'endpoint) : les tests de service ne sont pas concernés.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test RestaurantPos.slnx --filter "FullyQualifiedName~ClosureRules"`
Expected: échec de compilation (`FindOpenOrdersAsync`, `IsInClosedPeriodAsync`).

- [ ] **Step 3: Implement**

`INF525FiscalAuditService.cs` :

```csharp
/// <summary>Commande qui empêche la clôture Z : Label = table, ou libellé du panier comptoir en attente.</summary>
public record OpenOrderDto(Guid OrderId, string Label, long RemainingTtcCents);

// dans l'interface :
/// <summary>Commandes à encaisser avant une clôture Z (tout le restaurant).</summary>
Task<IReadOnlyList<OpenOrderDto>> FindOpenOrdersAsync(CancellationToken cancellationToken = default);

/// <summary>Vrai si un reçu de ce terminal émis à cette date est couvert par une clôture Z (du terminal ou du terminal principal).</summary>
Task<bool> IsInClosedPeriodAsync(string terminalId, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default);
```

`NF525FiscalAuditService.cs` :

```csharp
private static readonly string[] MainTerminalIds = ["", "POS_MAIN_TERM", "POS01"];

public async Task<IReadOnlyList<OpenOrderDto>> FindOpenOrdersAsync(CancellationToken cancellationToken = default)
{
    var orders = (await _dbContext.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
            .ToListAsync(cancellationToken).ConfigureAwait(false))
        .Where(o => o.Items.Count > 0)
        .ToList();
    if (orders.Count == 0) return [];

    var ids = orders.Select(o => o.Id).ToList();
    var held = await _dbContext.HeldOrders.AsNoTracking().Where(h => ids.Contains(h.OrderId)).ToListAsync(cancellationToken).ConfigureAwait(false);
    var paid = (await _dbContext.FiscalReceipts.AsNoTracking().Include(r => r.Tenders)
            .Where(r => ids.Contains(r.OrderId) && !r.IsVoid).ToListAsync(cancellationToken).ConfigureAwait(false))
        .GroupBy(r => r.OrderId)
        .ToDictionary(g => g.Key, g => g.SelectMany(r => r.Tenders).Sum(t => t.Amount.AmountInCents));

    var result = new List<OpenOrderDto>();
    foreach (var order in orders)
    {
        var hold = held.Where(h => h.OrderId == order.Id).OrderByDescending(h => h.HeldAtUtc).FirstOrDefault();
        if (hold is { IsVoided: true }) continue;   // panier annulé au code superviseur : la commande reste Open mais n'est plus à encaisser
        var remaining = order.TotalTtc.AmountInCents + order.TipAmount.AmountInCents - paid.GetValueOrDefault(order.Id);
        if (remaining <= 0) continue;               // commande entièrement offerte : rien à encaisser
        var label = hold is { IsRecalled: false } && !string.IsNullOrWhiteSpace(hold.CustomerLabel) ? hold.CustomerLabel : order.TableNumber;
        result.Add(new OpenOrderDto(order.Id, label, remaining));
    }
    return result.OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList();
}

public async Task<bool> IsInClosedPeriodAsync(string terminalId, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default)
{
    var term = terminalId ?? string.Empty;
    // La Z du terminal principal couvre les reçus de tous les terminaux (même périmètre que GenerateXReportAsync).
    var ends = (await _dbContext.DailyFiscalClosures.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
        .Where(c => c.TerminalId == term || MainTerminalIds.Contains(c.TerminalId))
        .Select(c => c.PeriodEndUtc)
        .ToList();
    return ends.Count > 0 && createdAtUtc <= ends.Max();
}
```

`FiscalEndpoints` `/z-closure`, après la validation du gérant et avant `ExecuteDailyZClosureAsync` :

```csharp
var open = await fiscal.FindOpenOrdersAsync();
if (open.Count > 0)
{
    var list = string.Join(", ", open.Select(o => $"{o.Label} ({(o.RemainingTtcCents / 100m).ToString("0.00", CultureInfo.InvariantCulture)})"));
    return Results.Json(new
    {
        code = "open_orders",
        message = Texts.T("errors.z_closure_open_orders", ("count", open.Count), ("orders", list)),
        openOrders = open.Select(o => new { tableNumber = o.Label, remainingTtc = o.RemainingTtcCents / 100m })
    }, statusCode: StatusCodes.Status409Conflict);
}
```

`CheckoutEndpoints` `/void/{receiptId}` : injecter `AppDbContext db` et `INF525FiscalAuditService fiscal` ; après le contrôle `OperatorId` :

```csharp
var original = await db.FiscalReceipts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == receiptId);
if (original is not null && await fiscal.IsInClosedPeriodAsync(original.TerminalId, original.CreatedAtUtc))
{
    return Results.Json(new { code = "void_after_closure", message = Texts.T("errors.void_after_closure") }, statusCode: StatusCodes.Status409Conflict);
}
```

`.resx` :

| Clé | en | fr | ar |
|---|---|---|---|
| `errors.z_closure_open_orders` | Z closure not possible: settle the {count} open order(s) first: {orders} | Clôture Z impossible : encaissez d'abord {count} commande(s) en cours : {orders} | لا يمكن الإغلاق Z: يجب أولاً تحصيل {count} طلب(ات) مفتوحة: {orders} |
| `errors.void_after_closure` | This receipt belongs to a closed period (Z) and can no longer be voided. | Ce ticket appartient à une période clôturée (Z) et ne peut plus être annulé. | هذا الإيصال ضمن فترة مغلقة (Z) ولا يمكن إلغاؤه. |

- [ ] **Step 4: Run tests**

Run: `dotnet format RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS (y compris le test Z existant, adapté).

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(fiscal): clôture Z refusée avec commandes en cours, pas d'annulation après Z"
```

---

### Task 4: Données et documents des rapports X/Z

**Files:**
- Modify: `src/RestaurantPos.Application/Common/Interfaces/INF525FiscalAuditService.cs` (`DailyFiscalClosureDto`)
- Modify: `src/RestaurantPos.Infrastructure/Services/NF525FiscalAuditService.cs:185,261` (construction du DTO)
- Create: `src/RestaurantPos.Infrastructure/Printing/ReportPrintDataService.cs`
- Modify: `src/RestaurantPos.Infrastructure/Printing/TicketDocumentBuilder.cs`
- Modify: `.resx` ×3
- Test: `tests/RestaurantPos.Infrastructure.Tests/ReportPrintDataServiceTests.cs`, `TicketDocumentBuilderTests.cs`

**Interfaces:**
- Consumes: Task 2 (`Order.OperatorId` au comptoir).
- Produces:
  - `DailyFiscalClosureDto(..., string SignatureHash, DateTimeOffset ClosedAtUtc, DateTimeOffset PeriodStartUtc, string SealedByUserName)`.
  - `ReportItemLine(string ProductName, int Quantity, long TotalTtcCents)`, `ReportCategoryGroup(string? CategoryName, IReadOnlyList<ReportItemLine> Items, long SubtotalTtcCents)` (`null` = Autres), `ReportTipLine(string? ServerName, long TipCents)` (`null` = Inconnu), `ReportPrintData(IReadOnlyList<ReportCategoryGroup> Categories, IReadOnlyList<ReportTipLine> Tips, long TotalTipsCents, bool HasGlobalDiscount)`.
  - `ReportPrintDataService(AppDbContext db)` (scoped) : `Task<ReportPrintData> BuildAsync(string terminalId, DateTimeOffset periodStartUtc, DateTimeOffset periodEndUtc, CancellationToken ct = default)`.
  - `TicketDocumentBuilder.XReport(FiscalSummaryDto summary, ReportPrintData data, string language, DateTimeOffset nowUtc)`, `TicketDocumentBuilder.ZClosure(DailyFiscalClosureDto closure, ReportPrintData data, string language)`.

- [ ] **Step 1: Write the failing tests**

`ReportPrintDataServiceTests.cs` :

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class ReportPrintDataServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddHours(16);

    private sealed class Seed
    {
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("Report_" + Guid.NewGuid().ToString("N")).Options);
        private int _seq;

        public Product Product(string name, string? categoryId)
        {
            var p = new Product { Name = name, CategoryId = categoryId ?? "CAT-DELETED", Price = Money.FromCents(100) };
            Db.Products.Add(p);
            Db.SaveChanges();
            return p;
        }

        public void Category(string id, string name)
        {
            Db.Categories.Add(new Category { Id = id, Name = name });
            Db.SaveChanges();
        }

        public User Server(string name)
        {
            var u = new User { Name = name, PinHash = "x" };   // compléter avec les champs requis de User
            Db.Users.Add(u);
            Db.SaveChanges();
            return u;
        }

        public Order PaidOrder(DateTimeOffset at, Guid operatorId, long tipCents, params (Product P, int Qty, long UnitCents, bool Comp)[] lines)
        {
            var order = new Order { Status = OrderStatus.Paid, OperatorId = operatorId, TipAmount = Money.FromCents(tipCents) };
            foreach (var l in lines)
                order.Items.Add(new OrderItem { ProductId = l.P.Id, ProductName = l.P.Name, Quantity = l.Qty, UnitPrice = Money.FromCents(l.UnitCents), IsComp = l.Comp });
            Db.Orders.Add(order);
            Db.FiscalReceipts.Add(new FiscalReceipt { TerminalId = "T01", ReceiptNumber = $"T01-{++_seq:D6}", OrderId = order.Id, TotalTtcAmount = Money.FromCents(Math.Max(1, order.TotalTtc.AmountInCents)), CreatedAtUtc = at });
            Db.SaveChanges();
            return order;
        }
    }

    [Fact]
    public async Task Items_GroupedByCategory_SortedByCategoryThenName_OthersLast()
    {
        var s = new Seed();
        s.Category("C1", "Plats"); s.Category("C2", "Desserts"); s.Category("C3", "boissons");
        var tiramisu = s.Product("Tiramisu", "C2");
        var brownie = s.Product("Brownie", "C2");
        var steak = s.Product("Entrecôte", "C1");
        var eau = s.Product("Eau", "C3");
        var ghost = s.Product("Vente Comptoir", null);
        s.PaidOrder(Start.AddHours(1), Guid.Empty, 0, (tiramisu, 2, 700, false), (steak, 1, 2200, false), (brownie, 1, 650, true));
        s.PaidOrder(Start.AddHours(2), Guid.Empty, 0, (tiramisu, 1, 700, false), (eau, 3, 300, false), (ghost, 1, 500, false));

        var data = await new ReportPrintDataService(s.Db).BuildAsync("POS_MAIN_TERM", Start, End);

        data.Categories.Select(c => c.CategoryName).Should().Equal("boissons", "Desserts", "Plats", null);
        var desserts = data.Categories[1];
        desserts.Items.Should().Equal(new ReportItemLine("Brownie", 1, 0), new ReportItemLine("Tiramisu", 3, 2100));
        desserts.SubtotalTtcCents.Should().Be(2100);
        data.Categories[^1].Items.Single().ProductName.Should().Be("Vente Comptoir");
    }

    [Fact]
    public async Task ExcludesCancelledOrders_OutOfPeriod_AndOtherTerminalWhenNotMain()
    {
        var s = new Seed();
        s.Category("C1", "Plats");
        var steak = s.Product("Entrecôte", "C1");
        s.PaidOrder(Start.AddHours(1), Guid.Empty, 0, (steak, 1, 2200, false));
        s.PaidOrder(Start.AddHours(-1), Guid.Empty, 0, (steak, 5, 2200, false));
        var cancelled = s.PaidOrder(Start.AddHours(3), Guid.Empty, 0, (steak, 7, 2200, false));
        cancelled.Status = OrderStatus.Cancelled;
        s.Db.SaveChanges();

        var data = await new ReportPrintDataService(s.Db).BuildAsync("POS_MAIN_TERM", Start, End);
        data.Categories.Single().Items.Single().Quantity.Should().Be(1);

        (await new ReportPrintDataService(s.Db).BuildAsync("T02", Start, End)).Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task Tips_ByServer_SortedByName_UnknownLast_TotalAndOmittedWhenZero()
    {
        var s = new Seed();
        var steak = s.Product("Entrecôte", null);
        var zoe = s.Server("Zoé");
        var alex = s.Server("Alex");
        s.PaidOrder(Start.AddHours(1), zoe.Id, 300, (steak, 1, 2200, false));
        s.PaidOrder(Start.AddHours(2), alex.Id, 150, (steak, 1, 2200, false));
        s.PaidOrder(Start.AddHours(3), zoe.Id, 200, (steak, 1, 2200, false));
        s.PaidOrder(Start.AddHours(4), Guid.Empty, 100, (steak, 1, 2200, false));
        s.PaidOrder(Start.AddHours(5), alex.Id, 0, (steak, 1, 2200, false));

        var data = await new ReportPrintDataService(s.Db).BuildAsync("POS_MAIN_TERM", Start, End);

        data.Tips.Should().Equal(new ReportTipLine("Alex", 150), new ReportTipLine("Zoé", 500), new ReportTipLine(null, 100));
        data.TotalTipsCents.Should().Be(750);
    }
}
```

`TicketDocumentBuilderTests` — ajouter (`using RestaurantPos.Application.Common.Interfaces;` et `using RestaurantPos.Infrastructure.Localization;` si absents) :

```csharp
private static ReportPrintData SampleReportData(bool withTips = true) => new(
    [new ReportCategoryGroup("Desserts", [new ReportItemLine("Tiramisu", 3, 2100)], 2100), new ReportCategoryGroup(null, [new ReportItemLine("Vente Comptoir", 1, 500)], 500)],
    withTips ? [new ReportTipLine("Alex", 150), new ReportTipLine(null, 100)] : [],
    withTips ? 250 : 0,
    HasGlobalDiscount: false);

private static FiscalSummaryDto SampleSummary() => new("POS_MAIN_TERM", DateTimeOffset.MinValue, new DateTimeOffset(2026, 9, 30, 20, 0, 0, TimeSpan.Zero),
    2600, 2364, 2, new Dictionary<decimal, long> { [10m] = 236 }, new Dictionary<PaymentMethod, long> { [PaymentMethod.Cash] = 2600 }, 99_000);

[Theory]
[InlineData("en", "X REPORT", "ITEMS SOLD", "Other", "Unknown")]
[InlineData("fr", "RAPPORT X", "ARTICLES VENDUS", "Autres", "Inconnu")]
[InlineData("ar", "تقرير X", "الأصناف المباعة", "أخرى", "غير معروف")]
public void XReport_ContainsTotalsItemsAndTips_Localized(string lang, string title, string items, string other, string unknown)
{
    var doc = TicketDocumentBuilder.XReport(SampleSummary(), SampleReportData(), lang, DateTimeOffset.UnixEpoch);
    var text = AllText(doc);
    doc.Lines[0].Should().BeOfType<TicketText>().Which.Text.Should().Be(title);
    text.Should().Contain(items).And.Contain("3x Tiramisu | 21.00").And.Contain(other).And.Contain(unknown)
        .And.Contain(Texts.Get(CultureInfo.GetCultureInfo(lang), "admin.payment_method_cash"))
        .And.Contain("26.00").And.Contain("990.00").And.Contain("—");
}

[Fact]
public void XReport_NoTips_OmitsTipsSection() =>
    AllText(TicketDocumentBuilder.XReport(SampleSummary(), SampleReportData(withTips: false), "fr", DateTimeOffset.UnixEpoch))
        .Should().NotContain("POURBOIRES");

[Fact]
public void ZClosure_HasSequenceManagerAndSignature()
{
    var closure = new DailyFiscalClosureDto(Guid.NewGuid(), "POS_MAIN_TERM", 12, 2600, 2364, 2, new Dictionary<decimal, long> { [10m] = 236 },
        new Dictionary<PaymentMethod, long> { [PaymentMethod.CreditCard] = 2600 }, 99_000, "abc123", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(-10), "Alexandre");
    var text = AllText(TicketDocumentBuilder.ZClosure(closure, SampleReportData(), "fr"));
    text.Should().StartWith("CLÔTURE Z n° 12").And.Contain("Alexandre").And.Contain("abc123").And.Contain("ARTICLES VENDUS");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "FullyQualifiedName~ReportPrintDataServiceTests|FullyQualifiedName~TicketDocumentBuilderTests"`
Expected: échec de compilation.

- [ ] **Step 3: Implement**

`DailyFiscalClosureDto` : ajouter en fin `DateTimeOffset PeriodStartUtc, string SealedByUserName`. `NF525FiscalAuditService` l.185 (Z) : passer `closure.PeriodStartUtc, closure.SealedByUserName` ; l.261 (`GetLatestZClosureAsync`) : passer `closure.PeriodStartUtc, closure.SealedByUserName ?? string.Empty`. Les endpoints renvoient des objets anonymes : contrat JSON inchangé. Corriger les autres `new DailyFiscalClosureDto(` s'il y en a (`grep -rn "new DailyFiscalClosureDto" src tests`).

`ReportPrintDataService.cs` :

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Printing;

public sealed record ReportItemLine(string ProductName, int Quantity, long TotalTtcCents);
/// <param name="CategoryName">null = articles sans famille connue (« Autres »).</param>
public sealed record ReportCategoryGroup(string? CategoryName, IReadOnlyList<ReportItemLine> Items, long SubtotalTtcCents);
/// <param name="ServerName">null = serveur inconnu.</param>
public sealed record ReportTipLine(string? ServerName, long TipCents);
public sealed record ReportPrintData(IReadOnlyList<ReportCategoryGroup> Categories, IReadOnlyList<ReportTipLine> Tips, long TotalTipsCents, bool HasGlobalDiscount);

/// <summary>Articles vendus et pourboires d'une période de rapport. Informatif : n'entre ni dans les totaux fiscaux ni dans la signature Z.</summary>
public sealed class ReportPrintDataService
{
    private static readonly StringComparer French = StringComparer.Create(CultureInfo.GetCultureInfo("fr"), ignoreCase: true);
    private readonly AppDbContext _db;

    public ReportPrintDataService(AppDbContext db) => _db = db;

    public async Task<ReportPrintData> BuildAsync(string terminalId, DateTimeOffset periodStartUtc, DateTimeOffset periodEndUtc, CancellationToken ct = default)
    {
        // Même périmètre que GenerateXReportAsync : le terminal principal couvre tous les terminaux.
        var isMainTerminal = string.IsNullOrWhiteSpace(terminalId) || terminalId == "POS_MAIN_TERM";
        var orderIds = (await _db.FiscalReceipts.AsNoTracking()
                .Where(r => !r.IsVoid && (isMainTerminal || r.TerminalId == terminalId))
                .ToListAsync(ct).ConfigureAwait(false))
            .Where(r => r.TotalTtcAmount.AmountInCents > 0 && r.CreatedAtUtc >= periodStartUtc && r.CreatedAtUtc <= periodEndUtc)
            .Select(r => r.OrderId).Distinct().ToList();

        var orders = (await _db.Orders.AsNoTracking().Include(o => o.Items)
                .Where(o => orderIds.Contains(o.Id)).ToListAsync(ct).ConfigureAwait(false))
            .Where(o => o.Status != OrderStatus.Cancelled).ToList();

        var productCategory = await _db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.CategoryId, ct).ConfigureAwait(false);
        var categoryName = await _db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        var userName = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct).ConfigureAwait(false);

        var categories = orders.SelectMany(o => o.Items)
            .GroupBy(i => productCategory.TryGetValue(i.ProductId, out var cat) && categoryName.TryGetValue(cat, out var name) ? name : null)
            .Select(g => new ReportCategoryGroup(
                g.Key,
                g.GroupBy(i => i.ProductName)
                    .Select(p => new ReportItemLine(p.Key, p.Sum(i => i.Quantity), p.Sum(LineTtc)))
                    .OrderBy(l => l.ProductName, French).ToList(),
                g.Sum(LineTtc)))
            .OrderBy(g => g.CategoryName is null).ThenBy(g => g.CategoryName, French)
            .ToList();

        var tips = orders.Where(o => o.TipAmount.AmountInCents > 0)
            .GroupBy(o => o.OperatorId)
            .Select(g => new ReportTipLine(userName.TryGetValue(g.Key, out var name) ? name : null, g.Sum(o => o.TipAmount.AmountInCents)))
            .OrderBy(t => t.ServerName is null).ThenBy(t => t.ServerName, French)
            .ToList();

        return new ReportPrintData(categories, tips, tips.Sum(t => t.TipCents), orders.Any(o => o.GlobalDiscountType is not null));
    }

    private static long LineTtc(OrderItem item) => item.IsComp ? 0 : item.CalculateTotalTtc().AmountInCents;
}
```

`TicketDocumentBuilder` — ajouter (`using RestaurantPos.Application.Common.Interfaces;` si absent) :

```csharp
public static TicketDocument XReport(FiscalSummaryDto summary, ReportPrintData data, string language, DateTimeOffset nowUtc)
{
    ArgumentNullException.ThrowIfNull(summary);
    ArgumentNullException.ThrowIfNull(data);
    var (lang, c) = Resolve(language);
    var lines = new List<TicketLine>
    {
        new TicketText(Texts.Get(c, "report.x_title"), TicketAlign.Center, Large: true),
        new TicketSeparator(),
        new TicketColumns(Texts.Get(c, "receipt.terminal"), summary.TerminalId),
        new TicketColumns(Texts.Get(c, "report.period_start"), LocalDate(summary.PeriodStartUtc)),
        new TicketColumns(Texts.Get(c, "report.period_end"), LocalDate(summary.PeriodEndUtc)),
        new TicketColumns(Texts.Get(c, "report.printed_at"), LocalDate(nowUtc))
    };
    AppendReportTotals(lines, c, summary.ReceiptCount, summary.TotalSalesTtcCents, summary.TotalSalesHtCents, summary.VatBreakdownCents, summary.PaymentTotalsCents, summary.PerpetualGrandTotalCents);
    AppendReportDetails(lines, c, data);
    return new TicketDocument(lang, lang == "ar", lines);
}

public static TicketDocument ZClosure(DailyFiscalClosureDto closure, ReportPrintData data, string language)
{
    ArgumentNullException.ThrowIfNull(closure);
    ArgumentNullException.ThrowIfNull(data);
    var (lang, c) = Resolve(language);
    var lines = new List<TicketLine>
    {
        new TicketText(Texts.Get(c, "report.z_title", ("sequence", closure.ClosureSequence)), TicketAlign.Center, Large: true),
        new TicketSeparator(),
        new TicketColumns(Texts.Get(c, "receipt.terminal"), closure.TerminalId),
        new TicketColumns(Texts.Get(c, "report.period_start"), LocalDate(closure.PeriodStartUtc)),
        new TicketColumns(Texts.Get(c, "report.closed_at"), LocalDate(closure.ClosedAtUtc)),
        new TicketColumns(Texts.Get(c, "report.manager"), closure.SealedByUserName)
    };
    AppendReportTotals(lines, c, closure.ReceiptCount, closure.TotalSalesTtcCents, closure.TotalSalesHtCents, closure.VatBreakdownCents, closure.PaymentTotalsCents, closure.PerpetualGrandTotalCents);
    AppendReportDetails(lines, c, data);
    lines.Add(new TicketSeparator());
    lines.Add(new TicketText(Texts.Get(c, "report.signature"), Bold: true));
    lines.Add(new TicketText(closure.SignatureHash));
    return new TicketDocument(lang, lang == "ar", lines);
}

private static void AppendReportTotals(List<TicketLine> lines, CultureInfo c, int receiptCount, long ttc, long ht,
    IReadOnlyDictionary<decimal, long> vat, IReadOnlyDictionary<PaymentMethod, long> payments, long grandTotal)
{
    lines.Add(new TicketSeparator());
    lines.Add(new TicketColumns(Texts.Get(c, "report.receipt_count"), receiptCount.ToString(CultureInfo.InvariantCulture)));
    lines.Add(new TicketColumns(Texts.Get(c, "receipt.total_ttc"), Amount(ttc)));
    lines.Add(new TicketColumns(Texts.Get(c, "receipt.total_ht"), Amount(ht)));
    lines.Add(new TicketSeparator());
    lines.Add(new TicketText(Texts.Get(c, "receipt.vat_breakdown"), Bold: true));
    foreach (var (rate, cents) in vat.OrderBy(v => v.Key))
        lines.Add(new TicketColumns($"{rate.ToString("0.##", CultureInfo.InvariantCulture)} %", Amount(cents)));
    lines.Add(new TicketSeparator());
    lines.Add(new TicketText(Texts.Get(c, "report.payments"), Bold: true));
    foreach (var (method, cents) in payments.OrderBy(p => p.Key))
        lines.Add(new TicketColumns(PaymentLabel(c, method), Amount(cents)));
    lines.Add(new TicketSeparator());
    lines.Add(new TicketColumns(Texts.Get(c, "report.grand_total"), Amount(grandTotal)));
}

private static void AppendReportDetails(List<TicketLine> lines, CultureInfo c, ReportPrintData data)
{
    if (data.Categories.Count > 0)
    {
        lines.Add(new TicketSeparator());
        lines.Add(new TicketText(Texts.Get(c, "report.items_sold"), TicketAlign.Center, Bold: true));
        if (data.HasGlobalDiscount) lines.Add(new TicketText(Texts.Get(c, "report.items_before_discount")));
        foreach (var group in data.Categories)
        {
            lines.Add(new TicketText(group.CategoryName ?? Texts.Get(c, "report.other_category"), Bold: true));
            foreach (var item in group.Items)
                lines.Add(new TicketColumns($"{item.Quantity}x {item.ProductName}", Amount(item.TotalTtcCents)));
            lines.Add(new TicketColumns(Texts.Get(c, "report.subtotal"), Amount(group.SubtotalTtcCents)));
        }
    }
    if (data.TotalTipsCents > 0)
    {
        lines.Add(new TicketSeparator());
        lines.Add(new TicketText(Texts.Get(c, "report.tips_by_server"), TicketAlign.Center, Bold: true));
        foreach (var tip in data.Tips)
            lines.Add(new TicketColumns(tip.ServerName ?? Texts.Get(c, "report.unknown_server"), Amount(tip.TipCents)));
        lines.Add(new TicketColumns(Texts.Get(c, "report.tips_total"), Amount(data.TotalTipsCents)));
    }
}

private static string PaymentLabel(CultureInfo c, PaymentMethod method) => Texts.Get(c, method switch
{
    PaymentMethod.Cash => "admin.payment_method_cash",
    PaymentMethod.CreditCard => "admin.payment_method_credit_card",
    PaymentMethod.MealVoucher => "admin.payment_method_meal_voucher",
    PaymentMethod.GiftCard => "admin.payment_method_gift_card",
    PaymentMethod.RoomCharge => "admin.payment_method_room_charge",
    _ => "admin.payment_method_other"
});

// Heure locale du serveur (restaurant) ; DateTimeOffset.MinValue = premier rapport sans clôture précédente.
private static string LocalDate(DateTimeOffset utc) =>
    utc == DateTimeOffset.MinValue ? "—" : utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
```

`.resx` :

| Clé | en | fr | ar |
|---|---|---|---|
| `report.x_title` | X REPORT | RAPPORT X | تقرير X |
| `report.z_title` | Z CLOSURE #{sequence} | CLÔTURE Z n° {sequence} | إغلاق Z رقم {sequence} |
| `report.period_start` | From | Du | من |
| `report.period_end` | To | Au | إلى |
| `report.printed_at` | Printed | Imprimé le | طُبع في |
| `report.closed_at` | Closed | Clôturé le | أُغلق في |
| `report.manager` | Manager | Gérant | المدير |
| `report.receipt_count` | Receipts | Tickets | عدد الإيصالات |
| `report.payments` | PAYMENTS | RÈGLEMENTS | المدفوعات |
| `report.grand_total` | Perpetual grand total | Grand total perpétuel | الإجمالي الدائم |
| `report.items_sold` | ITEMS SOLD | ARTICLES VENDUS | الأصناف المباعة |
| `report.items_before_discount` | Amounts before bill discounts | Montants avant remises sur note | المبالغ قبل خصومات الفاتورة |
| `report.subtotal` | Subtotal | Sous-total | المجموع الفرعي |
| `report.other_category` | Other | Autres | أخرى |
| `report.tips_by_server` | TIPS BY SERVER | POURBOIRES PAR SERVEUR | الإكراميات حسب النادل |
| `report.tips_total` | Total tips | Total pourboires | مجموع الإكراميات |
| `report.unknown_server` | Unknown | Inconnu | غير معروف |
| `report.signature` | Signature | Signature | التوقيع |

- [ ] **Step 4: Run tests**

Run: `dotnet format RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(impression): données et tickets des rapports X/Z (articles par famille, pourboires par serveur)"
```

---

### Task 5: Impression des rapports — file, clôture Z, routes

**Files:**
- Modify: `src/RestaurantPos.Domain/Entities/PrintJob.cs` (`PrintJobKind.Report = 3`)
- Modify: `src/RestaurantPos.Infrastructure/Printing/PrintDispatcher.cs`
- Modify: `src/RestaurantPos.Api/Endpoints/FiscalEndpoints.cs`
- Modify: `src/RestaurantPos.Api/Program.cs` (DI `ReportPrintDataService` scoped)
- Test: `tests/RestaurantPos.Infrastructure.Tests/PrintDispatcherTests.cs`, `tests/RestaurantPos.Api.Tests/ReportPrintApiTests.cs`

**Interfaces:**
- Consumes: Task 4 (`ReportPrintDataService`, `XReport`, `ZClosure`, `DailyFiscalClosureDto.PeriodStartUtc`), Task 3 (règle Z).
- Produces: `PrintDispatcher(AppDbContext, PrintQueue, IRestaurantSettingsService, ReportPrintDataService, TimeProvider, ILogger<PrintDispatcher>)` ; `Task<bool> QueueXReportAsync(FiscalSummaryDto summary, CancellationToken ct = default)` ; `Task<bool> QueueZClosureAsync(DailyFiscalClosureDto closure, CancellationToken ct = default)` ; `POST /api/fiscal/x-report/print?terminalId=` → `{ printQueued }` ; `POST /api/fiscal/latest-closure/print?terminalId=` → `{ printQueued }` / 404 ; réponse de `/z-closure` + `printQueued`.

- [ ] **Step 1: Write the failing tests**

`PrintDispatcherTests` : mettre à jour `Create()` (`new PrintDispatcher(db, queue, new RestaurantSettingsService(db), new ReportPrintDataService(db), TimeProvider.System, NullLogger<PrintDispatcher>.Instance)`) et ajouter :

```csharp
[Fact]
public async Task ZClosure_QueuesReportOnReceiptPrinter_InReceiptLanguage_NoDrawer()
{
    var (d, db) = Create();
    var caisse = Printer(db, "Caisse", ["RECEIPT"], drawer: true);
    await new RestaurantSettingsService(db).UpdateAsync(new UpdateRestaurantSettingsRequest("en", "fr"));
    var closure = new DailyFiscalClosureDto(Guid.NewGuid(), "POS_MAIN_TERM", 3, 0, 0, 0, new Dictionary<decimal, long>(), new Dictionary<PaymentMethod, long>(), 0, "sig", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(-8), "Gérant");

    (await d.QueueZClosureAsync(closure)).Should().BeTrue();

    var job = db.PrintJobs.Single();
    job.PrinterId.Should().Be(caisse.Id);
    job.Kind.Should().Be(PrintJobKind.Report);
    job.OpenCashDrawer.Should().BeFalse();
    var doc = TicketDocumentJson.Deserialize(job.DocumentJson);
    doc.Language.Should().Be("en");
    ((TicketText)doc.Lines[0]).Text.Should().Be("Z CLOSURE #3");
}

[Fact]
public async Task XReport_NoReceiptPrinter_ReturnsFalse_NoJob()
{
    var (d, db) = Create();
    var summary = new FiscalSummaryDto("POS_MAIN_TERM", DateTimeOffset.MinValue, DateTimeOffset.UtcNow, 0, 0, 0, new Dictionary<decimal, long>(), new Dictionary<PaymentMethod, long>(), 0);
    (await d.QueueXReportAsync(summary)).Should().BeFalse();
    db.PrintJobs.Should().BeEmpty();
}
```

`tests/RestaurantPos.Api.Tests/ReportPrintApiTests.cs` (fabrique neuve par test ; imprimante `RECEIPT` garantie comme `PrintTriggerTests.EnsurePrinters`) — écrire en entier :

| Test | Mise en place | Attendu |
|---|---|---|
| `ZClosure_Success_QueuesReport_AndReportsPrintQueued` | solder les commandes ouvertes du seed s'il y en a (règle Task 3) ; manager `1234` ; `POST /api/fiscal/z-closure` | 200, `printQueued` vrai ; un job `Report` |
| `XReportPrint_QueuesReport` | `POST /api/fiscal/x-report/print?terminalId=POS_MAIN_TERM` | 200 `{ printQueued: true }` ; un job `Report` |
| `LatestClosurePrint_WithoutClosure_Returns404_WithClosure_Queues` | d'abord sans clôture → route ; puis une Z, puis la route | 404 puis 200 `{ printQueued: true }` |
| `ReportPrint_WaiterForbidden` | serveur `2468` | les deux routes → 403 |

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test RestaurantPos.slnx --filter "FullyQualifiedName~PrintDispatcherTests|FullyQualifiedName~ReportPrintApiTests"`
Expected: échec de compilation.

- [ ] **Step 3: Implement**

`PrintJob.cs` : `public enum PrintJobKind { PickupVoucher = 0, Receipt = 1, KitchenTicket = 2, Report = 3 }`.

`PrintDispatcher` : ajouter le paramètre et le champ `ReportPrintDataService reports` (après `settings`), puis :

```csharp
public Task<bool> QueueXReportAsync(FiscalSummaryDto summary, CancellationToken ct = default)
{
    ArgumentNullException.ThrowIfNull(summary);
    return QueueReportAsync(summary.TerminalId, async language =>
    {
        var data = await _reports.BuildAsync(summary.TerminalId, summary.PeriodStartUtc, summary.PeriodEndUtc, ct).ConfigureAwait(false);
        return TicketDocumentBuilder.XReport(summary, data, language, _time.GetUtcNow());
    }, ct);
}

public Task<bool> QueueZClosureAsync(DailyFiscalClosureDto closure, CancellationToken ct = default)
{
    ArgumentNullException.ThrowIfNull(closure);
    return QueueReportAsync(closure.TerminalId, async language =>
    {
        var data = await _reports.BuildAsync(closure.TerminalId, closure.PeriodStartUtc, closure.ClosedAtUtc, ct).ConfigureAwait(false);
        return TicketDocumentBuilder.ZClosure(closure, data, language);
    }, ct);
}

private async Task<bool> QueueReportAsync(string terminalId, Func<string, Task<TicketDocument>> build, CancellationToken ct)
{
    try
    {
        var printer = await ReceiptPrinterAsync(terminalId, ct).ConfigureAwait(false);
        if (printer is null) return false;
        var language = (await _settings.GetAsync(ct).ConfigureAwait(false)).ReceiptLanguage;
        await _queue.EnqueueAsync(printer.Id, PrintJobKind.Report, await build(language).ConfigureAwait(false), false, ct).ConfigureAwait(false);
        return true;
    }
    catch (Exception ex)
    {
        PrintLog.QueueFailed(_logger, ex);
        return false;
    }
}
```

`Program.cs` : `builder.Services.AddScoped<ReportPrintDataService>();` (avant `PrintDispatcher`).

`FiscalEndpoints` :
- `/z-closure` : injecter `PrintDispatcher printing` ; après `ExecuteDailyZClosureAsync` : `var printQueued = await printing.QueueZClosureAsync(closure);` et ajouter `PrintQueued = printQueued` à l'objet de réponse.
- Nouvelles routes (groupe `/api/fiscal`, donc manager/admin) :

```csharp
group.MapPost("/x-report/print", async (string? terminalId, INF525FiscalAuditService fiscal, PrintDispatcher printing) =>
{
    var term = string.IsNullOrWhiteSpace(terminalId) ? "POS_MAIN_TERM" : terminalId;
    var summary = await fiscal.GenerateXReportAsync(term);
    return Results.Ok(new { PrintQueued = await printing.QueueXReportAsync(summary) });
});

group.MapPost("/latest-closure/print", async (string? terminalId, INF525FiscalAuditService fiscal, PrintDispatcher printing) =>
{
    var term = string.IsNullOrWhiteSpace(terminalId) ? "POS_MAIN_TERM" : terminalId;
    var closure = await fiscal.GetLatestZClosureAsync(term);
    return closure is null
        ? Results.NotFound(new { Message = Texts.T("errors.no_closure_found") })
        : Results.Ok(new { PrintQueued = await printing.QueueZClosureAsync(closure) });
});
```

- [ ] **Step 4: Run tests**

Run: `dotnet format RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(impression): impression des rapports X et Z (clôture automatique, réimpression)"
```

---

### Task 6: Client web

**Files:**
- Modify: `src/RestaurantPos.Api/wwwroot/index.html` (création d'imprimante, `editPrinterModal`, écran fiscal), `app.js` (création imprimante ~l.3569, édition l.2933 et l.4257, `printerToUpdatePayload` l.4128, liste des jobs, paiement à table ~l.1927, clôture Z l.3679-3708, zone du rapport X / dernière clôture), `i18n/{en,fr,ar}.json`
- Test: `tests/RestaurantPos.Web.E2ETests/tests/printing-reports.spec.ts`

**Interfaces:**
- Consumes: Tasks 1, 2, 3, 5 (`textMode`, `tipAmount`, 409 `open_orders`, `/x-report/print`, `/latest-closure/print`, `printQueued`, `kind` = `Report`).

- [ ] **Step 1: Write the failing Playwright spec**

`printing-reports.spec.ts` (helpers de connexion/navigation copiés des specs existants ; la clôture Z, qui modifierait l'état partagé, est simulée par `page.route`) :

```ts
import { test, expect } from '@playwright/test';

test('imprimante : case mode texte enregistrée et conservée', async ({ page }) => {
  await loginAs(page, '1234');
  await openAdminSection(page, 'printers');
  await createPrinter(page, { name: 'Texte E2E', ip: '10.0.0.77', textMode: true });   // coche #newPrinterTextMode
  const printers = await (await page.request.get('/api/printers')).json();
  expect(printers.find((p: any) => p.name === 'Texte E2E').textMode).toBe(true);
  await openPrinterEditor(page, 'Texte E2E');
  await expect(page.locator('#editPrinterTextMode')).toBeChecked();
});

test('fiscal : Imprimer le rapport X appelle la route d\'impression', async ({ page }) => {
  await loginAs(page, '1234');
  await openFiscal(page);
  const req = page.waitForRequest(r => r.url().includes('/api/fiscal/x-report/print') && r.method() === 'POST');
  await page.locator('#btnPrintXReport').click();
  await req;
});

test('fiscal : Z refusée affiche les commandes à encaisser', async ({ page }) => {
  await page.route('**/api/fiscal/z-closure', route => route.fulfill({
    status: 409, contentType: 'application/json',
    body: JSON.stringify({ code: 'open_orders', message: 'Clôture Z impossible : encaissez d\'abord 1 commande(s) en cours : T5 (12.50)', openOrders: [{ tableNumber: 'T5', remainingTtc: 12.5 }] })
  }));
  await loginAs(page, '1234');
  await openFiscal(page);
  await triggerZClosure(page);   // clic #btnExecuteZ (+ confirmation si l'écran en demande une)
  await expect(page.locator('.toast').last()).toContainText('T5 (12.50)');
});

test('fiscal : Z réussie sans imprimante → avertissement', async ({ page }) => {
  await page.route('**/api/fiscal/z-closure', route => route.fulfill({ status: 200, contentType: 'application/json',
    body: JSON.stringify({ closureSequence: 9, totalSalesTtc: 0, totalSalesHt: 0, receiptCount: 0, vatBreakdown: {}, paymentTotals: {}, perpetualGrandTotal: 0, signatureHash: 'x', closedAtUtc: new Date().toISOString(), printQueued: false }) }));
  await loginAs(page, '1234');
  await openFiscal(page);
  await triggerZClosure(page);
  await expect(page.locator('.toast').filter({ hasText: /imprimante/i })).toBeVisible();
});

test('paiement à table : le pourboire est envoyé à part et inclus dans l\'encaissement', async ({ page }) => {
  await loginAs(page, '2468');
  const linePrice = await openTableWithOneLine(page, 'T12');   // renvoie le prix TTC de la ligne ajoutée
  await openPaymentModal(page);
  await page.locator('[data-tip-percent="10"]').click();
  const req = page.waitForRequest(r => r.url().endsWith('/api/checkout/pay'));
  await confirmPayment(page);
  const body = (await req).postDataJSON();
  expect(body.tipAmount).toBeCloseTo(linePrice * 0.1, 2);
  expect(body.tenders[0].amount).toBeCloseTo(linePrice + body.tipAmount, 2);
});
```

Écrire les helpers (`createPrinter`, `openPrinterEditor`, `openFiscal`, `triggerZClosure`, `openTableWithOneLine`, `openPaymentModal`, `confirmPayment`) avec les identifiants réels d'`index.html` (les lire avant).

Run: `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npx playwright test tests/printing-reports.spec.ts --project='iPad Pro 11'`
Expected: FAIL.

- [ ] **Step 2: Implement**

Imprimantes :
- Création et édition : `<label><input type="checkbox" id="newPrinterTextMode"> <span data-i18n="admin.printer_text_mode">Mode texte (fr/en, plus rapide)</span></label>` et `#editPrinterTextMode` dans `editPrinterModal`.
- Création (~l.3569) : `textMode: document.getElementById('newPrinterTextMode')?.checked === true`.
- Édition : `document.getElementById('editPrinterTextMode').checked = !!pr.textMode;` (l.2933) ; corps du `PUT` (l.4257) : `textMode: document.getElementById('editPrinterTextMode').checked`.
- `printerToUpdatePayload(pr)` : ajouter `textMode: !!pr.textMode`.
- Ligne de la liste : suffixe `· ${t('admin.printer_text_mode_badge')}` si `pr.textMode`.
- Liste des jobs : ajouter la clé `admin.print_job_kind_report` (le libellé est construit depuis `kind` en minuscules).

Paiement à table (~l.1927) : dans le `payload`, `tipAmount: isSplit ? 0 : Math.round(getTipAmount() * 100) / 100` (`total` inclut déjà le pourboire hors partage). Masquer le conteneur des boutons de pourboire (`elements.tipPills[0]?.parentElement`) quand `state.splitPlan` est actif : dans `showCurrentSplitPart()` et à l'ouverture de la modale (`style.display = state.splitPlan ? 'none' : ''`).

Écran fiscal :
- Boutons `<button id="btnPrintXReport" data-i18n="fiscal.print_x_report">Imprimer le rapport X</button>` près de l'aperçu X et `<button id="btnReprintZ" data-i18n="fiscal.reprint_z">Réimprimer la dernière clôture</button>` près de la dernière clôture.

```js
async function postFiscalPrint(path) {
    await ensureAuthToken();
    const headers = state.token ? { 'Authorization': `Bearer ${state.token}` } : {};
    const res = await fetch(`${path}?terminalId=POS_MAIN_TERM`, { method: 'POST', headers });
    if (!res.ok) {
        showToast(await readApiError(res, t('fiscal.print_error')), 'error');
        return;
    }
    const data = await res.json();
    showToast(data.printQueued ? t('fiscal.print_queued') : t('payment.print_not_queued'), data.printQueued ? 'success' : 'warning');
}
```

Brancher `btnPrintXReport` → `postFiscalPrint('/api/fiscal/x-report/print')` et `btnReprintZ` → `postFiscalPrint('/api/fiscal/latest-closure/print')` (une fois, à côté du gestionnaire de `btnExecuteZ`).
- Clôture Z (l.3698) : après succès, si `closure.printQueued === false`, `showToast(t('payment.print_not_queued'), 'warning')`. Le refus 409 affiche déjà `err.message` (qui contient la liste) : aucun changement nécessaire.

Chaînes (`en` / `fr` / `ar`) :

| Clé | en | fr | ar |
|---|---|---|---|
| `admin.printer_text_mode` | Text mode (fr/en, faster) | Mode texte (fr/en, plus rapide) | وضع النص (فرنسي/إنجليزي، أسرع) |
| `admin.printer_text_mode_badge` | Text | Texte | نص |
| `admin.print_job_kind_report` | Report | Rapport | تقرير |
| `fiscal.print_x_report` | Print X report | Imprimer le rapport X | طباعة تقرير X |
| `fiscal.reprint_z` | Reprint last closure | Réimprimer la dernière clôture | إعادة طباعة آخر إغلاق |
| `fiscal.print_queued` | Sent to the printer | Envoyé à l'imprimante | أُرسل إلى الطابعة |
| `fiscal.print_error` | Printing refused | Impression refusée | رُفضت الطباعة |

- [ ] **Step 3: Run tests**

Run: `node scripts/i18n-check.mjs` puis `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npm test`
Expected: PASS, specs existants compris.

- [ ] **Step 4: Commit**

```bash
git add src/RestaurantPos.Api/wwwroot tests/RestaurantPos.Web.E2ETests
git commit -m "feat(impression): web — mode texte, impression des rapports X/Z, pourboire à table"
```

---

### Task 7: iPad — PosKit et écrans

**Files:**
- Modify: PosKit `Models/OperationsModels.swift` (`Printer.textMode`, `FiscalReport.printQueued`), `Models/OrderModels.swift` (`PaymentRequest.tipAmount`), `Networking/PosAPI.swift` + `HTTPPosAPI.swift` (`printXReport`, `reprintLatestClosure`, `savePrinter` avec `textMode`), `Testing/InMemoryPosAPI.swift`, `Stores/OperationsStores.swift` (store fiscal), `Stores/TicketStore.swift` (`pay(... tip:)`), `Resources/Localizable.xcstrings`
- Modify: fixtures `printers.json`, `z_closure.json` (recapturer depuis l'API)
- Modify: app `Features/Admin/AdminScreen.swift` (éditeur d'imprimante ~l.593, libellé du type de job), `Features/Fiscal/FiscalScreen.swift`, `Features/Payment/PaymentSheet.swift:257-262`, `Resources/Localizable.xcstrings`
- Test: `ContractDecodingTests.swift`, `NetworkingTests.swift`, `StoreTests.swift`, `ios/RestaurantPOSUITests/OperationsUITests.swift`, `PrintingUITests.swift`

**Interfaces:**
- Consumes: Tasks 1, 2, 3, 5.
- Produces (Swift) : `Printer.textMode: Bool` (décodage `?? false`) ; `FiscalReport.printQueued: Bool?` ; `PaymentRequest.tipAmount: Money` (défaut `.zero`, encodé comme `TenderInput.amount`) ; `PosAPI.printXReport(terminalId:) async throws -> Bool`, `PosAPI.reprintLatestClosure(terminalId:) async throws -> Bool` ; `FiscalStore.printX() async`, `FiscalStore.reprintZ() async` ; `TicketStore.pay(method:amount:tendered:printReceipt:tip:)`.

- [ ] **Step 1: Capturer les fixtures**

API lancée : `GET /api/printers` → `printers.json` (au moins une imprimante `textMode: true`) ; une Z réelle après avoir soldé les commandes → `z_closure.json` (contient `printQueued`).

- [ ] **Step 2: Write the failing tests**

```swift
// ContractDecodingTests
@Test func decodesPrinterTextMode() throws {
    let printers = try decodeFixture([Printer].self, "printers")
    #expect(printers.contains { $0.textMode })
}

@Test func printerWithoutTextModeDecodesFalse() throws {
    let json = #"{"id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","name":"P","ipAddress":"1.2.3.4","port":9100,"paperWidthMm":80,"openCashDrawerOnReceipt":false,"assignedStationIds":[],"isActive":true}"#
    #expect(try JSONDecoder().decode(Printer.self, from: Data(json.utf8)).textMode == false)
}

@Test func decodesZClosurePrintQueued() throws {
    #expect(try decodeFixture(FiscalReport.self, "z_closure").printQueued != nil)
}

// NetworkingTests
@Test func paymentRequestEncodesTip() throws {
    let req = PaymentRequest(orderId: nil, tableNumber: "T5", operatorId: nil, terminalId: "T01", tenders: [], tipAmount: Money(cents: 250))
    let json = try #require(try JSONSerialization.jsonObject(with: JSONEncoder().encode(req)) as? [String: Any])
    #expect((json["tipAmount"] as? NSNumber)?.decimalValue == Decimal(string: "2.5"))
}
```

(Remplacer `decodeFixture` par le helper réel du fichier.)

`StoreTests` (sur `InMemoryPosAPI`, mise en place des tests fiscaux et de paiement existants) — écrire en entier :

| Test | Action | Attendu |
|---|---|---|
| `fiscalPrintXCallsApi` | `await store.printX()` | `api.calls` contient `printXReport` ; notification de succès |
| `zClosureRefusedWithOpenOrdersShowsMessage` | une table ouverte avec une ligne ; `await store.executeZ()` | renvoie `false` ; le message notifié contient le numéro de table |
| `tablePaymentSendsTip` | `await ticket.pay(method: .card, amount: total + 2.00, tendered: total + 2.00, tip: Money(cents: 200))` | `api.lastPaymentRequest?.tipAmount == Money(cents: 200)` |

UI : `OperationsUITests.testFiscalXAndZReports` — si le jeu de données `-UITestMode` contient une table ouverte avec articles, l'encaisser avant `fiscal.executeZ` ; ajouter `XCTAssertTrue(app.buttons["fiscal.printX"].exists)` avant la Z. `PrintingUITests.testPrinterEditorHasTextModeToggle` : ouvrir l'éditeur d'imprimante, `XCTAssertTrue(app.switches["printer.textMode"].waitForExistence(timeout: 5))`.

Run: `cd ios && ./scripts/test.sh unit`
Expected: FAIL (compilation).

- [ ] **Step 3: Implement**

Modèles : `Printer.textMode: Bool` (init `textMode: Bool = false`, `decodeIfPresent ?? false`) ; `HTTPPosAPI.savePrinter` envoie `textMode` à la création et à la modification ; `FiscalReport.printQueued: Bool?` ; `PaymentRequest.tipAmount: Money` (init `tipAmount: Money = .zero`, encodé comme `TenderInput.amount`).

API :

```swift
// PosAPI
func printXReport(terminalId: String) async throws -> Bool
func reprintLatestClosure(terminalId: String) async throws -> Bool

// HTTPPosAPI
private struct PrintQueuedResponse: Decodable { let printQueued: Bool }

public func printXReport(terminalId: String) async throws -> Bool {
    try await call("POST", "fiscal/x-report/print", query: ["terminalId": terminalId], as: PrintQueuedResponse.self).printQueued
}

public func reprintLatestClosure(terminalId: String) async throws -> Bool {
    try await call("POST", "fiscal/latest-closure/print", query: ["terminalId": terminalId], as: PrintQueuedResponse.self).printQueued
}
```

`InMemoryPosAPI` : `textMode` stocké sur les imprimantes ; `printXReport` / `reprintLatestClosure` enregistrent l'appel et renvoient `true` s'il existe une imprimante `RECEIPT` ; `zClosure` lève `APIError.server(status: 409, message:)` avec la liste des tables si une commande avec articles n'est pas soldée (même règle que le serveur) ; `pay` enregistre `lastPaymentRequest` et ajoute `tipAmount` au pourboire de la commande.

`FiscalStore` (`OperationsStores.swift`) :

```swift
public func printX() async { await sendPrint { try await self.api.printXReport(terminalId: self.settings.terminalId) } }
public func reprintZ() async { await sendPrint { try await self.api.reprintLatestClosure(terminalId: self.settings.terminalId) } }

private func sendPrint(_ send: () async throws -> Bool) async {
    do {
        if try await send() { notifier.success(L10n.string("fiscal.print_queued")) }
        else { notifier.error(L10n.string("payment.print_not_queued")) }
    } catch {
        notifier.error(error)
    }
}
```

(Utiliser la méthode d'avertissement du `Notifier` si elle existe, à la place de `error` pour `print_not_queued`.) Dans `executeZ`, après succès : `if closure.printQueued == false { notifier.error(L10n.string("payment.print_not_queued")) }` (ou l'avertissement). Le 409 passe par `notifier.error(error)`, qui affiche le message serveur contenant la liste.

`TicketStore.pay(method:amount:tendered:printReceipt:tip: Money = .zero)` : `tipAmount: tip` dans la requête. `PaymentSheet.pay()` (l.257-262), branche table : `tip: plan.tipAmount` (`plan.tipAmount` vaut déjà `.zero` en partage ; `amount` = `plan.amountToCollect` inclut déjà le pourboire hors partage).

Écrans :
- `AdminScreen` éditeur d'imprimante : `Toggle("admin.printer_text_mode", isOn: $printer.textMode).accessibilityIdentifier("printer.textMode")` sous le tiroir ; badge « Texte » dans la ligne si `printer.textMode` ; libellé du type de job `Report` via `admin.print_job_kind_report`.
- `FiscalScreen` : bouton « Imprimer le rapport X » (`fiscal.printX`) quand l'aperçu X est affiché (`!isSealed`), « Réimprimer la dernière clôture » (`fiscal.reprintZ`) quand `isSealed`.

Chaînes (PosKit et app, en/fr/ar) : celles de la Task 6 utilisées ici (`admin.printer_text_mode`, `admin.printer_text_mode_badge`, `admin.print_job_kind_report`, `fiscal.print_x_report`, `fiscal.reprint_z`, `fiscal.print_queued`).

- [ ] **Step 4: Run tests**

Run: `cd ios && xcodegen generate && ./scripts/test.sh unit && ./scripts/test.sh ui` puis `POS_API_URL=http://localhost:5080 ./scripts/test.sh contract`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ios
git commit -m "feat(impression): iPad — mode texte, impression des rapports X/Z, pourboire à table"
```

---

### Task 8: Documentation et vérification finale

**Files:**
- Modify: `docs/impression.md`

- [ ] **Step 1: Compléter `docs/impression.md`**

Ajouter les sections :
1. **Mode texte** : case par imprimante, fr/en seulement (l'arabe reste en image), page de code PC858 ; si les accents sortent faux, décocher la case (vérifier avec l'impression de test).
2. **Rapports imprimés** : rapport X (bouton), clôture Z (automatique, bouton « Réimprimer »), contenu (totaux, TVA, règlements, articles par famille avec sous-totaux, pourboires par serveur), imprimante utilisée, montants articles avant remises sur note.
3. **Règles de clôture** : tout encaisser avant la Z (message listant tables et paniers), aucune annulation d'un ticket d'une période clôturée (y compris un ticket d'iPad couvert par la Z du terminal principal).
4. **Pourboire à table** : saisi sur le paiement qui solde la note (pas sur une part de partage), attribué au serveur de la table ; au comptoir, à l'opérateur qui encaisse.
5. **Essai manuel** (checklist) :
   - [ ] impression de test en mode texte, `fr` : accents et « € » corrects ; `ar` : sortie en image ;
   - [ ] même ticket de caisse de 15 lignes en image puis en texte : noter les deux durées et le modèle d'imprimante ;
   - [ ] rapport X en 58 mm et en 80 mm : colonnes alignées, signature et libellés longs repliés sans perte ;
   - [ ] clôture Z avec une table ouverte : refus et liste ; après encaissement : Z imprimée automatiquement.

- [ ] **Step 2: Vérification finale**

Run: `dotnet format RestaurantPos.slnx --verify-no-changes && dotnet build RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Run: `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npm test`
Run: `cd ios && ./scripts/test.sh unit && ./scripts/test.sh ui`
Expected: tout PASS.

- [ ] **Step 3: Commit**

```bash
git add docs/impression.md
git commit -m "docs(impression): mode texte, rapports imprimés, règles de clôture, pourboire à table"
```
