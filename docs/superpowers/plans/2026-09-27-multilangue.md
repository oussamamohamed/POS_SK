# Multilangue (EN / FR / AR) — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rendre le serveur, le client web et l'app iPad utilisables en anglais, français et arabe (RTL), avec une langue des tickets réglée par restaurant.

**Architecture:** Clés sémantiques uniques (`zone.nom`) sur les trois couches. Serveur : `.resx` (neutre = anglais) lus par un helper statique `Texts`, langue choisie par `Accept-Language` (UI culture seulement). Web : dictionnaires JSON + `t()`. iOS : String Catalogs. Nouvelle entité `RestaurantSettings` (`ReceiptLanguage`) et modèle de ticket structuré `TicketDocument` (pas d'impression réelle, c'est le sous-projet C).

**Tech Stack:** .NET 9 (ASP.NET Core Minimal API, EF Core SQLite/InMemory, xUnit + FluentAssertions), JS vanilla + Playwright, Swift 6 / SwiftUI / Swift Testing / XCUITest, XcodeGen.

**Spec:** `docs/superpowers/specs/2026-09-27-multilangue-design.md`

## Global Constraints

- Langues : `en` (défaut et repli), `fr`, `ar`. Toute autre langue → `en`.
- Clés : `<zone>.<nom>` en `snake_case`. Zones : `login`, `floor`, `order`, `payment`, `kitchen`, `fiscal`, `admin`, `receipt`, `errors`, `messages`, `common`.
- Placeholders nommés `{nom}`. Placeholder sans valeur → laissé tel quel, jamais d'exception.
- Clé absente dans la langue → anglais ; absente en anglais → la clé elle-même est affichée.
- Chiffres occidentaux (0-9) partout, y compris en arabe (`ar-u-nu-latn` web, `numberingSystem=latn` iOS, `CurrentCulture` serveur figé à `en`).
- Données saisies (produits, catégories, tables, opérateurs, en-tête restaurant) jamais traduites.
- Chaîne NF525 : aucun changement de format ; le hash ne dépend d'aucune langue.
- Contrat JSON des erreurs inchangé (`Message` / `message` / `ErrorMessage` restent des textes lisibles, déjà traduits).
- `ReceiptLanguage` : `fr` si la base contient déjà des commandes, sinon `en`. Valeurs acceptées `en`, `fr`, `ar` ; autre → 400.
- Aucun envoi à une imprimante dans ce sous-projet.
- `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` : `dotnet format RestaurantPos.slnx` avant chaque build en cas de doute.
- Schéma : toute nouvelle table a son `CREATE TABLE IF NOT EXISTS` dans `Program.cs` (bloc non-`Testing`).
- `ios/RestaurantPOS.xcodeproj` est généré : modifier `ios/project.yml`, jamais le `.xcodeproj`.
- Les tests existants (Playwright, XCUITest) restent en français : `locale: 'fr-FR'`, `-AppleLanguages (fr)`.
- Messages de commit en français, terminés par `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Libellé laissé en dur** après extraction (web ou iOS) : l'utilisateur arabe voit du français au milieu de l'écran. → Le script `i18n-check.mjs` (Task 5) signale aussi les chaînes françaises restantes dans `app.js`/`index.html` (heuristique accents + mots FR) ; Task 7/8 le font passer à zéro.
2. **Culture serveur qui change le formatage** (virgule décimale, chiffres arabes dans un montant ou un hash) : la localisation ne doit changer que `CurrentUICulture`. → Test Task 1 `AcceptLanguage_Ar_DoesNotChangeCurrentCulture` et test Task 4 hash NF525 identique sous `fr`/`ar`.
3. **Pavé numérique / montant inversé en RTL** (1-2-3 affiché 3-2-1, « 12,50 » devenant « 50,12 ») → assertions Playwright Task 9 et XCUITest Task 11 sur l'ordre des touches.
4. **Base existante sans ligne de réglages** : doit démarrer en `fr`, pas en `en`, sinon les tickets d'un client français basculent en anglais. → Tests Task 3 `Get_WithExistingOrders_InitializesFrench` / `Get_OnEmptyDatabase_InitializesEnglish`.
5. **Accept-Language exotique** (`de`, `fr-CA`, `ar-MA`, en-tête absent, liste pondérée `ar;q=0.9,en;q=0.8`) → Test Task 1 paramétré.

---

## File Structure

**Serveur**
- Create `src/RestaurantPos.Infrastructure/Localization/SharedResource.cs` — marqueur de ressources.
- Create `src/RestaurantPos.Infrastructure/Localization/SharedResource.resx` (+ `.fr.resx`, `.ar.resx`) — textes serveur.
- Create `src/RestaurantPos.Infrastructure/Localization/Texts.cs` — lecture des textes, cultures supportées.
- Create `src/RestaurantPos.Domain/Entities/RestaurantSettings.cs` — ligne unique de réglages.
- Create `src/RestaurantPos.Application/Common/Interfaces/IRestaurantSettingsService.cs` + DTOs dans le même fichier.
- Create `src/RestaurantPos.Infrastructure/Services/RestaurantSettingsService.cs`.
- Create `src/RestaurantPos.Api/Endpoints/SettingsEndpoints.cs`.
- Create `src/RestaurantPos.Infrastructure/Printing/TicketDocument.cs` — modèle structuré.
- Create `src/RestaurantPos.Infrastructure/Printing/TicketDocumentBuilder.cs` — remplace `TakeawayTicketFormatter.cs` (supprimé).
- Modify `src/RestaurantPos.Api/Program.cs`, `Persistence/AppDbContext.cs`, tous les endpoints et services listés Task 2.

**Web**
- Create `src/RestaurantPos.Api/wwwroot/i18n.js` — runtime i18n (chargé avant `app.js`).
- Create `src/RestaurantPos.Api/wwwroot/i18n/en.json`, `fr.json`, `ar.json`.
- Create `scripts/i18n-check.mjs`.
- Create `tests/RestaurantPos.Web.E2ETests/tests/i18n.spec.ts`.
- Modify `wwwroot/index.html`, `wwwroot/app.js`, `wwwroot/styles.css`, `tests/RestaurantPos.Web.E2ETests/playwright.config.ts`.

**iOS**
- Create `ios/Packages/PosKit/Sources/PosKit/Resources/Localizable.xcstrings`.
- Create `ios/RestaurantPOS/Resources/Localizable.xcstrings`.
- Create `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/settings.json`, `LocalizationTests.swift`.
- Modify `ios/project.yml`, `Package.swift`, `HTTPPosAPI.swift`, `PosAPI.swift`, `InMemoryPosAPI.swift`, `Money.swift`, `AdminStores.swift`, `AppModel.swift`, `OperationsModels.swift`, vues `RestaurantPOS/**`, `RestaurantPOSUITests/PosUITestCase.swift`.

---

### Task 1: Infrastructure de localisation serveur

**Files:**
- Create: `src/RestaurantPos.Infrastructure/Localization/SharedResource.cs`
- Create: `src/RestaurantPos.Infrastructure/Localization/SharedResource.resx`, `SharedResource.fr.resx`, `SharedResource.ar.resx`
- Create: `src/RestaurantPos.Infrastructure/Localization/Texts.cs`
- Modify: `src/RestaurantPos.Api/Program.cs` (services ~l.60, pipeline ~l.290)
- Modify: `src/RestaurantPos.Api/Endpoints/DeviceEndpoints.cs:62`
- Test: `tests/RestaurantPos.Api.Tests/LocalizationTests.cs`, `tests/RestaurantPos.Infrastructure.Tests/TextsTests.cs`
- Modify test: `tests/RestaurantPos.Api.Tests/DeviceEndpointsTests.cs:131`

**Interfaces:**
- Produces:
  - `Texts.SupportedLanguages : string[]` = `["en", "fr", "ar"]`
  - `Texts.T(string key, params (string Name, object? Value)[] args) : string` — culture = `CultureInfo.CurrentUICulture`.
  - `Texts.Get(CultureInfo culture, string key, params (string Name, object? Value)[] args) : string`
  - `Texts.Keys(CultureInfo culture) : IReadOnlySet<string>` — clés définies exactement dans cette culture (sans repli), pour les tests de complétude.

- [ ] **Step 1: Test unitaire de `Texts` (échoue)**

`tests/RestaurantPos.Infrastructure.Tests/TextsTests.cs` :

```csharp
using System.Globalization;
using FluentAssertions;
using RestaurantPos.Infrastructure.Localization;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class TextsTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr");
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("ar");
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de");

    [Fact]
    public void Get_ReturnsValueForEachLanguage()
    {
        Texts.Get(En, "errors.pairing_code_invalid").Should().Be("Invalid or expired code");
        Texts.Get(Fr, "errors.pairing_code_invalid").Should().Be("Code invalide ou expiré");
        Texts.Get(Ar, "errors.pairing_code_invalid").Should().Be("رمز غير صالح أو منتهي الصلاحية");
    }

    [Fact]
    public void Get_UnsupportedCulture_FallsBackToEnglish() =>
        Texts.Get(De, "errors.pairing_code_invalid").Should().Be("Invalid or expired code");

    [Fact]
    public void Get_UnknownKey_ReturnsKey() =>
        Texts.Get(Fr, "errors.does_not_exist").Should().Be("errors.does_not_exist");

    [Fact]
    public void Get_ReplacesNamedPlaceholders_WithInvariantFormatting()
    {
        Texts.Get(Ar, "errors.rate_limited_seconds", ("seconds", 12.0))
            .Should().Contain("12").And.NotContain("{seconds}");
    }

    [Fact]
    public void Get_MissingArgument_LeavesPlaceholder() =>
        Texts.Get(En, "errors.rate_limited_seconds").Should().Contain("{seconds}");

    [Fact]
    public void EveryEnglishKey_ExistsInFrenchAndArabic_AndIsNotEmpty()
    {
        var en = Texts.Keys(En);
        en.Should().NotBeEmpty();
        foreach (var culture in new[] { Fr, Ar })
        {
            Texts.Keys(culture).Should().BeEquivalentTo(en, $"culture {culture.Name}");
            foreach (var key in en) Texts.Get(culture, key).Should().NotBeNullOrWhiteSpace();
        }
    }
}
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "FullyQualifiedName~TextsTests"`
Expected: FAIL à la compilation (`Texts` introuvable).

- [ ] **Step 3: Implémenter `SharedResource`, `Texts` et les trois `.resx`**

`SharedResource.cs` :

```csharp
namespace RestaurantPos.Infrastructure.Localization;

/// <summary>Marqueur des ressources partagées. Le .resx neutre est en anglais (langue de repli).</summary>
public sealed class SharedResource
{
}
```

`Texts.cs` :

```csharp
using System.Collections;
using System.Globalization;
using System.Resources;

namespace RestaurantPos.Infrastructure.Localization;

/// <summary>Textes serveur par clé sémantique (`zone.nom`). Repli : anglais, puis la clé elle-même.</summary>
public static class Texts
{
    public static readonly string[] SupportedLanguages = ["en", "fr", "ar"];

    private static readonly ResourceManager Resources =
        new("RestaurantPos.Infrastructure.Localization.SharedResource", typeof(SharedResource).Assembly);

    public static string T(string key, params (string Name, object? Value)[] args) =>
        Get(CultureInfo.CurrentUICulture, key, args);

    public static string Get(CultureInfo culture, string key, params (string Name, object? Value)[] args)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var text = Resources.GetString(key, culture) ?? key;
        foreach (var (name, value) in args)
        {
            text = text.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
        return text;
    }

    public static IReadOnlySet<string> Keys(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        // L'anglais est le .resx neutre : pas d'assembly satellite « en ».
        var target = culture.TwoLetterISOLanguageName == "en" ? CultureInfo.InvariantCulture : culture;
        var set = Resources.GetResourceSet(target, createIfNotExists: true, tryParents: false);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (set is null) return keys;
        foreach (DictionaryEntry entry in set) keys.Add((string)entry.Key);
        return keys;
    }
}
```

Les trois `.resx` : format standard (en-tête `resheader` resmimetype/version/reader/writer de .NET). Contenu initial, une entrée :

| clé | `SharedResource.resx` (en) | `.fr.resx` | `.ar.resx` |
|---|---|---|---|
| `errors.pairing_code_invalid` | Invalid or expired code | Code invalide ou expiré | رمز غير صالح أو منتهي الصلاحية |
| `errors.rate_limited_seconds` | Too many attempts. Please wait {seconds} seconds. | Trop de tentatives. Patientez {seconds} secondes. | محاولات كثيرة جدًا. يرجى الانتظار {seconds} ثانية. |

Exemple d'entrée :

```xml
<data name="errors.pairing_code_invalid" xml:space="preserve">
  <value>Invalid or expired code</value>
</data>
```

Le SDK embarque les `.resx` automatiquement (nom manifeste `RestaurantPos.Infrastructure.Localization.SharedResource`), et produit les assemblies satellites `fr/` et `ar/`.

- [ ] **Step 4: Lancer `TextsTests`, vérifier le succès**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "FullyQualifiedName~TextsTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Test API `Accept-Language` (échoue)**

`tests/RestaurantPos.Api.Tests/LocalizationTests.cs` :

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.DTOs;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class LocalizationTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;
    public LocalizationTests(PosApiApplicationFactory factory) => _factory = factory;

    private async Task<string> PairWithBadCodeAsync(string? acceptLanguage)
    {
        var client = _factory.CreateClient();
        if (acceptLanguage is not null) client.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);
        var response = await client.PostAsJsonAsync("/api/devices/pair", new PairRequest("ZZZZZZZZ"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("message").GetString()!;
    }

    [Theory]
    [InlineData(null, "Invalid or expired code")]
    [InlineData("en", "Invalid or expired code")]
    [InlineData("de", "Invalid or expired code")]
    [InlineData("fr", "Code invalide ou expiré")]
    [InlineData("fr-CA", "Code invalide ou expiré")]
    [InlineData("ar-MA", "رمز غير صالح أو منتهي الصلاحية")]
    [InlineData("ar;q=0.9,en;q=0.8", "رمز غير صالح أو منتهي الصلاحية")]
    public async Task PairingError_IsTranslatedFromAcceptLanguage(string? header, string expected) =>
        (await PairWithBadCodeAsync(header)).Should().Be(expected);
}
```

Vérifier que `PairRequest` est bien le type utilisé par `DeviceEndpointsTests` (même `using`).

- [ ] **Step 6: Lancer, vérifier l'échec**

Run: `dotnet test tests/RestaurantPos.Api.Tests --filter "FullyQualifiedName~LocalizationTests"`
Expected: FAIL — message toujours « Code invalide ou expiré » pour `en`/`de`/`ar`.

- [ ] **Step 7: Brancher la localisation dans `Program.cs` et `DeviceEndpoints`**

Dans `Program.cs`, après `builder.Services.AddMemoryCache();` :

```csharp
        // Langue des messages : seule la culture d'interface suit Accept-Language.
        // CurrentCulture reste « en » pour que nombres, dates et hash ne changent jamais de format.
        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(culture: "en", uiCulture: "en");
            options.SupportedCultures = [CultureInfo.GetCultureInfo("en")];
            options.SupportedUICultures = Texts.SupportedLanguages.Select(CultureInfo.GetCultureInfo).ToList();
            options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
        });
```

Usings à ajouter : `System.Globalization`, `Microsoft.AspNetCore.Localization`, `RestaurantPos.Infrastructure.Localization`.

Dans le pipeline, juste après `app.UseCors();` :

```csharp
        app.UseRequestLocalization();
```

`DeviceEndpoints.cs:62` :

```csharp
                return Results.BadRequest(new { code = "pairing_code_invalid", message = Texts.T("errors.pairing_code_invalid") });
```

`DeviceEndpoints.cs:54` :

```csharp
                    new { code = "rate_limited", message = Texts.T("errors.rate_limited_seconds", ("seconds", wait)) },
```

Ajouter un test dans `LocalizationTests` pour la contrainte « culture de formatage figée » :

```csharp
    [Fact]
    public void AcceptLanguage_Ar_DoesNotChangeCurrentCulture()
    {
        var options = _factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>>().Value;
        options.SupportedCultures!.Select(c => c.Name).Should().Equal("en");
        options.SupportedUICultures!.Select(c => c.Name).Should().Equal("en", "fr", "ar");
    }
```

- [ ] **Step 8: Adapter `DeviceEndpointsTests.cs:131`**

Le client de test n'envoie pas d'`Accept-Language` → anglais :

```csharp
            body.GetProperty("message").GetString().Should().Be("Invalid or expired code");
```

- [ ] **Step 9: Build + tests**

Run: `dotnet format RestaurantPos.slnx && dotnet build RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: build OK, tous les tests PASS.

- [ ] **Step 10: Commit**

```bash
git add src/RestaurantPos.Infrastructure/Localization src/RestaurantPos.Api/Program.cs src/RestaurantPos.Api/Endpoints/DeviceEndpoints.cs tests/RestaurantPos.Api.Tests/LocalizationTests.cs tests/RestaurantPos.Api.Tests/DeviceEndpointsTests.cs tests/RestaurantPos.Infrastructure.Tests/TextsTests.cs
git commit -m "feat(i18n): localisation serveur par Accept-Language (en/fr/ar)"
```

---

### Task 2: Traduire tous les messages serveur renvoyés aux clients

**Files:**
- Modify: `src/RestaurantPos.Infrastructure/Localization/SharedResource*.resx`
- Modify: `src/RestaurantPos.Api/Endpoints/{RequireDeviceFilter,GridEndpoints,DeviceEndpoints,HospitalityEndpoints,AuthEndpoints,CounterSaleEndpoints,TableEndpoints,CheckoutEndpoints,FiscalEndpoints,HappyHourEndpoints,SyncEndpoints}.cs`, `src/RestaurantPos.Api/Program.cs:357`
- Modify: `src/RestaurantPos.Infrastructure/Security/OperatorAuthenticationService.cs:23,47`, `Services/MealVoucherPolicyService.cs:33,49`, `Services/HappyHourPricingService.cs:248,323`, `Services/PrinterConfigurationService.cs:108,113`
- Modify tests: `tests/RestaurantPos.Api.Tests/MealVoucherPolicyTests.cs:118`, `tests/RestaurantPos.Infrastructure.Tests/PrinterConfigurationServiceTests.cs:59`, `HappyHourPricingServiceTests.cs:197`

**Interfaces:**
- Consumes: `Texts.T(key, args)` (Task 1).
- Produces: les clés ci-dessous, utilisables par les clients qui affichent `message`.

Hors périmètre de cette tâche : messages `LoggerMessage` (logs), exceptions non interceptées (renvoient 500 sans corps), `NF525FiscalAuditService.ErrorDetails` (rapport d'audit technique).

- [ ] **Step 1: Test de non-régression (échoue)**

Ajouter à `LocalizationTests.cs` :

```csharp
    [Fact]
    public async Task NoFrenchMessage_WhenEnglishRequested()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "en");
        var response = await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("0000"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorMessage").GetString().Should().Be("Invalid PIN or credentials");
    }

    [Fact]
    public async Task AuthError_InFrench_WhenFrenchRequested()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "fr");
        var response = await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("0000"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorMessage").GetString().Should().Be("Code PIN ou identifiants incorrects");
    }
```

Vérifier le nom JSON exact (`errorMessage`) dans `AuthEndpointsTests.cs` ; si `0000` déclenche « Code PIN invalide » (format), attendre `errors.pin_invalid` à la place.

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `dotnet test tests/RestaurantPos.Api.Tests --filter "FullyQualifiedName~LocalizationTests"`
Expected: FAIL (message français).

- [ ] **Step 3: Ajouter les clés aux trois `.resx`**

| clé | en | fr | ar |
|---|---|---|---|
| `errors.device_not_paired` | This device is not paired with the server. | Ce poste n'est pas appairé au serveur. | هذا الجهاز غير مقترن بالخادم. |
| `errors.grid_not_found_for_category` | No grid for category {category} (page {page}) | Aucune grille pour la catégorie {category} (page {page}) | لا توجد شبكة للفئة {category} (الصفحة {page}) |
| `errors.grid_swap_not_found` | Grid not found for the swap. | Grille introuvable pour la permutation. | الشبكة غير موجودة لعملية التبديل. |
| `errors.device_name_role_required` | Name (max 64 characters) and role (Caisse, Serveur, Cuisine, BackOffice) are required. | Nom (64 caractères max) et rôle (Caisse, Serveur, Cuisine, BackOffice) obligatoires. | الاسم (64 حرفًا كحد أقصى) والدور (Caisse، Serveur، Cuisine، BackOffice) مطلوبان. |
| `messages.order_transferred` | Order moved from {from} to {to} | Commande transférée de {from} vers {to} | تم نقل الطلب من {from} إلى {to} |
| `errors.table_transfer_failed` | Table transfer failed. | Échec du transfert de table. | فشل نقل الطاولة. |
| `messages.tables_merged` | Tables {from} and {to} merged | Tables {from} et {to} fusionnées | تم دمج الطاولتين {from} و{to} |
| `errors.table_merge_failed` | Table merge failed. | Échec de la fusion de tables. | فشل دمج الطاولات. |
| `errors.discount_failed` | Discount operation failed. | Opération de remise échouée. | فشلت عملية الخصم. |
| `errors.comp_failed` | Complimentary item operation failed. | Opération de gratuité échouée. | فشلت عملية المجانية. |
| `messages.next_course_fired` | Next course sent to the kitchen for table {table} | Réclame suite transmise en cuisine pour la table {table} | تم إرسال الطبق التالي إلى المطبخ للطاولة {table} |
| `errors.room_not_found` | Room {room} not found or not occupied. | Chambre {room} introuvable ou non occupée. | الغرفة {room} غير موجودة أو غير مشغولة. |
| `messages.room_charge_recorded` | Charge of {amount} € recorded on room {room} ({guest}) | Facturation de {amount} € enregistrée sur la chambre {room} ({guest}) | تم تسجيل مبلغ {amount} € على الغرفة {room} ({guest}) |
| `errors.room_charge_failed` | Room charge failed. | Facturation chambre échouée. | فشل تحميل المبلغ على الغرفة. |
| `errors.login_rate_limited` | Too many failed attempts. Please wait {seconds} seconds. | Trop de tentatives infructueuses. Veuillez patienter {seconds} secondes. | محاولات فاشلة كثيرة جدًا. يرجى الانتظار {seconds} ثانية. |
| `errors.supervisor_rate_limited` | Too many attempts. Please wait {seconds} seconds. | Trop de tentatives. Veuillez patienter {seconds} secondes. | محاولات كثيرة جدًا. يرجى الانتظار {seconds} ثانية. |
| `errors.supervisor_pin_invalid` | Invalid supervisor PIN. | Code PIN superviseur invalide. | رمز PIN للمشرف غير صالح. |
| `errors.supervisor_privileges_insufficient` | Insufficient supervisor privileges. | Privilèges superviseur insuffisants. | صلاحيات المشرف غير كافية. |
| `errors.order_not_found` | Order not found. | Commande introuvable. | الطلب غير موجود. |
| `errors.hold_empty_cart` | Cannot put an empty cart on hold. | Impossible de mettre en attente un panier vide. | لا يمكن تعليق سلة فارغة. |
| `errors.held_order_not_found_or_recalled` | Held order not found or already recalled. | Commande en attente introuvable ou déjà rappelée. | الطلب المعلّق غير موجود أو تم استرجاعه مسبقًا. |
| `errors.supervisor_pin_required` | Supervisor PIN required. | Code PIN superviseur requis. | رمز PIN للمشرف مطلوب. |
| `errors.authorization_insufficient` | Insufficient authorization: supervisor or manager PIN required. | Autorisation insuffisante : code PIN superviseur ou gérant requis. | صلاحية غير كافية: يلزم رمز PIN للمشرف أو المدير. |
| `errors.held_order_not_found` | Held order not found. | Commande en attente introuvable. | الطلب المعلّق غير موجود. |
| `messages.held_order_cancelled` | Held order cancelled. | Commande en attente annulée avec succès. | تم إلغاء الطلب المعلّق. |
| `errors.order_not_found_or_empty` | Order not found or cart empty. | Commande introuvable ou panier vide. | الطلب غير موجود أو السلة فارغة. |
| `errors.table_number_required` | Table number is required. | Le numéro de table est requis. | رقم الطاولة مطلوب. |
| `errors.no_active_order_on_table` | No active order on table {table} | Aucune commande active sur la table {table} | لا يوجد طلب نشط على الطاولة {table} |
| `errors.order_not_found_for_payment` | Order not found for this payment. | Commande introuvable pour ce règlement. | الطلب غير موجود لهذه الدفعة. |
| `errors.payment_failed` | Payment failed. | Échec de l'encaissement. | فشل الدفع. |
| `errors.operator_required_for_void` | OperatorId is required to void. | OperatorId est obligatoire pour l'annulation. | معرّف المشغّل مطلوب للإلغاء. |
| `errors.void_failed` | Unable to void the receipt. | Impossible d'annuler le reçu. | تعذّر إلغاء الإيصال. |
| `errors.z_closure_manager_required` | ManagerId and ManagerName are required for the Z closure. | ManagerId et ManagerName sont obligatoires pour la clôture Z. | معرّف المدير واسمه مطلوبان لإقفال Z. |
| `errors.no_closure_found` | No closure found. | Aucune clôture trouvée. | لم يتم العثور على أي إقفال. |
| `errors.time_format_invalid` | Invalid time format (HH:mm required). | Format horaire invalide (HH:mm requis). | صيغة الوقت غير صالحة (المطلوب HH:mm). |
| `messages.happy_hour_created` | Happy Hour slot created. | Plage Happy Hour créée avec succès. | تم إنشاء فترة Happy Hour. |
| `errors.time_slot_not_found` | Time slot not found. | Plage horaire introuvable. | الفترة الزمنية غير موجودة. |
| `messages.time_slot_deleted` | Time slot deleted. | Plage horaire supprimée. | تم حذف الفترة الزمنية. |
| `errors.sync_payment_failed` | Synchronized payment failed. | Échec de l'encaissement synchronisé. | فشل الدفع المتزامن. |
| `messages.test_data_seeded` | Production test data inserted into the database. | Données de test de production insérées avec succès dans la base de données. | تم إدراج بيانات الاختبار في قاعدة البيانات. |
| `errors.pin_invalid` | Invalid PIN | Code PIN invalide | رمز PIN غير صالح |
| `errors.pin_or_credentials_invalid` | Invalid PIN or credentials | Code PIN ou identifiants incorrects | رمز PIN أو بيانات الدخول غير صحيحة |
| `errors.meal_voucher_above_legal_cap` | Meal voucher amount ({amount} €) exceeds the legal eligible cap ({max} €). | Le montant par Titre-Restaurant ({amount} €) dépasse le plafond légal éligible ({max} €). | مبلغ قسيمة الوجبة ({amount} €) يتجاوز الحد القانوني المؤهل ({max} €). |
| `errors.meal_voucher_overpayment_refused` | Meal voucher overpayment refused: face value ({face} €) exceeds the balance due ({due} €). | Surpaiement par Titre-Restaurant refusé : la valeur faciale ({face} €) dépasse le solde dû ({due} €). | رُفض الدفع الزائد بقسيمة الوجبة: القيمة الاسمية ({face} €) تتجاوز الرصيد المستحق ({due} €). |
| `messages.test_print_ok` | Test print succeeded ({name} @ {ip}:{port}) | Test d'impression réussi ({name} @ {ip}:{port}) | نجحت الطباعة التجريبية ({name} @ {ip}:{port}) |
| `errors.test_print_failed` | Printer connection failed ({ip}:{port}): {error} | Échec de connexion imprimante ({ip}:{port}) : {error} | فشل الاتصال بالطابعة ({ip}:{port}): {error} |

- [ ] **Step 4: Remplacer chaque littéral par `Texts.T`**

Règle : `Message = "<fr>"` → `Message = Texts.T("<clé>")` ; interpolations → placeholders. Montants : formater avant, en invariant, `"0.00"`. Exemples exacts :

```csharp
// GridEndpoints.cs:29
Results.NotFound(new { Message = Texts.T("errors.grid_not_found_for_category", ("category", categoryId), ("page", pageIndex)) })

// AuthEndpoints.cs:34
return Results.Json(new { Success = false, ErrorMessage = Texts.T("errors.login_rate_limited", ("seconds", Math.Ceiling(remaining.TotalSeconds))) }, statusCode: StatusCodes.Status429TooManyRequests);

// HospitalityEndpoints.cs:141
Message = Texts.T("messages.room_charge_recorded",
    ("amount", (charge.Amount + charge.TipAmount).ToDecimal().ToString("0.00", CultureInfo.InvariantCulture)),
    ("room", charge.RoomNumber), ("guest", charge.GuestName)),

// MealVoucherPolicyService.cs:33
ErrorMessage: Texts.T("errors.meal_voucher_above_legal_cap",
    ("amount", voucherAmount.ToString("0.00", CultureInfo.InvariantCulture)),
    ("max", legalMax.ToString("0.00", CultureInfo.InvariantCulture))),

// PrinterConfigurationService.cs:113
return new TestPrintResult(false, Texts.T("errors.test_print_failed", ("ip", printer.IpAddress), ("port", printer.Port), ("error", ex.Message)), sw.Elapsed);
```

`HappyHourPricingService.cs:248,323` → `Texts.T("errors.authorization_insufficient")`. `CounterSaleEndpoints.cs:182` réutilise la même clé.

Vérification qu'il ne reste rien :

Run: `grep -rnE "(Message|message|ErrorMessage) = \\$?\"" src/RestaurantPos.Api --include='*.cs' | grep -v LoggerMessage`
Expected: aucune ligne.

- [ ] **Step 5: Adapter les tests existants (sans en-tête → anglais ; InMemory → `CurrentUICulture` du thread)**

`MealVoucherPolicyTests.cs:118` :

```csharp
        content.Should().Contain("Meal voucher overpayment refused");
```

`PrinterConfigurationServiceTests.cs` et `HappyHourPricingServiceTests.cs` : ces tests appellent le service hors requête HTTP, la culture est celle de la machine. En tête des deux méthodes concernées :

```csharp
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
```

puis `testResult.Message.Should().Contain("Printer connection failed");` et `response.Message.Should().Contain("Insufficient authorization");`.

- [ ] **Step 6: Build + tests**

Run: `dotnet format RestaurantPos.slnx && dotnet build RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS, dont `TextsTests.EveryEnglishKey_ExistsInFrenchAndArabic_AndIsNotEmpty`.

- [ ] **Step 7: Commit**

```bash
git add -A src tests
git commit -m "feat(i18n): messages serveur traduits (clés errors.* / messages.*)"
```

---

### Task 3: Réglages restaurant (`ReceiptLanguage`) + endpoints

**Files:**
- Create: `src/RestaurantPos.Domain/Entities/RestaurantSettings.cs`
- Create: `src/RestaurantPos.Application/Common/Interfaces/IRestaurantSettingsService.cs`
- Create: `src/RestaurantPos.Infrastructure/Services/RestaurantSettingsService.cs`
- Create: `src/RestaurantPos.Api/Endpoints/SettingsEndpoints.cs`
- Modify: `src/RestaurantPos.Infrastructure/Persistence/AppDbContext.cs` (DbSet + config), `src/RestaurantPos.Api/Program.cs` (DI, `CREATE TABLE`, `app.MapSettingsEndpoints()`)
- Test: `tests/RestaurantPos.Infrastructure.Tests/RestaurantSettingsServiceTests.cs`, `tests/RestaurantPos.Api.Tests/SettingsEndpointsTests.cs`

**Interfaces:**
- Consumes: `Texts.SupportedLanguages`, `Texts.T` (Task 1).
- Produces:
  - `RestaurantSettings { int Id = 1; string ReceiptLanguage; DateTimeOffset UpdatedAtUtc }`
  - `RestaurantSettingsDto(string ReceiptLanguage)`, `UpdateRestaurantSettingsRequest(string ReceiptLanguage)`
  - `IRestaurantSettingsService.GetAsync(CancellationToken) : Task<RestaurantSettingsDto>`
  - `IRestaurantSettingsService.UpdateAsync(UpdateRestaurantSettingsRequest, CancellationToken) : Task<RestaurantSettingsDto?>` — `null` si langue invalide.
  - `GET /api/settings` (authentifié) → `{"receiptLanguage":"fr"}` ; `PUT /api/settings` (`RequireManagerOrAdmin`) → 200 DTO | 400 `{ message = errors.receipt_language_invalid }`.

- [ ] **Step 1: Tests service (échouent)**

```csharp
using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class RestaurantSettingsServiceTests
{
    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase($"PosTest_Settings_{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Get_OnEmptyDatabase_InitializesEnglish()
    {
        using var db = NewDb();
        var dto = await new RestaurantSettingsService(db).GetAsync();
        dto.ReceiptLanguage.Should().Be("en");
        (await db.RestaurantSettings.SingleAsync()).ReceiptLanguage.Should().Be("en");
    }

    [Fact]
    public async Task Get_WithExistingOrders_InitializesFrench()
    {
        using var db = NewDb();
        db.Orders.Add(new Order());
        await db.SaveChangesAsync();
        (await new RestaurantSettingsService(db).GetAsync()).ReceiptLanguage.Should().Be("fr");
    }

    [Fact]
    public async Task Get_IsStable_AfterOrdersAppear()
    {
        using var db = NewDb();
        var service = new RestaurantSettingsService(db);
        await service.GetAsync();
        db.Orders.Add(new Order());
        await db.SaveChangesAsync();
        (await service.GetAsync()).ReceiptLanguage.Should().Be("en");
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("en")]
    public async Task Update_AcceptsSupportedLanguage(string lang)
    {
        using var db = NewDb();
        var service = new RestaurantSettingsService(db);
        (await service.UpdateAsync(new UpdateRestaurantSettingsRequest(lang)))!.ReceiptLanguage.Should().Be(lang);
        (await service.GetAsync()).ReceiptLanguage.Should().Be(lang);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("")]
    [InlineData("FR")]
    [InlineData("fr-FR")]
    public async Task Update_RejectsOtherValues(string lang)
    {
        using var db = NewDb();
        (await new RestaurantSettingsService(db).UpdateAsync(new UpdateRestaurantSettingsRequest(lang))).Should().BeNull();
    }
}
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "FullyQualifiedName~RestaurantSettingsServiceTests"`
Expected: FAIL compilation.

- [ ] **Step 3: Implémenter entité, interface, service, DbContext**

`RestaurantSettings.cs` :

```csharp
using System;

namespace RestaurantPos.Domain.Entities;

/// <summary>Réglages du restaurant (ligne unique, Id = 1).</summary>
public class RestaurantSettings
{
    public const int SingletonId = 1;
    public int Id { get; init; } = SingletonId;
    public required string ReceiptLanguage { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
```

`IRestaurantSettingsService.cs` :

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Application.Common.Interfaces;

public record RestaurantSettingsDto(string ReceiptLanguage);
public record UpdateRestaurantSettingsRequest(string ReceiptLanguage);

public interface IRestaurantSettingsService
{
    Task<RestaurantSettingsDto> GetAsync(CancellationToken ct = default);
    /// <returns>null si la langue n'est pas supportée.</returns>
    Task<RestaurantSettingsDto?> UpdateAsync(UpdateRestaurantSettingsRequest request, CancellationToken ct = default);
}
```

`RestaurantSettingsService.cs` :

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class RestaurantSettingsService : IRestaurantSettingsService
{
    private readonly AppDbContext _db;
    public RestaurantSettingsService(AppDbContext db) => _db = db;

    public async Task<RestaurantSettingsDto> GetAsync(CancellationToken ct = default) =>
        new((await LoadOrCreateAsync(ct).ConfigureAwait(false)).ReceiptLanguage);

    public async Task<RestaurantSettingsDto?> UpdateAsync(UpdateRestaurantSettingsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Texts.SupportedLanguages.Contains(request.ReceiptLanguage, StringComparer.Ordinal)) return null;
        var settings = await LoadOrCreateAsync(ct).ConfigureAwait(false);
        settings.ReceiptLanguage = request.ReceiptLanguage;
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new(settings.ReceiptLanguage);
    }

    // Première lecture : une base qui a déjà des commandes est une installation française existante.
    private async Task<RestaurantSettings> LoadOrCreateAsync(CancellationToken ct)
    {
        var settings = await _db.RestaurantSettings.FindAsync([RestaurantSettings.SingletonId], ct).ConfigureAwait(false);
        if (settings is not null) return settings;
        var hasOrders = await _db.Orders.AnyAsync(ct).ConfigureAwait(false);
        settings = new RestaurantSettings { ReceiptLanguage = hasOrders ? "fr" : "en" };
        _db.RestaurantSettings.Add(settings);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return settings;
    }
}
```

`AppDbContext.cs` : ajouter `public DbSet<RestaurantSettings> RestaurantSettings => Set<RestaurantSettings>();` et dans `OnModelCreating` :

```csharp
        modelBuilder.Entity<RestaurantSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ReceiptLanguage).HasMaxLength(8).IsRequired();
        });
```

- [ ] **Step 4: Lancer les tests service → PASS**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "FullyQualifiedName~RestaurantSettingsServiceTests"`
Expected: PASS (10).

- [ ] **Step 5: Tests endpoints (échouent)**

`tests/RestaurantPos.Api.Tests/SettingsEndpointsTests.cs` (même modèle d'auth que `HeldOrderQueueTests`, PIN `9999` admin, `2468` serveur) :

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class SettingsEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;
    public SettingsEndpointsTests(PosApiApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string pin)
    {
        var client = _factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest(pin))).Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    [Fact]
    public async Task Put_ThenGet_RoundTrips()
    {
        var admin = await ClientAsync("9999");
        (await admin.PutAsJsonAsync("/api/settings", new UpdateRestaurantSettingsRequest("ar"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await admin.GetFromJsonAsync<JsonElement>("/api/settings");
        body.GetProperty("receiptLanguage").GetString().Should().Be("ar");
    }

    [Fact]
    public async Task Put_InvalidLanguage_Returns400_Translated()
    {
        var admin = await ClientAsync("9999");
        admin.DefaultRequestHeaders.Add("Accept-Language", "fr");
        var response = await admin.PutAsJsonAsync("/api/settings", new UpdateRestaurantSettingsRequest("de"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()
            .Should().Be("Langue des tickets non supportée (en, fr, ar).");
    }

    [Fact]
    public async Task Put_AsWaiter_IsForbidden()
    {
        var waiter = await ClientAsync("2468");
        (await waiter.PutAsJsonAsync("/api/settings", new UpdateRestaurantSettingsRequest("fr"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_Anonymous_IsUnauthorized() =>
        (await _factory.CreateClient().GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
}
```

Si `TestAuthHandler` authentifie tout le monde en environnement `Testing` (le lire avant), adapter `Get_Anonymous_IsUnauthorized` et `Put_AsWaiter_IsForbidden` au comportement réel des autres tests de rôle (ex. `AuthEndpointsTests`) et le noter dans le commit.

- [ ] **Step 6: Implémenter endpoints, DI, schéma**

`SettingsEndpoints.cs` :

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings").RequireAuthorization();

        group.MapGet("/", async (IRestaurantSettingsService settings) => Results.Ok(await settings.GetAsync()));

        group.MapPut("/", async (UpdateRestaurantSettingsRequest req, IRestaurantSettingsService settings) =>
        {
            var updated = await settings.UpdateAsync(req);
            return updated is null
                ? Results.BadRequest(new { message = Texts.T("errors.receipt_language_invalid") })
                : Results.Ok(updated);
        }).RequireAuthorization("RequireManagerOrAdmin");
    }
}
```

`.resx` : `errors.receipt_language_invalid` = en « Unsupported receipt language (en, fr, ar). » / fr « Langue des tickets non supportée (en, fr, ar). » / ar « لغة التذاكر غير مدعومة (en، fr، ar). ».

`Program.cs` : `builder.Services.AddScoped<IRestaurantSettingsService, RestaurantSettingsService>();` ; `app.MapSettingsEndpoints();` à côté de `app.MapPrinterEndpoints();` ; dans le bloc schéma, après `DevicePairingCodes` :

```csharp
            try { dbContext.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS RestaurantSettings (
                Id INTEGER PRIMARY KEY,
                ReceiptLanguage TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );"); } catch { }
```

Vérifier que le stockage de `DateTimeOffset` dans SQLite suit la même convention que les autres tables (`TEXT`), en regardant `Devices.PairedAtUtc`.

- [ ] **Step 7: Build + tests + vérification manuelle SQLite**

Run: `dotnet format RestaurantPos.slnx && dotnet build RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS.

Puis démarrer l'API (commande CLAUDE.md, port 5080) sur la base existante et :

```bash
TOKEN=$(curl -s -X POST localhost:5080/api/auth/login -H 'Content-Type: application/json' -d '{"pin":"9999"}' | jq -r .token)
curl -s localhost:5080/api/settings -H "Authorization: Bearer $TOKEN"
```

Expected: `{"receiptLanguage":"fr"}` sur une base qui a des commandes.

Sauvegarder cette réponse dans `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/settings.json` (utilisée Task 10).

- [ ] **Step 8: Commit**

```bash
git add -A src tests ios/Packages/PosKit/Tests/PosKitTests/Fixtures/settings.json
git commit -m "feat(settings): réglage restaurant ReceiptLanguage + GET/PUT /api/settings"
```

---

### Task 4: `TicketDocument` localisé (remplace `TakeawayTicketFormatter`)

**Files:**
- Create: `src/RestaurantPos.Infrastructure/Printing/TicketDocument.cs`
- Create: `src/RestaurantPos.Infrastructure/Printing/TicketDocumentBuilder.cs`
- Delete: `src/RestaurantPos.Infrastructure/Printing/TakeawayTicketFormatter.cs` (aucun appelant : vérifier avec `grep -rn TakeawayTicketFormatter src tests`)
- Modify: `.resx` (clés `receipt.*`)
- Test: `tests/RestaurantPos.Infrastructure.Tests/TicketDocumentBuilderTests.cs`

**Interfaces:**
- Consumes: `Texts.Get(CultureInfo, key, args)`.
- Produces (utilisé par le sous-projet C) :

```csharp
public enum TicketAlign { Start, Center, End }
public abstract record TicketLine;
public sealed record TicketText(string Text, TicketAlign Align = TicketAlign.Start, bool Bold = false, bool Large = false) : TicketLine;
public sealed record TicketColumns(string Label, string Value) : TicketLine;   // libellé côté début, valeur côté fin
public sealed record TicketSeparator(bool Cut = false) : TicketLine;
public sealed record TicketDocument(string Language, bool RightToLeft, IReadOnlyList<TicketLine> Lines);

public static class TicketDocumentBuilder
{
    public static TicketDocument PickupCoupon(Order order, string pickupNumber, string? buzzer, string language, DateTimeOffset nowUtc);
    public static TicketDocument FiscalReceipt(FiscalReceipt receipt, Order order, string pickupNumber, string? buzzer, string language);
}
```

- [ ] **Step 1: Tests (échouent)**

```csharp
using System;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Printing;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class TicketDocumentBuilderTests
{
    private static Order SampleOrder() => new()
    {
        Destination = OrderDestination.Takeaway,
        Items = { new OrderItem { ProductName = "Burger Rossini", Quantity = 2, UnitPrice = Money.FromCents(1250) } }
    };

    private static FiscalReceipt SampleReceipt() => new()
    {
        TerminalId = "T01", ReceiptNumber = "T01-000042",
        TotalTtcAmount = Money.FromCents(2500), TotalHtAmount = Money.FromCents(2273),
        TaxBreakdownJson = "{\"10\":227}", SignatureHash = "abc123"
    };

    private static string AllText(TicketDocument doc) => string.Join("\n", doc.Lines.Select(l => l switch
    {
        TicketText t => t.Text,
        TicketColumns c => c.Label + " | " + c.Value,
        _ => "---"
    }));

    [Theory]
    [InlineData("en", "TAKEAWAY", "PICKUP")]
    [InlineData("fr", "À EMPORTER", "RETRAIT")]
    [InlineData("ar", "سفري", "استلام")]
    public void PickupCoupon_UsesReceiptLanguage(string lang, string mode, string title)
    {
        var doc = TicketDocumentBuilder.PickupCoupon(SampleOrder(), "A12", "7", lang, DateTimeOffset.UnixEpoch);
        var text = AllText(doc);
        text.Should().Contain(mode).And.Contain(title).And.Contain("A12").And.Contain("2x Burger Rossini");
        doc.Language.Should().Be(lang);
    }

    [Fact]
    public void Arabic_IsRightToLeft_OthersNot()
    {
        TicketDocumentBuilder.PickupCoupon(SampleOrder(), "1", null, "ar", DateTimeOffset.UnixEpoch).RightToLeft.Should().BeTrue();
        TicketDocumentBuilder.PickupCoupon(SampleOrder(), "1", null, "fr", DateTimeOffset.UnixEpoch).RightToLeft.Should().BeFalse();
    }

    [Fact]
    public void FiscalReceipt_ContainsTotals_Signature_AndPickupCoupon()
    {
        var doc = TicketDocumentBuilder.FiscalReceipt(SampleReceipt(), SampleOrder(), "A12", null, "en");
        var text = AllText(doc);
        text.Should().Contain("TOTAL INCL. VAT | 25.00").And.Contain("abc123").And.Contain("T01-000042").And.Contain("PICKUP");
        doc.Lines.OfType<TicketSeparator>().Should().Contain(s => s.Cut);
    }

    [Fact]
    public void Amounts_UseWesternDigits_InArabic()
    {
        var text = AllText(TicketDocumentBuilder.FiscalReceipt(SampleReceipt(), SampleOrder(), "A12", null, "ar"));
        text.Should().Contain("25.00").And.NotContainAny("٠", "١", "٢", "٥");
    }

    [Fact]
    public void UnsupportedLanguage_FallsBackToEnglish() =>
        AllText(TicketDocumentBuilder.PickupCoupon(SampleOrder(), "1", null, "de", DateTimeOffset.UnixEpoch)).Should().Contain("TAKEAWAY");

    [Fact]
    public void Nf525Hash_IsIndependentOfCulture()
    {
        var service = new NF525FiscalAuditService(null!);
        string Hash() => service.ComputeReceiptHashSignature(NF525FiscalAuditService.GenesisHash, "T01", 1, 2500,
            new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero), "{\"10\":227}");
        var original = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            var en = Hash();
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
            var fr = Hash();
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar");
            Hash().Should().Be(en).And.Be(fr);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = original;
        }
    }
}
```

Avant d'écrire : vérifier la signature réelle de `ComputeReceiptHashSignature` (`NF525FiscalAuditService.cs:29`) et l'ordre de ses paramètres, et que le constructeur tolère un `AppDbContext` inutilisé (sinon passer un contexte InMemory). Vérifier que `OrderItem.CalculateTotalTtc()` donne 25,00 € pour 2 × 12,50 (taux 10 %, TTC).

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "FullyQualifiedName~TicketDocumentBuilderTests"`
Expected: FAIL compilation. (`Nf525Hash_IsIndependentOfCulture` peut échouer pour une vraie raison : si c'est le cas, c'est un bug NF525 à signaler avant d'aller plus loin, pas à contourner.)

- [ ] **Step 3: Clés `receipt.*` dans les trois `.resx`**

| clé | en | fr | ar |
|---|---|---|---|
| `receipt.pickup_title` | PICKUP VOUCHER | BON DE RETRAIT COMMANDE | قسيمة استلام الطلب |
| `receipt.number` | NUMBER | NUMÉRO | الرقم |
| `receipt.buzzer` | BUZZER | BIPEUR | جهاز النداء |
| `receipt.date` | Date | Date | التاريخ |
| `receipt.mode` | Mode | Mode | النوع |
| `receipt.mode_takeaway` | TAKEAWAY | À EMPORTER | سفري |
| `receipt.mode_eat_in` | EAT IN | SUR PLACE | في المطعم |
| `receipt.items_count` | Items: {count} line(s) | Articles : {count} ligne(s) | الأصناف: {count} سطر |
| `receipt.keep_until_pickup` | KEEP THIS VOUCHER UNTIL PICKUP | CONSERVEZ CE BON JUSQU'AU RETRAIT | احتفظ بهذه القسيمة حتى الاستلام |
| `receipt.ticket` | Receipt | Ticket | الإيصال |
| `receipt.terminal` | Terminal | Caisse | الصندوق |
| `receipt.total_ttc` | TOTAL INCL. VAT | TOTAL TTC | المجموع شامل الضريبة |
| `receipt.total_ht` | TOTAL EXCL. VAT | TOTAL HT | المجموع دون الضريبة |
| `receipt.vat_breakdown` | VAT BREAKDOWN (NF525) | VENTILATION TVA (NF525) | تفصيل الضريبة (NF525) |
| `receipt.fiscal_signature` | NF525 FISCAL SIGNATURE | SIGNATURE FISCALE NF525 | التوقيع الضريبي NF525 |

- [ ] **Step 4: Implémenter `TicketDocument.cs` (types ci-dessus) et `TicketDocumentBuilder.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>Construit les tickets dans la langue des tickets du restaurant. Aucun rendu ni envoi imprimante (sous-projet C).</summary>
public static class TicketDocumentBuilder
{
    // ponytail: en-tête restaurant en dur, repris de l'ancien formatter ; à déplacer dans RestaurantSettings quand un écran de saisie existera.
    private static readonly string[] Header =
        ["RESTAURANT L'ANTIGRAVITE", "12 Rue de la Gastronomie", "75001 Paris", "SIRET: 888 777 666 00012", "TVA: FR 12 888777666"];

    public static TicketDocument PickupCoupon(Order order, string pickupNumber, string? buzzer, string language, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(order);
        var (lang, culture) = Resolve(language);
        var lines = new List<TicketLine>();
        AppendPickup(lines, culture, order, pickupNumber, buzzer, nowUtc);
        return new TicketDocument(lang, lang == "ar", lines);
    }

    public static TicketDocument FiscalReceipt(FiscalReceipt receipt, Order order, string pickupNumber, string? buzzer, string language)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(order);
        var (lang, c) = Resolve(language);
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
        lines.Add(new TicketSeparator(Cut: true));
        AppendPickup(lines, c, order, pickupNumber, buzzer, receipt.CreatedAtUtc);
        return new TicketDocument(lang, lang == "ar", lines);
    }

    private static void AppendPickup(List<TicketLine> lines, CultureInfo c, Order order, string pickupNumber, string? buzzer, DateTimeOffset nowUtc)
    {
        lines.Add(new TicketText(Texts.Get(c, "receipt.pickup_title"), TicketAlign.Center, Bold: true));
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.number"), pickupNumber));
        if (!string.IsNullOrWhiteSpace(buzzer)) lines.Add(new TicketColumns(Texts.Get(c, "receipt.buzzer"), buzzer));
        lines.Add(new TicketSeparator());
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.date"), nowUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)));
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.mode"), Mode(c, order)));
        lines.Add(new TicketText(Texts.Get(c, "receipt.items_count", ("count", order.Items.Count))));
        lines.Add(new TicketSeparator());
        foreach (var item in order.Items)
        {
            lines.Add(new TicketText($"{item.Quantity}x {item.ProductName}"));
            if (item.SelectedModifiers.Count > 0) lines.Add(new TicketText("+ " + string.Join(", ", item.SelectedModifiers)));
        }
        lines.Add(new TicketSeparator());
        lines.Add(new TicketText(Texts.Get(c, "receipt.keep_until_pickup"), TicketAlign.Center));
    }

    private static (string Lang, CultureInfo Culture) Resolve(string language)
    {
        var lang = Array.IndexOf(Texts.SupportedLanguages, language) >= 0 ? language : "en";
        return (lang, CultureInfo.GetCultureInfo(lang));
    }

    private static string Mode(CultureInfo c, Order order) =>
        Texts.Get(c, order.Destination == OrderDestination.Takeaway ? "receipt.mode_takeaway" : "receipt.mode_eat_in");

    // Devise en dur jusqu'au sous-projet B.
    private static string Amount(long cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
```

Vérifier que `item.SelectedModifiers` existe sous ce nom (utilisé par l'ancien formatter ligne 36) et `Money.AmountInCents`.

- [ ] **Step 5: Supprimer `TakeawayTicketFormatter.cs`, build + tests**

Run: `git rm src/RestaurantPos.Infrastructure/Printing/TakeawayTicketFormatter.cs && dotnet format RestaurantPos.slnx && dotnet build RestaurantPos.slnx && dotnet test RestaurantPos.slnx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A src tests
git commit -m "feat(tickets): TicketDocument structuré et localisé, remplace TakeawayTicketFormatter"
```

---

### Task 5: Runtime i18n web + sélecteur de langue + script de vérification

**Files:**
- Create: `src/RestaurantPos.Api/wwwroot/i18n.js`
- Create: `src/RestaurantPos.Api/wwwroot/i18n/en.json`, `fr.json`, `ar.json` (initialement : clés `login.*`, `common.language_*`)
- Create: `scripts/i18n-check.mjs`
- Create: `tests/RestaurantPos.Web.E2ETests/tests/i18n.spec.ts`
- Modify: `wwwroot/index.html` (script, modal PIN l.931-963), `wwwroot/app.js` (l.2 et wrapper `fetch` l.66-115), `tests/RestaurantPos.Web.E2ETests/playwright.config.ts`

**Interfaces:**
- Produces (globaux, utilisés par Tasks 6-9) :
  - `window.i18nReady : Promise<void>`
  - `window.t(key: string, params?: Record<string, string|number>) : string`
  - `window.i18n = { lang: 'en'|'fr'|'ar', dir: 'ltr'|'rtl', locale: string /* 'ar-u-nu-latn' en arabe */, setLanguage(lang) /* écrit localStorage 'pos_lang' + reload */, apply(root = document) }`
  - Attributs HTML : `data-i18n`, `data-i18n-placeholder`, `data-i18n-aria-label`, `data-i18n-title`.
  - `node scripts/i18n-check.mjs [--langs en,fr,ar] [--no-french]` : code 1 si écart.

- [ ] **Step 1: Épingler les tests existants en français**

`playwright.config.ts`, dans `use` :

```ts
    locale: 'fr-FR',
