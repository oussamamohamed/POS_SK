using System;
using System.Collections.Generic;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Application.DTOs;

public record HappyHourWindowDto(
    string StartTime,
    string EndTime,
    int RemainingMinutes
);

public record HappyHourOverrideDetailDto(
    Guid OverrideSessionId,
    string OperatorName,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Reason
);

public record HappyHourStatusDto(
    bool IsActive,
    bool IsOverride,
    string? ActiveScheduleName,
    Guid? ActiveScheduleId,
    bool AppliesToTakeaway,
    HappyHourWindowDto? CurrentWindow,
    HappyHourOverrideDetailDto? OverrideDetails
);

public record HappyHourPricingItemDto(
    Guid ProductId,
    string ProductName,
    decimal StandardPrice,
    decimal HappyHourPrice,
    decimal DiscountAmount,
    string RuleType
);

public record HappyHourPricingTableDto(
    bool IsActive,
    DateTimeOffset GeneratedAtUtc,
    List<HappyHourPricingItemDto> Items
);

public record ActivateOverrideRequest(
    string TerminalId,
    string SupervisorPin,
    int DurationMinutes,
    string Reason
);

public record StopOverrideRequest(
    string TerminalId,
    string SupervisorPin,
    string Reason
);

public record OverrideActionResponse(
    bool Success,
    Guid? OverrideSessionId,
    string? AuthorizedBy,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string Message
);

public record HappyHourRuleDto(
    Guid? Id,
    HappyHourTargetType TargetType,
    string TargetId,
    string TargetName,
    HappyHourPricingMode PricingMode,
    decimal? FixedPrice,
    decimal? DiscountPercent
);

public record HappyHourScheduleDto(
    Guid? Id,
    string Name,
    List<DayOfWeek> DaysOfWeek,
    string StartTime,
    string EndTime,
    bool IsActive,
    bool AppliesToTakeaway,
    int Priority,
    List<HappyHourRuleDto> PriceRules
);
