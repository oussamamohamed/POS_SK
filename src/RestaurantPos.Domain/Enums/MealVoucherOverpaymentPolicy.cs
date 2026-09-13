namespace RestaurantPos.Domain.Enums;

public enum MealVoucherOverpaymentPolicy
{
    CapAtBalance = 0,        // Policy A (Default): No change given, voucher consumed, surplus forfeited
    StrictRejection = 1,     // Policy B: Rejects any voucher entry strictly greater than balance due
    CustomerCreditVoucher = 2// Policy C: Generates a non-fiscalized store credit voucher for the excess
}