```

- [ ] **Step 2: Écrire `i18n.spec.ts` (échoue)**

```ts
import { test, expect } from '@playwright/test';

test.describe('Langue de l\'interface', () => {
  test('anglais par défaut quand le navigateur est en allemand', async ({ browser, baseURL }) => {
    const context = await browser.newContext({ locale: 'de-DE' });
    const page = await context.newPage();
    await page.goto(baseURL!);
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.locator('#pinLockModal h3')).toHaveText('Terminal locked');
    await context.close();
  });

  test('français quand le navigateur est en français', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('#pinLockModal h3')).toHaveText('Caisse verrouillée');
  });

  test('arabe : RTL, libellé arabe, pavé PIN gauche-droite', async ({ page }) => {
    await page.goto('/');
    await page.locator('#languageSelect').selectOption('ar');
    await page.waitForLoadState('load');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.locator('#pinLockModal h3')).toHaveText('الصندوق مقفل');
    const keypad = page.locator('.pin-keypad');
    await expect(keypad).toHaveAttribute('dir', 'ltr');
    const one = await keypad.locator('[data-val="1"]').boundingBox();
    const three = await keypad.locator('[data-val="3"]').boundingBox();
    expect(one!.x).toBeLessThan(three!.x);
  });

  test('le choix est mémorisé et envoyé au serveur', async ({ page }) => {
    await page.goto('/');
    await page.locator('#languageSelect').selectOption('en');
    await page.waitForLoadState('load');
    const request = page.waitForRequest(r => r.url().includes('/api/'));
    await page.reload();
    expect((await request).headers()['accept-language']).toBe('en');
  });
});
```

Run: `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npx playwright test tests/i18n.spec.ts --project='iPad Pro 11'`
Expected: FAIL (`#languageSelect` absent, `lang="fr"`).

