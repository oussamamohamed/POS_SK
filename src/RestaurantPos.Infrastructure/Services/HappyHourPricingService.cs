using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class HappyHourPricingService : IHappyHourPricingService
{
    private readonly AppDbContext _dbContext;
    private readonly IOperatorAuthenticationService _authService;

    public HappyHourPricingService(AppDbContext dbContext, IOperatorAuthenticationService authService)
    {
        _dbContext = dbContext;
        _authService = authService;
    }

    public async Task<HappyHourStatusDto> GetCurrentStatusAsync(string terminalId = "POS_MAIN", CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        // Check active override session first
        var activeOverrides = await _dbContext.HappyHourOverrideSessions
            .Where(o => (o.TerminalId == terminalId || o.TerminalId == "POS_MAIN" || o.TerminalId == "POS_MAIN_TERM")
                        && o.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var activeOverride = activeOverrides
            .Where(o => o.ExpiresAtUtc > nowUtc)
            .OrderByDescending(o => o.ExpiresAtUtc)
            .FirstOrDefault();

        if (activeOverride != null)
        {
            int remaining = (int)Math.Max(0, Math.Ceiling((activeOverride.ExpiresAtUtc - nowUtc).TotalMinutes));
            var overrideDto = new HappyHourOverrideDetailDto(
                activeOverride.Id,
                activeOverride.OperatorName,
                activeOverride.StartsAtUtc,
                activeOverride.ExpiresAtUtc,
                activeOverride.Reason
            );

            // Fetch any schedule name for context, or use default
            var firstSchedule = await _dbContext.HappyHourSchedules.FirstOrDefaultAsync(s => s.IsActive, cancellationToken).ConfigureAwait(false);

            return new HappyHourStatusDto(
                IsActive: true,
                IsOverride: true,
                ActiveScheduleName: firstSchedule?.Name ?? "Dérogation Responsable",
                ActiveScheduleId: firstSchedule?.Id,
                AppliesToTakeaway: firstSchedule?.AppliesToTakeaway ?? false,
                CurrentWindow: new HappyHourWindowDto(
                    activeOverride.StartsAtUtc.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    activeOverride.ExpiresAtUtc.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    remaining
                ),
                OverrideDetails: overrideDto
            );
        }

        // Check regular schedules by local time
        // Local restaurant time: TimeZoneInfo Local or Utc
        var localNow = DateTime.Now;
        var currentDay = localNow.DayOfWeek;
        var currentTime = TimeOnly.FromDateTime(localNow);

        var schedules = await _dbContext.HappyHourSchedules
            .Include(s => s.PriceRules)
            .Where(s => s.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var activeSchedule = schedules
            .Where(s => s.DaysOfWeek.Contains(currentDay)
                        && s.StartTime <= currentTime
                        && currentTime < s.EndTime)
            .OrderByDescending(s => s.Priority)
            .FirstOrDefault();

        if (activeSchedule != null)
        {
            var endDateTime = localNow.Date.Add(activeSchedule.EndTime.ToTimeSpan());
            int remainingMinutes = (int)Math.Max(0, Math.Ceiling((endDateTime - localNow).TotalMinutes));

            return new HappyHourStatusDto(
                IsActive: true,
                IsOverride: false,
                ActiveScheduleName: activeSchedule.Name,
                ActiveScheduleId: activeSchedule.Id,
                AppliesToTakeaway: activeSchedule.AppliesToTakeaway,
                CurrentWindow: new HappyHourWindowDto(
                    activeSchedule.StartTime.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    activeSchedule.EndTime.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    remainingMinutes
                ),
                OverrideDetails: null
            );
        }

        return new HappyHourStatusDto(
            IsActive: false,
            IsOverride: false,
            ActiveScheduleName: null,
            ActiveScheduleId: null,
            AppliesToTakeaway: false,
            CurrentWindow: null,
            OverrideDetails: null
        );
    }

    public async Task<HappyHourPricingTableDto> GetPricingTableAsync(string terminalId = "POS_MAIN", CancellationToken cancellationToken = default)
    {
        var status = await GetCurrentStatusAsync(terminalId, cancellationToken).ConfigureAwait(false);
        if (!status.IsActive)
        {
            return new HappyHourPricingTableDto(false, DateTimeOffset.UtcNow, new List<HappyHourPricingItemDto>());
        }

        // Get active schedule or highest priority schedule if override
        HappyHourSchedule? schedule = null;
        if (status.ActiveScheduleId.HasValue)
        {
            schedule = await _dbContext.HappyHourSchedules
                .Include(s => s.PriceRules)
                .FirstOrDefaultAsync(s => s.Id == status.ActiveScheduleId.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        if (schedule == null)
        {
            schedule = await _dbContext.HappyHourSchedules
                .Include(s => s.PriceRules)
                .Where(s => s.IsActive)
                .OrderByDescending(s => s.Priority)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (schedule == null || schedule.PriceRules.Count == 0)
        {
            return new HappyHourPricingTableDto(true, DateTimeOffset.UtcNow, new List<HappyHourPricingItemDto>());
        }

        var products = await _dbContext.Products
            .Where(p => p.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = new List<HappyHourPricingItemDto>();

        foreach (var prod in products)
        {
            var (effectivePrice, isHh, originalPrice, _) = CalculatePriceForRule(prod.Id, prod.CategoryId, prod.Price, schedule.PriceRules);
            if (isHh && originalPrice.HasValue && effectivePrice.AmountInCents < originalPrice.Value.AmountInCents)
            {
                var rule = schedule.PriceRules.FirstOrDefault(r => r.TargetType == HappyHourTargetType.Product && string.Equals(r.TargetId, prod.Id.ToString(), StringComparison.OrdinalIgnoreCase))
                        ?? schedule.PriceRules.FirstOrDefault(r => r.TargetType == HappyHourTargetType.Category && (string.Equals(r.TargetId, prod.CategoryId, StringComparison.OrdinalIgnoreCase) || r.TargetName.Equals(prod.CategoryId, StringComparison.OrdinalIgnoreCase)));

                string ruleType = (rule?.PricingMode == HappyHourPricingMode.FixedPrice) ? "FixedPrice" : "CategoryDiscount";

                decimal std = originalPrice.Value.ToDecimal();
                decimal hh = effectivePrice.ToDecimal();
                items.Add(new HappyHourPricingItemDto(
                    prod.Id,
                    prod.Name,
                    std,
                    hh,
                    std - hh,
                    ruleType
                ));
            }
        }

        return new HappyHourPricingTableDto(true, DateTimeOffset.UtcNow, items);
    }

    public async Task<(Money EffectivePrice, bool IsHappyHour, Money? OriginalPrice, Guid? ScheduleId)> ResolveItemPriceAsync(
        Guid productId,
        string categoryId,
        Money standardPrice,
        bool isTakeaway = false,
        string terminalId = "POS_MAIN",
        CancellationToken cancellationToken = default)
    {
        var status = await GetCurrentStatusAsync(terminalId, cancellationToken).ConfigureAwait(false);
        if (!status.IsActive)
        {
            return (standardPrice, false, null, null);
        }

        if (isTakeaway && !status.AppliesToTakeaway)
        {
            return (standardPrice, false, null, null);
        }

        HappyHourSchedule? schedule = null;
        if (status.ActiveScheduleId.HasValue)
        {
            schedule = await _dbContext.HappyHourSchedules
                .Include(s => s.PriceRules)
                .FirstOrDefaultAsync(s => s.Id == status.ActiveScheduleId.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        if (schedule == null)
        {
            schedule = await _dbContext.HappyHourSchedules
                .Include(s => s.PriceRules)
                .Where(s => s.IsActive)
                .OrderByDescending(s => s.Priority)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (schedule == null || schedule.PriceRules.Count == 0)
        {
            return (standardPrice, false, null, null);
        }

        var result = CalculatePriceForRule(productId, categoryId, standardPrice, schedule.PriceRules);
        return (result.EffectivePrice, result.IsHappyHour, result.OriginalPrice, result.IsHappyHour ? schedule.Id : null);
    }

    public async Task<OverrideActionResponse> ActivateOverrideAsync(
        ActivateOverrideRequest request,
        CancellationToken cancellationToken = default)
    {
        var auth = await _authService.AuthenticatePinAsync(request.SupervisorPin, cancellationToken).ConfigureAwait(false);
        if (!auth.IsSuccess || (auth.Role != UserRole.FloorManager && auth.Role != UserRole.Admin))
        {
            return new OverrideActionResponse(
                false,
                null,
                null,
                null,
                null,
                "Autorisation insuffisante : code PIN superviseur ou gérant requis."
            );
        }

        int duration = request.DurationMinutes > 0 ? request.DurationMinutes : 60;
        var nowUtc = DateTimeOffset.UtcNow;
        var expiresAtUtc = nowUtc.AddMinutes(duration);

        // Deactivate previous active override sessions for this terminal
        var existing = await _dbContext.HappyHourOverrideSessions
            .Where(o => o.TerminalId == request.TerminalId && o.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var sess in existing)
        {
            sess.IsActive = false;
        }

        var session = new HappyHourOverrideSession
        {
            TerminalId = request.TerminalId,
            OperatorId = auth.OperatorId ?? Guid.Empty,
            OperatorName = auth.OperatorName ?? "Superviseur",
            OverrideType = HappyHourOverrideType.ForceStart,
            StartsAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
            Reason = request.Reason ?? "Dérogation manuelle responsable",
            IsActive = true
        };
        _dbContext.HappyHourOverrideSessions.Add(session);

        // NF525 JET Audit Entry
        var jet = new TransactionJournalEntry
        {
            TerminalId = request.TerminalId,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            EventType = "EVENT_HAPPY_HOUR_OVERRIDE_ACTIVATED",
            PayloadJson = JsonSerializer.Serialize(new
            {
                SessionId = session.Id,
                DurationMinutes = duration,
                Operator = auth.OperatorName,
                Reason = session.Reason,
                ExpiresAtUtc = session.ExpiresAtUtc
            }),
            EntryHash = "JET_HH_" + Guid.NewGuid().ToString("N")[..16]
        };
        _dbContext.JournalEntries.Add(jet);

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new OverrideActionResponse(
            true,
            session.Id,
            auth.OperatorName,
            nowUtc,
            expiresAtUtc,
            $"Happy Hour activé/prolongé de {duration} minutes avec succès."
        );
    }

    public async Task<OverrideActionResponse> StopOverrideAsync(
        StopOverrideRequest request,
        CancellationToken cancellationToken = default)
    {
        var auth = await _authService.AuthenticatePinAsync(request.SupervisorPin, cancellationToken).ConfigureAwait(false);
        if (!auth.IsSuccess || (auth.Role != UserRole.FloorManager && auth.Role != UserRole.Admin))
        {
            return new OverrideActionResponse(
                false,
                null,
                null,
                null,
                null,
                "Autorisation insuffisante : code PIN superviseur ou gérant requis."
            );
        }

        var existing = await _dbContext.HappyHourOverrideSessions
            .Where(o => o.TerminalId == request.TerminalId && o.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var sess in existing)
        {
            sess.IsActive = false;
        }

        var jet = new TransactionJournalEntry
        {
            TerminalId = request.TerminalId,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            EventType = "EVENT_HAPPY_HOUR_OVERRIDE_STOPPED",
            PayloadJson = JsonSerializer.Serialize(new
            {
                Operator = auth.OperatorName,
                Reason = request.Reason
            }),
            EntryHash = "JET_HH_STOP_" + Guid.NewGuid().ToString("N")[..16]
        };
        _dbContext.JournalEntries.Add(jet);

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new OverrideActionResponse(
            true,
            null,
            auth.OperatorName,
            null,
            null,
            "Dérogation Happy Hour désactivée."
        );
    }

    public static (Money EffectivePrice, bool IsHappyHour, Money? OriginalPrice, Guid? ScheduleId) CalculatePriceForRule(
        Guid productId,
        string categoryId,
        Money standardPrice,
        IEnumerable<HappyHourPriceRule> rules)
    {
        // 1. Product specific FixedPrice rule
        var productFixedRule = rules.FirstOrDefault(r =>
            r.TargetType == HappyHourTargetType.Product
            && string.Equals(r.TargetId, productId.ToString(), StringComparison.OrdinalIgnoreCase)
            && r.PricingMode == HappyHourPricingMode.FixedPrice
            && r.FixedPrice.HasValue);

        if (productFixedRule != null && productFixedRule.FixedPrice!.Value.AmountInCents < standardPrice.AmountInCents)
        {
            return (productFixedRule.FixedPrice.Value, true, standardPrice, productFixedRule.ScheduleId);
        }

        // 2. Product specific PercentageDiscount rule
        var productPctRule = rules.FirstOrDefault(r =>
            r.TargetType == HappyHourTargetType.Product
            && string.Equals(r.TargetId, productId.ToString(), StringComparison.OrdinalIgnoreCase)
            && r.PricingMode == HappyHourPricingMode.PercentageDiscount
            && r.DiscountPercent.HasValue
            && r.DiscountPercent.Value > 0);

        if (productPctRule != null)
        {
            long baseCents = standardPrice.AmountInCents;
            long discountedCents = (long)Math.Round(baseCents * (1.0m - (productPctRule.DiscountPercent!.Value / 100.0m)), MidpointRounding.AwayFromZero);
            var discounted = Money.FromCents(Math.Max(0, discountedCents));
            return (discounted, true, standardPrice, productPctRule.ScheduleId);
        }

        // 3. Category specific PercentageDiscount rule
        var categoryPctRule = rules.FirstOrDefault(r =>
            r.TargetType == HappyHourTargetType.Category
            && (r.TargetId.ToString().Equals(categoryId, StringComparison.OrdinalIgnoreCase) || r.TargetName.Equals(categoryId, StringComparison.OrdinalIgnoreCase))
            && r.PricingMode == HappyHourPricingMode.PercentageDiscount
            && r.DiscountPercent.HasValue
            && r.DiscountPercent.Value > 0);

        if (categoryPctRule != null)
        {
            long baseCents = standardPrice.AmountInCents;
            long discountedCents = (long)Math.Round(baseCents * (1.0m - (categoryPctRule.DiscountPercent!.Value / 100.0m)), MidpointRounding.AwayFromZero);
            var discounted = Money.FromCents(Math.Max(0, discountedCents));
            return (discounted, true, standardPrice, categoryPctRule.ScheduleId);
        }

        // 4. Category specific FixedPrice rule (if any)
        var categoryFixedRule = rules.FirstOrDefault(r =>
            r.TargetType == HappyHourTargetType.Category
            && (r.TargetId.ToString().Equals(categoryId, StringComparison.OrdinalIgnoreCase) || r.TargetName.Equals(categoryId, StringComparison.OrdinalIgnoreCase))
            && r.PricingMode == HappyHourPricingMode.FixedPrice
            && r.FixedPrice.HasValue);

        if (categoryFixedRule != null && categoryFixedRule.FixedPrice!.Value.AmountInCents < standardPrice.AmountInCents)
        {
            return (categoryFixedRule.FixedPrice.Value, true, standardPrice, categoryFixedRule.ScheduleId);
        }

        return (standardPrice, false, null, null);
    }
}
