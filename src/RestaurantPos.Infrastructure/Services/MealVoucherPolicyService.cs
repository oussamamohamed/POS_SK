using System;
using System.Globalization;
using System.Linq;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Infrastructure.Services;

public class MealVoucherPolicyService : IMealVoucherPolicyService
{
    public MealVoucherValidationResult ValidateVoucherTender(
        Order order,
        decimal voucherAmount,
        decimal? facialValue,
        MealVoucherOverpaymentPolicy policy,
        decimal maxDailyCap = 25.00m)
    {
        ArgumentNullException.ThrowIfNull(order);

        // 1. Calculate eligible subtotal (only items with IsFoodVoucherEligible == true)
        decimal eligibleSubtotal = order.Items
            .Where(i => i.IsFoodVoucherEligible)
            .Sum(i => i.CalculateTotalTtc().ToDecimal());

        decimal legalMax = Math.Min(eligibleSubtotal, maxDailyCap);

        if (voucherAmount > legalMax)
        {
            return new MealVoucherValidationResult(
                IsAllowed: false,
                MaxAllowedAmount: legalMax,
                ErrorMessage: Texts.T("errors.meal_voucher_above_legal_cap",
                    ("amount", voucherAmount.ToString("0.00", CultureInfo.InvariantCulture)),
                    ("max", legalMax.ToString("0.00", CultureInfo.InvariantCulture))),
                SurplusAmount: 0m
            );
        }

        // 2. Check facial value overpayment against order balance
        decimal balanceDue = order.TotalTtc.ToDecimal();
        if (facialValue.HasValue && facialValue.Value > balanceDue)
        {
            decimal surplus = facialValue.Value - balanceDue;

            if (policy == MealVoucherOverpaymentPolicy.StrictRejection)
            {
                return new MealVoucherValidationResult(
                    IsAllowed: false,
                    MaxAllowedAmount: balanceDue,
                    ErrorMessage: Texts.T("errors.meal_voucher_overpayment_refused",
                        ("face", facialValue.Value.ToString("0.00", CultureInfo.InvariantCulture)),
                        ("due", balanceDue.ToString("0.00", CultureInfo.InvariantCulture))),
                    SurplusAmount: surplus
                );
            }

            if (policy == MealVoucherOverpaymentPolicy.CustomerCreditVoucher)
            {
                return new MealVoucherValidationResult(
                    IsAllowed: true,
                    MaxAllowedAmount: balanceDue,
                    ErrorMessage: null,
                    SurplusAmount: surplus
                );
            }

            // CapAtBalance (Solution A - Default): surplus is absorbed, change given = 0.00 €
            return new MealVoucherValidationResult(
                IsAllowed: true,
                MaxAllowedAmount: balanceDue,
                ErrorMessage: null,
                SurplusAmount: 0m
            );
        }

        return new MealVoucherValidationResult(
            IsAllowed: true,
            MaxAllowedAmount: legalMax,
            ErrorMessage: null,
            SurplusAmount: 0m
        );
    }
}