- [ ] **Step 3: Écrire `i18n.js`**

```js
// Runtime i18n : clés sémantiques, repli anglais puis clé. Chargé avant app.js.
(() => {
    const SUPPORTED = ['en', 'fr', 'ar'];
    const stored = (() => { try { return localStorage.getItem('pos_lang'); } catch { return null; } })();
    const browser = (navigator.language || 'en').slice(0, 2).toLowerCase();
    const lang = SUPPORTED.includes(stored) ? stored : SUPPORTED.includes(browser) ? browser : 'en';
    const dir = lang === 'ar' ? 'rtl' : 'ltr';
    let dict = {}, fallback = {};

    document.documentElement.lang = lang;
    document.documentElement.dir = dir;

    const load = l => fetch(`i18n/${l}.json`, { cache: 'no-cache' }).then(r => (r.ok ? r.json() : {})).catch(() => ({}));

    function t(key, params) {
        let text = dict[key] ?? fallback[key];
        if (text === undefined) {
            console.warn(`[i18n] clé absente : ${key}`);
            text = key;
        } else if (dict[key] === undefined && lang !== 'en') {
            console.warn(`[i18n] ${key} absente en ${lang}, repli anglais`);
        }
        if (params) for (const [k, v] of Object.entries(params)) text = text.replaceAll(`{${k}}`, String(v));
        return text;
    }

    const ATTRS = [['data-i18n-placeholder', 'placeholder'], ['data-i18n-aria-label', 'aria-label'], ['data-i18n-title', 'title']];
    function apply(root = document) {
        root.querySelectorAll('[data-i18n]').forEach(el => { el.textContent = t(el.dataset.i18n); });
        for (const [data, attr] of ATTRS) root.querySelectorAll(`[${data}]`).forEach(el => el.setAttribute(attr, t(el.getAttribute(data))));
    }

    function setLanguage(l) {
        try { localStorage.setItem('pos_lang', l); } catch { /* stockage indisponible : choix perdu au rechargement */ }
        location.reload();
    }

    window.t = t;
    window.i18n = { lang, dir, locale: lang === 'ar' ? 'ar-u-nu-latn' : lang, setLanguage, apply };
    window.i18nReady = Promise.all([load(lang), lang === 'en' ? Promise.resolve({}) : load('en')])
        .then(([d, f]) => { dict = d; fallback = lang === 'en' ? d : f; })
        .then(() => new Promise(r => (document.readyState === 'loading' ? document.addEventListener('DOMContentLoaded', r) : r())))
        .then(() => apply());
})();
```

