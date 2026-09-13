using System;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;

namespace RestaurantPos.Application.Common.Interfaces;

public record MealVoucherValidationResult(
    bool IsAllowed,
    decimal MaxAllowedAmount,
    string? ErrorMessage,
    decimal SurplusAmount = 0m
);

public interface IMealVoucherPolicyService
{
    MealVoucherValidationResult ValidateVoucherTender(
        Order order,
        decimal voucherAmount,
        decimal? facialValue,
        MealVoucherOverpaymentPolicy policy,
        decimal maxDailyCap = 25.00m
    );
}
