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