- [ ] **Step 4: Brancher dans `index.html` et `app.js`**

`index.html` : `<html lang="en">` ; avant `app.js` :

```html
    <script src="i18n.js?v=20260927_1"></script>
```

Modal PIN (l.931-963) :

```html
            <div class="pin-header">
                <span class="pin-lock-icon">🔒</span>
                <h3 data-i18n="login.locked_title">Caisse verrouillée</h3>
                <p data-i18n="login.enter_pin">Composez votre code PIN (4-6 chiffres) :</p>
            </div>
            <div class="pin-display" dir="ltr"> … inchangé … </div>
            <div class="pin-keypad" dir="ltr"> … inchangé … </div>
            <label class="pin-language">
                <span data-i18n="login.language">Langue</span>
                <select id="languageSelect" aria-describedby="languageHint">
                    <option value="en">English</option>
                    <option value="fr">Français</option>
                    <option value="ar">العربية</option>
                </select>
            </label>
```

(Noms de langues toujours dans leur propre langue, jamais traduits.)

`app.js` l.2 :

```js
document.addEventListener('DOMContentLoaded', async () => {
    await window.i18nReady;
```

Dans le wrapper `fetch`, juste avant `const res = await originalFetch(resource, config);` :

```js
        if (target.origin === location.origin && target.pathname.startsWith('/api/')) {
            config = config || {};
            config.headers = config.headers || {};
            if (config.headers instanceof Headers) config.headers.set('Accept-Language', window.i18n.lang);
            else if (Array.isArray(config.headers)) config.headers.push(['Accept-Language', window.i18n.lang]);
            else config.headers['Accept-Language'] = window.i18n.lang;
        }
```

