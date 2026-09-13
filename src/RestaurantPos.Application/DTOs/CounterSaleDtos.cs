using System;
using System.Collections.Generic;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;

namespace RestaurantPos.Application.DTOs;

public record DirectCounterOpenRequest(
    string? TerminalId,
    OrderDestination? Destination
);

public record SwitchDestinationRequest(
    OrderDestination Destination
);

public record HoldCounterOrderRequest(
    Guid OrderId,
    string? TerminalId,
    Guid? StaffId,
    string? CustomerLabel
);

public record VoidHeldOrderRequest(
    string SupervisorPin,
    string VoidReason,
    string? TerminalId
);

public record CounterPaymentTender(
    PaymentMethod Method,
    decimal Amount,
    decimal Tendered,
    decimal? FacialValue = null
);

public record CounterCheckoutRequest(
    Guid OrderId,
    string? TerminalId,
    OrderDestination Destination,
    string? PickupBuzzer,
    DateTimeOffset? PickupScheduledAtUtc,
    decimal TipAmount,
    bool RequestFiscalReceiptPrint,
    MealVoucherOverpaymentPolicy? MealVoucherPolicy,
    List<CounterPaymentTender> Tenders
);

public record CustomerCreditVoucherDto(
    string VoucherCode,
    decimal Amount,
    DateTimeOffset ExpiresAtUtc
);

public record CounterCheckoutResponse(
    Guid OrderId,
    string PickupNumber,
    decimal TotalPaid,
    decimal ChangeGiven,
    decimal RemainingBalance,
    string? ReceiptNumber,
    string? FiscalSignature,
    CustomerCreditVoucherDto? IssuedCreditVoucher,
    bool PrintPickupVoucher,
    bool PrintFiscalReceipt,
    bool OpenCashDrawer
);
