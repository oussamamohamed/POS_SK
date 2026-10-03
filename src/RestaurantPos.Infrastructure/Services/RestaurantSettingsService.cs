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
    private readonly IFiscalJournal _fiscalJournal;

    public RestaurantSettingsService(AppDbContext db, IFiscalJournal? fiscalJournal = null)
    {
        _db = db;
        _fiscalJournal = fiscalJournal ?? new FiscalJournalService(db);
    }

    public async Task<RestaurantSettingsDto> GetAsync(CancellationToken ct = default) =>
        ToDto(await LoadOrCreateAsync(ct).ConfigureAwait(false));

    public async Task<UpdateRestaurantSettingsResult> UpdateAsync(
        UpdateRestaurantSettingsRequest request,
        Guid? operatorId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Language validation
        if (request.ReceiptLanguage is not null && !IsSupported(request.ReceiptLanguage))
            return new UpdateRestaurantSettingsResult(UpdateSettingsStatus.InvalidLanguage);

        if (request.KitchenTicketLanguage is not null && !IsSupported(request.KitchenTicketLanguage))
            return new UpdateRestaurantSettingsResult(UpdateSettingsStatus.InvalidLanguage);

        // 2. SIRET validation: 14 digits
        if (request.Siret is not null)
        {
            var cleanSiret = request.Siret.Replace(" ", "");
            if (cleanSiret.Length != 14 || !cleanSiret.All(char.IsAsciiDigit))
                return new UpdateRestaurantSettingsResult(UpdateSettingsStatus.InvalidSiret);
        }

        // 3. VAT validation: FR + 11 alphanumeric characters
        if (request.VatNumber is not null)
        {
            var cleanVat = request.VatNumber.Replace(" ", "");
            if (!cleanVat.StartsWith("FR", StringComparison.OrdinalIgnoreCase)
                || cleanVat.Length != 13
                || !cleanVat[2..].All(char.IsLetterOrDigit))
                return new UpdateRestaurantSettingsResult(UpdateSettingsStatus.InvalidVat);
        }

        // 4. Fiscal year dates validation
        if (request.FiscalYearStartMonth is { } m && (m < 1 || m > 12))
            return new UpdateRestaurantSettingsResult(UpdateSettingsStatus.InvalidFiscalDate);

        if (request.FiscalYearStartDay is { } d && (d < 1 || d > 28))
            return new UpdateRestaurantSettingsResult(UpdateSettingsStatus.InvalidFiscalDate);

        var settings = await LoadOrCreateAsync(ct).ConfigureAwait(false);

        // 5. Fiscal year lock check: if month/day changed, check if any yearly period closure exists
        var monthChanging = request.FiscalYearStartMonth.HasValue && request.FiscalYearStartMonth.Value != settings.FiscalYearStartMonth;
        var dayChanging = request.FiscalYearStartDay.HasValue && request.FiscalYearStartDay.Value != settings.FiscalYearStartDay;

        if (monthChanging || dayChanging)
        {
            var hasAnnualClosure = await _db.PeriodClosures
                .AnyAsync(p => p.PeriodType == FiscalPeriodType.Annual, ct)
                .ConfigureAwait(false);

            if (hasAnnualClosure)
                return new UpdateRestaurantSettingsResult(UpdateSettingsStatus.FiscalYearLocked);
        }

        // 6. Apply updates
        if (request.ReceiptLanguage is not null) settings.ReceiptLanguage = request.ReceiptLanguage;
        if (request.KitchenTicketLanguage is not null) settings.KitchenTicketLanguage = request.KitchenTicketLanguage;
        if (request.CompanyName is not null) settings.CompanyName = request.CompanyName.Trim();
        if (request.AddressLines is not null) settings.AddressLines = request.AddressLines;
        if (request.Siret is not null) settings.Siret = request.Siret.Replace(" ", "");
        if (request.VatNumber is not null) settings.VatNumber = request.VatNumber.Replace(" ", "").ToUpperInvariant();
        if (request.CertificateNumber is not null) settings.CertificateNumber = string.IsNullOrWhiteSpace(request.CertificateNumber) ? null : request.CertificateNumber.Trim();
        if (request.FiscalYearStartMonth.HasValue) settings.FiscalYearStartMonth = request.FiscalYearStartMonth.Value;
        if (request.FiscalYearStartDay.HasValue) settings.FiscalYearStartDay = request.FiscalYearStartDay.Value;
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var dto = ToDto(settings);

        // 7. Réglages + entrée JET dans la même transaction : pas de changement fiscal sans trace d'audit.
        await _db.ExecuteInTransactionAsync(async txCt =>
        {
            await _db.SaveChangesAsync(txCt).ConfigureAwait(false);
            await _fiscalJournal.AppendAsync(
                JournalEventTypes.FiscalSettingsChanged,
                dto,
                terminalId: null,
                operatorId: operatorId,
                cancellationToken: txCt).ConfigureAwait(false);
            return true;
        }, System.Data.IsolationLevel.Serializable, ct).ConfigureAwait(false);

        return new UpdateRestaurantSettingsResult(UpdateSettingsStatus.Success, dto);
    }

    private static bool IsSupported(string language) => Texts.SupportedLanguages.Contains(language, StringComparer.Ordinal);
    private static RestaurantSettingsDto ToDto(RestaurantSettings s) => new(
        s.ReceiptLanguage,
        s.KitchenTicketLanguage,
        s.CompanyName,
        s.AddressLines,
        s.Siret,
        s.VatNumber,
        s.CertificateNumber,
        s.FiscalYearStartMonth,
        s.FiscalYearStartDay
    );

    // Première lecture : une base qui a déjà des commandes est une installation française existante.
    private async Task<RestaurantSettings> LoadOrCreateAsync(CancellationToken ct)
    {
        var settings = await _db.RestaurantSettings.FindAsync([RestaurantSettings.SingletonId], ct).ConfigureAwait(false);
        if (settings is not null) return settings;
        var hasOrders = await _db.Orders.AnyAsync(ct).ConfigureAwait(false);
        var lang = hasOrders ? "fr" : "en";
        settings = new RestaurantSettings { ReceiptLanguage = lang, KitchenTicketLanguage = lang };
        _db.RestaurantSettings.Add(settings);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Course sur le premier appel (deux terminaux) : un autre a déjà inséré la ligne singleton.
            // On détache notre tentative locale et on relit la ligne gagnante.
            _db.Entry(settings).State = EntityState.Detached;
            var winner = await _db.RestaurantSettings.FindAsync([RestaurantSettings.SingletonId], ct).ConfigureAwait(false);
            if (winner is null) throw; // Pas un conflit sur la ligne singleton : ne pas masquer l'erreur d'origine.
            settings = winner;
        }
        return settings;
    }
}
