using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class TerminalLayoutService : ITerminalLayoutService
{
    private readonly AppDbContext _dbContext;

    public TerminalLayoutService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TerminalLayoutProfile> GetActiveProfileAsync(string terminalId, CancellationToken ct = default)
    {
        var profile = await _dbContext.TerminalLayoutProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsDefault, ct)
            .ConfigureAwait(false);

        if (profile is not null) return profile;

        var defaultProfile = new TerminalLayoutProfile
        {
            Id = UuidV7.NewGuid(),
            ProfileName = "Profil Standard",
            GridColumnCount = 4,
            DefaultLandingView = "SalesTerminal",
            IsDefault = true,
            OrderedCategoryIds = [],
            QuickKeyProductIds = [],
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.TerminalLayoutProfiles.Add(defaultProfile);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return defaultProfile;
    }

    public async Task<TerminalLayoutProfile> SaveProfileAsync(TerminalLayoutProfile profile, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.ProfileName);

        var existing = await _dbContext.TerminalLayoutProfiles.FindAsync([profile.Id], ct).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.ProfileName = profile.ProfileName.Trim();
            existing.OrderedCategoryIds = profile.OrderedCategoryIds ?? [];
            existing.QuickKeyProductIds = profile.QuickKeyProductIds ?? [];
            existing.GridColumnCount = profile.GridColumnCount is >= 2 and <= 6 ? profile.GridColumnCount : 4;
            existing.DefaultLandingView = profile.DefaultLandingView;
            existing.IsDefault = profile.IsDefault;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            existing = profile;
            _dbContext.TerminalLayoutProfiles.Add(existing);
        }

        if (existing.IsDefault)
        {
            var otherDefaults = await _dbContext.TerminalLayoutProfiles
                .Where(p => p.Id != existing.Id && p.IsDefault)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var other in otherDefaults)
            {
                other.IsDefault = false;
            }
        }

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return existing;
    }

    public async Task<IReadOnlyList<TerminalLayoutProfile>> GetAllProfilesAsync(CancellationToken ct = default)
    {
        return await _dbContext.TerminalLayoutProfiles
            .AsNoTracking()
            .OrderBy(p => p.ProfileName)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