Dans `init()`, après `setupPinKeypad();` :

```js
        const languageSelect = document.getElementById('languageSelect');
        languageSelect.value = window.i18n.lang;
        languageSelect.addEventListener('change', e => window.i18n.setLanguage(e.target.value));
```

Dictionnaires initiaux :

`en.json` : `{"login.locked_title":"Terminal locked","login.enter_pin":"Enter your PIN (4-6 digits):","login.language":"Language"}`
`fr.json` : `{"login.locked_title":"Caisse verrouillée","login.enter_pin":"Composez votre code PIN (4-6 chiffres) :","login.language":"Langue"}`
`ar.json` : `{"login.locked_title":"الصندوق مقفل","login.enter_pin":"أدخل رمز PIN (من 4 إلى 6 أرقام):","login.language":"اللغة"}`

- [ ] **Step 5: Écrire `scripts/i18n-check.mjs`**

```js
#!/usr/bin/env node
// Vérifie les dictionnaires web : clés utilisées ↔ clés définies, valeurs vides, français restant.
// Usage : node scripts/i18n-check.mjs [--langs en,fr,ar] [--no-french]
import { readFileSync } from 'node:fs';

const root = new URL('../src/RestaurantPos.Api/wwwroot/', import.meta.url);
const read = p => readFileSync(new URL(p, root), 'utf8');
const args = process.argv.slice(2);
const langs = (args.find(a => a.startsWith('--langs'))?.split('=')[1] ?? args[args.indexOf('--langs') + 1] ?? 'en,fr,ar').split(',');
const js = read('app.js'), html = read('index.html');

const used = new Set();
for (const m of js.matchAll(/\bt\(\s*['"`]([a-z_]+\.[a-z0-9_.]+)['"`]/g)) used.add(m[1]);
for (const m of html.matchAll(/data-i18n(?:-[a-z-]+)?="([a-z_]+\.[a-z0-9_.]+)"/g)) used.add(m[1]);

let errors = 0;
const fail = msg => { errors++; console.error('✘ ' + msg); };

for (const lang of langs) {
  const dict = JSON.parse(read(`i18n/${lang}.json`));
  for (const k of used) if (!(k in dict)) fail(`${lang}: clé manquante ${k}`);
  for (const [k, v] of Object.entries(dict)) {
    if (!used.has(k)) fail(`${lang}: clé orpheline ${k}`);
    if (typeof v !== 'string' || !v.trim()) fail(`${lang}: valeur vide ${k}`);
  }
}

if (args.includes('--no-french')) {
  // Heuristique : chaîne littérale contenant un accent français ou un mot FR courant, hors commentaires.
  const FR = /['"`>][^'"`<]*(?:[éèêàùçôî]|\b(?:le|la|les|des|du|une|pour|avec|introuvable|commande|annuler|valider|enregistrer)\b)[^'"`<]*['"`<]/i;
  const scan = (name, text) => text.split('\n').forEach((line, i) => {
    const code = line.replace(/\/\/.*$/, '').replace(/<!--.*?-->/g, '');
    if (/console\.(warn|error|log)/.test(code)) return;
    if (FR.test(code)) fail(`${name}:${i + 1} français en dur : ${code.trim().slice(0, 100)}`);
  });
  scan('app.js', js);
  scan('index.html', html.replace(/<option value="fr">Français<\/option>/, ''));
}

console.log(errors ? `${errors} problème(s)` : `✔ ${used.size} clés OK (${langs.join(', ')})`);
process.exit(errors ? 1 : 0);
```

- [ ] **Step 6: Lancer**

Run: `node scripts/i18n-check.mjs`
Expected: `✔ 3 clés OK (en, fr, ar)`.

Run (API démarrée sur 5080) : `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npx playwright test --project='iPad Pro 11'`
Expected: `i18n.spec.ts` PASS et toutes les specs existantes PASS (épinglées `fr-FR`).

- [ ] **Step 7: Commit**

```bash
git add src/RestaurantPos.Api/wwwroot/i18n.js src/RestaurantPos.Api/wwwroot/i18n src/RestaurantPos.Api/wwwroot/index.html src/RestaurantPos.Api/wwwroot/app.js scripts/i18n-check.mjs tests/RestaurantPos.Web.E2ETests
git commit -m "feat(web): runtime i18n, sélecteur de langue, Accept-Language, script de vérification"
```

---

### Task 6: Extraire les textes de `index.html`

**Files:**
- Modify: `src/RestaurantPos.Api/wwwroot/index.html`, `wwwroot/i18n/en.json`, `wwwroot/i18n/fr.json`

**Interfaces:**
- Consumes: attributs `data-i18n*`, `window.i18n.apply()` (Task 5).

Procédure pour chaque texte visible (texte de nœud, `placeholder`, `aria-label`, `title`, `<title>`) :
1. Choisir la clé selon la section HTML englobante : header/nav → `common.*`, plan de salle → `floor.*`, ticket/commande/modificateurs → `order.*`, encaissement/split/pourboire → `payment.*`, cuisine → `kitchen.*`, fiscal/Z/X → `fiscal.*`, back-office → `admin.*`, modals génériques (confirmer/annuler/fermer) → `common.*`. Nom = sens en anglais `snake_case` (`order.send_to_kitchen`).
2. Ajouter l'attribut ; garder le texte français dans le HTML (affiché avant `apply`, et lisible dans le source).
3. Ajouter la clé dans `fr.json` (texte actuel exact) et `en.json` (traduction).
4. Éléments dont le texte contient des enfants (icône + texte) : envelopper le texte dans `<span data-i18n="…">`.
5. Ne pas traduire : noms de langues du sélecteur, codes PIN de démo, `€` (sous-projet B), symboles (✕, ⌫, C).

Exemple :

```html
<h3 id="modifiersModalTitle" data-i18n="order.modifiers_title" style="…">Personnalisation & Options</h3>
<div id="modifiersModalSubtitle" data-i18n="order.select_item" style="…">Sélectionnez un article</div>
```

`en.json` : `"order.modifiers_title": "Customization & options", "order.select_item": "Select an item"`.

- [ ] **Step 1: Mesurer l'état initial**

Run: `node scripts/i18n-check.mjs --langs en,fr --no-french 2>&1 | grep -c "index.html"`
Expected: un nombre > 0 (lignes françaises restantes dans `index.html`).

- [ ] **Step 2: Extraire section par section** (header, plan de salle, ticket, paiement, cuisine, fiscal, admin, modals) en appliquant la procédure. Commit intermédiaire possible par section.

- [ ] **Step 3: Vérifier**

Run: `node scripts/i18n-check.mjs --langs en,fr --no-french 2>&1 | grep "index.html" ; node scripts/i18n-check.mjs --langs en,fr`
Expected: aucune ligne `index.html` ; `✔ … clés OK (en, fr)`.

Run: `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npm test`
Expected: PASS (les sélecteurs par texte français fonctionnent car le navigateur est `fr-FR`).

- [ ] **Step 4: Commit**

```bash
git add src/RestaurantPos.Api/wwwroot/index.html src/RestaurantPos.Api/wwwroot/i18n
git commit -m "feat(web): textes de index.html en clés i18n (en/fr)"
```

---

### Task 7: Extraire les textes de `app.js` — prise de commande, salle, paiement (l.1-2600)

**Files:**
- Modify: `src/RestaurantPos.Api/wwwroot/app.js` (l.1-2600), `wwwroot/i18n/en.json`, `wwwroot/i18n/fr.json`

**Interfaces:**
- Consumes: `t(key, params)`, `window.i18n.locale` (Task 5).

Procédure :
1. Toute chaîne affichée (template literal injecté dans le DOM, `textContent`, `showToast(...)`, `confirm(...)`, `alert(...)`, libellés de boutons générés) → `t('zone.nom')`. Interpolations → placeholders :
   ```js
   // avant
   showToast(`Table ${tableNumber} transférée vers ${target}`, 'success');
   // après
   showToast(t('floor.table_transferred', { from: tableNumber, to: target }), 'success');
   ```
   `fr.json` : `"floor.table_transferred": "Table {from} transférée vers {to}"` ; `en.json` : `"Table {from} moved to {to}"`.
2. Messages d'erreur venant du serveur (`body.message`, `body.Message`, `errorMessage`) : afficher tels quels (déjà traduits par le serveur via `Accept-Language`).
3. Dates/nombres : `toLocaleString()` / `toLocaleDateString()` / `toLocaleTimeString()` sans argument → passer `window.i18n.locale` ; `Intl.NumberFormat('fr-FR', …)` → `Intl.NumberFormat(window.i18n.locale, …)`. Ne pas toucher au symbole `€` (sous-projet B).
4. Ne pas traduire : `console.*`, clés d'API, valeurs d'enum envoyées au serveur (`'Takeaway'`, `'EatIn'`), noms de classes CSS, données catalogue.
5. Chaînes dans des attributs de HTML généré (`title="…"`, `placeholder="…"`) → `${t('…')}`.

- [ ] **Step 1: Mesurer**

Run: `node scripts/i18n-check.mjs --langs en,fr --no-french 2>&1 | awk -F: '/app.js/ && $2<=2600' | wc -l`
Expected: > 0.

- [ ] **Step 2: Extraire** fonction par fonction (plan de salle, catalogue/grille, panier, modificateurs, remises/gratuités, envoi cuisine, comptoir/attente, encaissement, split, pourboires, chambre d'hôtel, signature), en appliquant la procédure.

- [ ] **Step 3: Vérifier**

Run: `node scripts/i18n-check.mjs --langs en,fr --no-french 2>&1 | awk -F: '/app.js/ && $2<=2600' ; node scripts/i18n-check.mjs --langs en,fr`
Expected: aucune ligne ; `✔`.

Run: `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npm test`
Expected: PASS.

Contrôle manuel : navigateur en anglais (`localStorage.pos_lang='en'`), parcourir commande → envoi cuisine → encaissement : aucun texte français (hors données catalogue).

- [ ] **Step 4: Commit**

```bash
git add src/RestaurantPos.Api/wwwroot/app.js src/RestaurantPos.Api/wwwroot/i18n
git commit -m "feat(web): textes salle/commande/paiement en clés i18n (en/fr)"
```

---

### Task 8: Extraire les textes de `app.js` — back-office, cuisine, fiscal (l.2600-fin)

**Files:**
- Modify: `src/RestaurantPos.Api/wwwroot/app.js` (l.2600-fin), `wwwroot/i18n/en.json`, `wwwroot/i18n/fr.json`

**Interfaces:**
- Consumes: `t`, `window.i18n.locale` (Task 5). Même procédure que Task 7 (répétée ici) :
  1. Chaîne affichée → `t('zone.nom', params)` ; zones `admin.*`, `kitchen.*`, `fiscal.*`, `common.*`.
  2. Messages serveur affichés tels quels.
  3. `toLocaleString()` etc. → `window.i18n.locale` ; exemple l.3864 :
     ```js
     ${d.isRevoked ? t('admin.device_revoked') : (d.lastSeenUtc ? t('admin.device_last_seen', { date: new Date(d.lastSeenUtc).toLocaleString(window.i18n.locale) }) : t('admin.device_never_used'))}
     ```
  4. Ne pas traduire logs, enums API, classes CSS, données.
  5. Attributs HTML générés → `${t('…')}`.
  6. Libellés de rôles (`Caisse`, `Serveur`, `Cuisine`, `BackOffice`) : ce sont des valeurs d'enum envoyées au serveur ; garder la valeur, traduire seulement l'affichage via `t('admin.role_caisse')` etc.

- [ ] **Step 1: Mesurer**

Run: `node scripts/i18n-check.mjs --langs en,fr --no-french 2>&1 | grep -c app.js`
Expected: > 0.

- [ ] **Step 2: Extraire** (tableau de bord, catalogue, grille, équipe, imprimantes, appareils, Happy Hour, cuisine/KDS, X/Z, FEC, audit).

- [ ] **Step 3: Vérifier**

Run: `node scripts/i18n-check.mjs --langs en,fr --no-french && node scripts/i18n-check.mjs --langs en,fr`
Expected: `✔` deux fois (plus aucun français en dur dans `app.js` ni `index.html`). Faux positifs de l'heuristique : ajuster la regex du script dans ce même commit, jamais ignorer une vraie chaîne.

Run: `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npm test`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/RestaurantPos.Api/wwwroot/app.js src/RestaurantPos.Api/wwwroot/i18n scripts/i18n-check.mjs
git commit -m "feat(web): textes back-office/cuisine/fiscal en clés i18n (en/fr)"
```

---

### Task 9: RTL web

**Files:**
- Modify: `src/RestaurantPos.Api/wwwroot/styles.css`, `wwwroot/app.js`, `wwwroot/index.html`
- Test: `tests/RestaurantPos.Web.E2ETests/tests/i18n.spec.ts`

**Interfaces:**
- Consumes: `<html dir>` (Task 5).

- [ ] **Step 1: Tests RTL (échouent)**

Ajouter à `i18n.spec.ts` :

```ts
  test('arabe : montants et numéros restent gauche-droite, icônes retour inversées', async ({ page }) => {
    await page.goto('/');
    await page.evaluate(() => localStorage.setItem('pos_lang', 'ar'));
    await page.reload();
    // login démo manager
    for (const d of '1234') await page.locator(`.pin-keypad [data-val="${d}"]`).click();
    await expect(page.locator('#pinLockModal')).not.toHaveClass(/active/);
    const amounts = page.locator('[data-amount], .price, .cart-total');
    const count = await amounts.count();
    for (let i = 0; i < Math.min(count, 5); i++) {
      await expect(amounts.nth(i)).toHaveCSS('direction', 'ltr');
    }
    const panel = await page.locator('.app-header').boundingBox();
    const brand = await page.locator('.brand-section').boundingBox();
    expect(brand!.x + brand!.width).toBeGreaterThan(panel!.x + panel!.width / 2); // logo à droite en RTL
  });
```

Adapter les sélecteurs de montants aux classes réellement utilisées dans `index.html`/`app.js` (les relever avant d'écrire le test).

Run: `npx playwright test tests/i18n.spec.ts --project='iPad Pro 11'`
Expected: FAIL.

- [ ] **Step 2: Passer en propriétés logiques**

Run: `grep -nE "(margin|padding|border)-(left|right)|(^|[^-])(left|right):|text-align: *(left|right)|float: *(left|right)" src/RestaurantPos.Api/wwwroot/styles.css`

Remplacements :

| avant | après |
|---|---|
| `margin-left` / `margin-right` | `margin-inline-start` / `margin-inline-end` |
| `padding-left` / `padding-right` | `padding-inline-start` / `padding-inline-end` |
| `border-left` / `border-right` | `border-inline-start` / `border-inline-end` |
| `left:` / `right:` (positionné) | `inset-inline-start:` / `inset-inline-end:` |
| `text-align: left` / `right` | `text-align: start` / `end` |
| `float: left` / `right` | `float: inline-start` / `inline-end` |

Même traitement pour les styles inline : `grep -nE "style=\"[^\"]*(left|right)" src/RestaurantPos.Api/wwwroot/index.html src/RestaurantPos.Api/wwwroot/app.js`.

Exceptions : `transform: translateX(...)` d'animations de tiroir → ajouter une règle `[dir="rtl"]` qui inverse le signe.

- [ ] **Step 3: Exceptions LTR et icônes**

`styles.css` :

```css
/* Montants, PIN, numéros de ticket et pavés numériques : toujours gauche-droite. */
.pin-display, .pin-keypad, .numpad, .price, .cart-total, [data-amount], .ticket-number { direction: ltr; unicode-bidi: isolate; }
/* Icônes directionnelles miroir en RTL. */
[dir="rtl"] .icon-directional { transform: scaleX(-1); }
```

Relever les vrais noms de classes (pavés numériques de paiement/split, montants du panier, numéros de ticket) et les lister ici ; ajouter `class="icon-directional"` aux flèches retour/chevrons (`←`, `→`, `‹`, `›`) de `index.html`/`app.js`.

- [ ] **Step 4: Vérifier**

Run: `npx playwright test --project='iPad Pro 11'`
Expected: PASS (dont les specs françaises, qui ne doivent pas bouger en LTR).

Contrôle visuel : captures en `ar` de la salle, du ticket, du paiement, du back-office (`npx playwright test tests/i18n.spec.ts --project='iPad Pro 11' --headed` ou `page.screenshot`) ; aucun chevauchement, aucun texte coupé.

- [ ] **Step 5: Commit**

```bash
git add src/RestaurantPos.Api/wwwroot tests/RestaurantPos.Web.E2ETests/tests/i18n.spec.ts
git commit -m "feat(web): mise en page RTL (propriétés logiques, exceptions LTR)"
```

---

### Task 10: iOS PosKit — String Catalog, `Accept-Language`, `Money`, réglages

**Files:**
- Modify: `ios/Packages/PosKit/Package.swift` (`defaultLocalization: "en"`, `resources: [.process("Resources")]`)
- Create: `ios/Packages/PosKit/Sources/PosKit/Resources/Localizable.xcstrings`
- Create: `ios/Packages/PosKit/Sources/PosKit/Core/L10n.swift`
- Modify: `Networking/HTTPPosAPI.swift:35-52`, `Networking/PosAPI.swift`, `Testing/InMemoryPosAPI.swift`, `Core/Money.swift:59-73`, `Models/OperationsModels.swift`, `Stores/AdminStores.swift`, `Stores/AppModel.swift:51`, et toute chaîne affichée dans `Sources/PosKit/**` (notifier, `errorDescription`)
- Test: `ios/Packages/PosKit/Tests/PosKitTests/LocalizationTests.swift`, `NetworkingTests.swift`, `ContractDecodingTests.swift`, `MoneyAndMathTests.swift:13-17`

**Interfaces:**
- Consumes: `GET/PUT /api/settings` (Task 3), fixture `settings.json`.
- Produces :
  - `public struct RestaurantSettings: Codable, Hashable, Sendable { public var receiptLanguage: String }`
  - `PosAPI.settings() async throws -> RestaurantSettings`, `PosAPI.saveSettings(_ s: RestaurantSettings) async throws -> RestaurantSettings`
  - `@MainActor @Observable public final class SettingsStore { public private(set) var settings: RestaurantSettings?; public func load() async; public func setReceiptLanguage(_ lang: String) async }` exposé comme `AppModel.settingsStore` (le nom `settings` est déjà pris par `model.settings.mealVoucherPolicy` : le vérifier dans `AppModel.swift`).
  - `Money.formatted(locale: Locale = .current) -> String` ; `Money.formatted` (propriété) conserve son API et appelle `formatted(locale: .current)`.
  - `L10n.string(_ key: String) -> String` interne à PosKit : `String(localized: String.LocalizationValue(key), bundle: .module)`.

- [ ] **Step 1: Tests (échouent)**

`LocalizationTests.swift` :

```swift
import Foundation
import Testing
@testable import PosKit

@Suite("Localisation PosKit")
struct LocalizationTests {
    struct Catalog: Decodable {
        struct Entry: Decodable { let localizations: [String: Localization]? }
        struct Localization: Decodable { let stringUnit: StringUnit? }
        struct StringUnit: Decodable { let state: String; let value: String }
        let sourceLanguage: String
        let strings: [String: Entry]
    }

    static func catalog() throws -> Catalog {
        let url = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("Sources/PosKit/Resources/Localizable.xcstrings")
        return try JSONDecoder().decode(Catalog.self, from: Data(contentsOf: url))
    }

    @Test func sourceLanguageIsEnglish() throws {
        #expect(try Self.catalog().sourceLanguage == "en")
    }

    @Test(arguments: ["en", "fr"])
    func everyKeyIsTranslated(language: String) throws {
        let catalog = try Self.catalog()
        #expect(!catalog.strings.isEmpty)
        for (key, entry) in catalog.strings {
            let unit = entry.localizations?[language]?.stringUnit
            #expect(unit?.state == "translated", "\(language) : \(key)")
            #expect(!(unit?.value.isEmpty ?? true), "\(language) : \(key)")
        }
    }

    @Test func keysAreSemantic() throws {
        for key in try Self.catalog().strings.keys {
            #expect(key.range(of: #"^[a-z]+\.[a-z0-9_.]+$"#, options: .regularExpression) != nil, "clé non sémantique : \(key)")
        }
    }
}
```

(`"ar"` est ajouté aux arguments dans Task 12.)

`NetworkingTests.swift`, dans `HTTPPosAPITests` :

```swift
    @Test func sendsAcceptLanguage() async throws {
        let api = makeAPI { _ in .init(status: 200, body: #"{"receiptLanguage":"fr"}"#) }
        _ = try await api.settings()
        let header = try #require(last.value(forHTTPHeaderField: "Accept-Language"))
        #expect(["en", "fr", "ar"].contains(header))
    }

    @Test func savesSettings() async throws {
        let api = makeAPI { _ in .init(status: 200, body: #"{"receiptLanguage":"ar"}"#) }
        let saved = try await api.saveSettings(RestaurantSettings(receiptLanguage: "ar"))
        #expect(last.httpMethod == "PUT")
        #expect(last.url?.path == "/api/settings")
        #expect(try body(last)["receiptLanguage"] as? String == "ar")
        #expect(saved.receiptLanguage == "ar")
    }
```

`ContractDecodingTests.swift` :

```swift
    @Test func settings() throws {
        let settings = try decode(RestaurantSettings.self, "settings")
        #expect(["en", "fr", "ar"].contains(settings.receiptLanguage))
    }
```

`MoneyAndMathTests.swift:13-17` → la locale devient explicite :

```swift
    @Test func formatsInFrench() {
        let text = Money(cents: 1950).formatted(locale: Locale(identifier: "fr_FR"))
        #expect(text.contains("19,50"))
        #expect(text.contains("€"))
    }

    @Test func formatsWithWesternDigitsInArabic() {
        let text = Money(cents: 1950).formatted(locale: Locale(identifier: "ar"))
        #expect(text.contains("19"))
        #expect(!text.contains("١"))
    }
```

Run: `cd ios/Packages/PosKit && swift test`
Expected: FAIL compilation.

- [ ] **Step 2: Implémenter**

`Core/L10n.swift` :

```swift
import Foundation

/// Textes affichés par PosKit (notifications, erreurs), par clé sémantique ; repli anglais géré par le bundle.
enum L10n {
    static func string(_ key: String) -> String {
        String(localized: String.LocalizationValue(key), bundle: .module)
    }
}
```

`Package.swift` :

```swift
    defaultLocalization: "en",
    …
        .target(name: "PosKit", resources: [.process("Resources")]),
```

`HTTPPosAPI.makeRequest`, après l'en-tête `Accept` :

```swift
        request.setValue(Self.acceptLanguage, forHTTPHeaderField: "Accept-Language")
```

et :

```swift
    /// Langue de l'app choisie par iOS parmi celles qu'elle déclare (réglage par app).
    static var acceptLanguage: String {
        let lang = Bundle.main.preferredLocalizations.first.map { String($0.prefix(2)) } ?? "en"
        return ["en", "fr", "ar"].contains(lang) ? lang : "en"
    }
```

`Money.swift` :

```swift
    /// « 19,50 € » dans la locale donnée, chiffres occidentaux forcés.
    public func formatted(locale: Locale = .current) -> String {
        let f = NumberFormatter()
        f.numberStyle = .currency
        f.currencyCode = "EUR" // devise : sous-projet B
        f.locale = Locale(identifier: locale.identifier + "@numbers=latn")
        f.minimumFractionDigits = 2
        f.maximumFractionDigits = 2
        return f.string(from: euros as NSDecimalNumber) ?? "\(euros) €"
    }

    public var formatted: String { formatted(locale: .current) }
```

Supprimer l'ancien `private static let formatter` (devenu inutilisé).

`OperationsModels.swift` : ajouter `RestaurantSettings` (init public). `PosAPI.swift` : les deux méthodes. `HTTPPosAPI.swift` :

```swift
    public func settings() async throws -> RestaurantSettings { try await call("GET", "settings") }
    public func saveSettings(_ s: RestaurantSettings) async throws -> RestaurantSettings { try await call("PUT", "settings", body: s) }
```

`InMemoryPosAPI.swift` : `private var settingsStore = RestaurantSettings(receiptLanguage: "fr")` + implémentations (`try await step("settings")`, validation `["en","fr","ar"]` sinon `throw APIError.badRequest(...)` — utiliser le cas d'erreur réellement défini dans `APIError`).

`AdminStores.swift` : `SettingsStore` sur le modèle de `PrinterStore` (`load()` → `notifier.error` en cas d'échec ; `setReceiptLanguage` → `saveSettings` puis `notifier.success(L10n.string("admin.receipt_language_saved"))`). `AppModel.swift` : `settingsStore = SettingsStore(api: api, notifier: notifier)`.

- [ ] **Step 3: Extraire les chaînes de PosKit**

Lister : `grep -rnE "\"[^\"]*[A-Za-zÀ-ÿ]{3,}[^\"]*\"" ios/Packages/PosKit/Sources --include='*.swift' | grep -E "notifier\.|errorDescription|return \"|case .*: \""`.
Chaque texte affiché → `L10n.string("zone.nom")` (ou `String(localized: "zone.nom", defaultValue: …)` si interpolation : utiliser `String(format: L10n.string("admin.printer_saved"), printer.name)` avec `%@` dans le catalogue). Exemple :

```swift
        guard !printer.name.trimmingCharacters(in: .whitespaces).isEmpty else { notifier.warning(L10n.string("admin.name_required")); return false }
        guard Self.isValidIPv4(printer.ipAddress) else { notifier.warning(L10n.string("admin.ip_invalid")); return false }
            notifier.success(String(format: L10n.string("admin.printer_saved"), printer.name))
```

`Localizable.xcstrings` (source `en`, `version: "1.0"`), chaque clé avec `en` et `fr` en `"state": "translated"`, ex. :

```json
{
  "sourceLanguage" : "en",
  "strings" : {
    "admin.name_required" : { "localizations" : {
      "en" : { "stringUnit" : { "state" : "translated", "value" : "Name is required" } },
      "fr" : { "stringUnit" : { "state" : "translated", "value" : "Le nom est requis" } } } }
  },
  "version" : "1.0"
}
```

Si `swift build` en ligne de commande ne compile pas `.xcstrings` (vérifier : `swift build` puis `find .build -name "*.strings" -path "*fr.lproj*"`), le noter et basculer PosKit vers `Resources/en.lproj/Localizable.strings` + `fr.lproj` + `ar.lproj`, en adaptant `LocalizationTests` pour lire ces fichiers ; l'app garde son `.xcstrings`.

- [ ] **Step 4: Tests**

Run: `cd ios/Packages/PosKit && swift test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(ios): PosKit localisé (xcstrings), Accept-Language, Money par locale, réglages restaurant"
```

---

### Task 11: iOS app — String Catalog, extraction, réglage langue des tickets, RTL

**Files:**
- Modify: `ios/project.yml` (`developmentLanguage: en`, `CFBundleDevelopmentRegion: en`, `CFBundleLocalizations: [en, fr, ar]`)
- Create: `ios/RestaurantPOS/Resources/Localizable.xcstrings`
- Modify: toutes les vues `ios/RestaurantPOS/**/*.swift`
- Modify: `ios/RestaurantPOS/Features/Admin/AdminScreen.swift` (section réglages)
- Modify: `ios/RestaurantPOSUITests/PosUITestCase.swift:28-35`
- Create: `ios/RestaurantPOSUITests/LocalizationUITests.swift`

**Interfaces:**
- Consumes: `SettingsStore` (`model.settingsStore`), `Money.formatted`, `L10n` n'est pas public : l'app utilise `Text("zone.nom")` / `String(localized: "zone.nom")` sur le bundle principal.

- [ ] **Step 1: Épingler les UI tests existants en français et écrire le test arabe (échoue)**

`PosUITestCase.launch` : ajouter un paramètre `language: String = "fr"` et

```swift
        app.launchArguments += ["-AppleLanguages", "(\(language))", "-AppleLocale", language == "ar" ? "ar" : "\(language)_FR"]
```

`LocalizationUITests.swift` :

```swift
import XCTest

final class LocalizationUITests: PosUITestCase {
    func testArabicShowsArabicLabelsAndKeepsKeypadLeftToRight() {
        let app = launch(pin: nil, language: "ar")
        XCTAssertTrue(app.staticTexts["الصندوق مقفل"].waitForExistence(timeout: 5))
        let one = app.buttons["pin.key.1"].frame
        let three = app.buttons["pin.key.3"].frame
        XCTAssertLessThan(one.minX, three.minX)
    }

    func testEnglish() {
        let app = launch(pin: nil, language: "en")
        XCTAssertTrue(app.staticTexts["Terminal locked"].waitForExistence(timeout: 5))
    }
}
```

Relever les `accessibilityIdentifier` réels des touches PIN dans l'écran de login (sinon en ajouter : `pin.key.<chiffre>`) et le titre réel de l'écran verrouillé.

Run: `cd ios && ./scripts/test.sh ui`
Expected: `LocalizationUITests` FAIL ; les autres PASS.

- [ ] **Step 2: `project.yml` + catalogue**

`options.developmentLanguage: en` ; `info.properties.CFBundleDevelopmentRegion: en` ; ajouter `CFBundleLocalizations: [en, fr, ar]`. Créer `Localizable.xcstrings` (source `en`) dans `RestaurantPOS/Resources`. `cd ios && xcodegen generate`.

- [ ] **Step 3: Extraire les chaînes des vues**

Lister : `grep -rnE "(Text|Label|Button|Toggle|TextField|Section|navigationTitle|alert|confirmationDialog)\(\"" ios/RestaurantPOS`.
Règles :
- `Text("Encaisser")` → `Text("payment.submit")` ; catalogue : `en` « Pay », `fr` « Encaisser ».
- Interpolations : `Text("order.items_count \(count)")` → clé catalogue `order.items_count %lld` (Xcode) ; valeurs `en`/`fr` avec `%lld`.
- `String` non-`Text` (ex. `var title: String` de `AdminScreen.Section`) → `String(localized: "admin.section_dashboard")`.
- Noms de langues du sélecteur : jamais traduits.
- Montants : `.environment(\.layoutDirection, .leftToRight)` sur les pavés numériques (PIN, paiement, split) et les `Text(money.formatted)` de totaux.

- [ ] **Step 4: Réglage « Langue des tickets » dans Admin**

Dans `AdminScreen.swift`, section `.network` (vue `NetworkSettingsView`), ajouter :

```swift
struct ReceiptLanguagePicker: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        Picker("admin.receipt_language", selection: Binding(
            get: { model.settingsStore.settings?.receiptLanguage ?? "en" },
            set: { lang in Task { await model.settingsStore.setReceiptLanguage(lang) } }
        )) {
            Text(verbatim: "English").tag("en")
            Text(verbatim: "Français").tag("fr")
            Text(verbatim: "العربية").tag("ar")
        }
        .accessibilityIdentifier("admin.receiptLanguage")
        .task { await model.settingsStore.load() }
    }
}
```

Vérifier comment les autres vues admin obtiennent `AppModel` (`@Environment` ou autre) et s'aligner. Ajouter la phrase d'aide sous le picker (`admin.receipt_language_hint`) : en « Printers that do not support Arabic must stay in English or French. » / fr « Une imprimante qui ne gère pas l'arabe doit rester en anglais ou en français. ».

Test UI dans `OperationsUITests.swift` : section admin → `admin.receiptLanguage` → choisir « العربية » → toast de succès visible.

- [ ] **Step 5: Vérifier**

Run: `cd ios && ./scripts/test.sh unit && ./scripts/test.sh ui`
Expected: PASS (tests existants en français inchangés, `LocalizationUITests` PASS).

Contrôle : `grep -rnE "(Text|Label|Button|Toggle)\(\"[^\"]*[A-ZÀ-ÿ][^\"]* " ios/RestaurantPOS | grep -v verbatim` → aucune chaîne française restante.

- [ ] **Step 6: Commit**

```bash
git add ios/project.yml ios/RestaurantPOS ios/RestaurantPOSUITests
git commit -m "feat(ios): app localisée (xcstrings), réglage langue des tickets, RTL"
```

---

### Task 12: Traductions arabes complètes + vérifications finales

**Files:**
- Modify: `src/RestaurantPos.Api/wwwroot/i18n/ar.json`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Resources/Localizable.xcstrings`, `ios/RestaurantPOS/Resources/Localizable.xcstrings`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/LocalizationTests.swift` (arguments `["en", "fr", "ar"]`)
- Create: `docs/i18n.md` (convention de clés + commandes de vérification, 1 page)

**Interfaces:**
- Consumes: toutes les clés des Tasks 5-11. (Serveur déjà complet en `ar` depuis Tasks 1-4.)

- [ ] **Step 1: Rendre les vérifications strictes (échouent)**

`LocalizationTests.everyKeyIsTranslated` : `arguments: ["en", "fr", "ar"]`.
Ajouter dans `LocalizationTests.swift` (PosKit) un second test qui lit le catalogue de l'app, `ios/RestaurantPOS/Resources/Localizable.xcstrings`, par chemin relatif à `#filePath` (remonter de 5 niveaux jusqu'à `ios/`) et applique les mêmes vérifications `en`/`fr`/`ar`.

Run: `node scripts/i18n-check.mjs` (toutes langues) ; `cd ios/Packages/PosKit && swift test --filter LocalizationTests`
Expected: FAIL (clés `ar` manquantes).

- [ ] **Step 2: Traduire en arabe standard moderne**

Règles : terminologie restauration courante au Maghreb et au Moyen-Orient (« طاولة » table, « طلب » commande, « دفع » paiement, « سفري » à emporter, « المطبخ » cuisine, « الصندوق » caisse) ; placeholders `{x}` / `%@` / `%lld` conservés à l'identique ; sigles (PIN, TVA/VAT, NF525, Z, X, FEC, Happy Hour) laissés en latin ; ponctuation arabe (« ، » « ؟ »).

- [ ] **Step 3: Vérifier**

Run: `node scripts/i18n-check.mjs --no-french`
Expected: `✔ … clés OK (en, fr, ar)`.

Run: `dotnet test RestaurantPos.slnx`
Expected: PASS.

Run: `cd ios && ./scripts/test.sh unit && ./scripts/test.sh ui`
Expected: PASS.

Run (API sur 5080) : `cd tests/RestaurantPos.Web.E2ETests && POS_WEB_URL=http://localhost:5080 npm run test:all`
Expected: PASS.

Run : `POS_API_URL=http://localhost:5080 ./ios/scripts/test.sh contract`
Expected: PASS.

- [ ] **Step 4: `docs/i18n.md`**

Contenu : convention `zone.nom` et liste des zones ; où ajouter une clé (web `i18n/*.json`, iOS `.xcstrings`, serveur `.resx`) ; repli anglais ; commandes `node scripts/i18n-check.mjs --no-french`, tests `TextsTests`, `LocalizationTests` ; relecture arabophone obligatoire avant mise en production.

- [ ] **Step 5: Commit**

```bash
git add src/RestaurantPos.Api/wwwroot/i18n ios docs/i18n.md
git commit -m "feat(i18n): traductions arabes complètes et vérifications strictes"
```

Rappel au livreur : la version arabe doit être relue par un arabophone avant la mise en production (spec, section Traductions).
