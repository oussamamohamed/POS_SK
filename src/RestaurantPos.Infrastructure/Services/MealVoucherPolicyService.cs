using System;
using System.Globalization;
using System.Linq;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;

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
                ErrorMessage: string.Format(CultureInfo.InvariantCulture, "Le montant par Titre-Restaurant ({0:0.00} €) dépasse le plafond légal éligible ({1:0.00} €).", voucherAmount, legalMax),
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
                    ErrorMessage: string.Format(CultureInfo.InvariantCulture, "Surpaiement par Titre-Restaurant refusé : la valeur faciale ({0:0.00} €) dépasse le solde dû ({1:0.00} €).", facialValue.Value, balanceDue),
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
