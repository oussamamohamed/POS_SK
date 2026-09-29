using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Application.Common.Interfaces;

public record RestaurantSettingsDto(string ReceiptLanguage, string KitchenTicketLanguage);
/// <param name="KitchenTicketLanguage">null = inchangé (clients antérieurs au sous-projet C).</param>
public record UpdateRestaurantSettingsRequest(string ReceiptLanguage, string? KitchenTicketLanguage = null);

public interface IRestaurantSettingsService
{
    Task<RestaurantSettingsDto> GetAsync(CancellationToken ct = default);
    /// <returns>null si la langue n'est pas supportée.</returns>
    Task<RestaurantSettingsDto?> UpdateAsync(UpdateRestaurantSettingsRequest request, CancellationToken ct = default);
}
