using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Application.Common.Interfaces;

public interface IHappyHourPricingService
{
    Task<HappyHourStatusDto> GetCurrentStatusAsync(string terminalId = "POS_MAIN", CancellationToken cancellationToken = default);

    Task<HappyHourPricingTableDto> GetPricingTableAsync(string terminalId = "POS_MAIN", CancellationToken cancellationToken = default);

    Task<(Money EffectivePrice, bool IsHappyHour, Money? OriginalPrice, Guid? ScheduleId)> ResolveItemPriceAsync(
        Guid productId,
        string categoryId,
        Money standardPrice,
        bool isTakeaway = false,
        string terminalId = "POS_MAIN",
        CancellationToken cancellationToken = default);

    Task<OverrideActionResponse> ActivateOverrideAsync(
        ActivateOverrideRequest request,
        CancellationToken cancellationToken = default);

    Task<OverrideActionResponse> StopOverrideAsync(
        StopOverrideRequest request,
        CancellationToken cancellationToken = default);

    Task<BatchPriceRulesResponseDto> ApplyBatchPriceRulesAsync(
        Guid scheduleId,
        BatchPriceRulesRequestDto request,
        CancellationToken cancellationToken = default);

    Task<BatchDeleteRulesResponseDto> DeleteBatchPriceRulesAsync(
        Guid scheduleId,
        BatchDeleteRulesRequestDto request,
        CancellationToken cancellationToken = default);
}
