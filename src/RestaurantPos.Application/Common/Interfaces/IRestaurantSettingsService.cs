using System;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Application.Common.Interfaces;

public record RestaurantSettingsDto(
    string ReceiptLanguage,
    string KitchenTicketLanguage,
    string CompanyName,
    string AddressLines,
    string Siret,
    string VatNumber,
    string? CertificateNumber,
    int FiscalYearStartMonth,
    int FiscalYearStartDay
);

/// <param name="KitchenTicketLanguage">null = inchangé (clients antérieurs au sous-projet C).</param>
public record UpdateRestaurantSettingsRequest(
    string? ReceiptLanguage = null,
    string? KitchenTicketLanguage = null,
    string? CompanyName = null,
    string? AddressLines = null,
    string? Siret = null,
    string? VatNumber = null,
    string? CertificateNumber = null,
    int? FiscalYearStartMonth = null,
    int? FiscalYearStartDay = null
);

public enum UpdateSettingsStatus
{
    Success,
    InvalidLanguage,
    InvalidSiret,
    InvalidVat,
    InvalidFiscalDate,
    FiscalYearLocked
}

public record UpdateRestaurantSettingsResult(UpdateSettingsStatus Status, RestaurantSettingsDto? Settings = null);

public interface IRestaurantSettingsService
{
    Task<RestaurantSettingsDto> GetAsync(CancellationToken ct = default);
    Task<UpdateRestaurantSettingsResult> UpdateAsync(UpdateRestaurantSettingsRequest request, Guid? operatorId = null, CancellationToken ct = default);
}

