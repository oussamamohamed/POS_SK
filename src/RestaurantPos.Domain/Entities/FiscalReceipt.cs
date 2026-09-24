using System;
using System.Collections.Generic;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Domain.Entities;

public enum PaymentMethod
{
    Cash = 0,
    CreditCard = 1,
    MealVoucher = 2,
    GiftCard = 3,
    RoomCharge = 4
}

public class PaymentTender
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid FiscalReceiptId { get; set; }
    public PaymentMethod Method { get; set; }
    public Money Amount { get; set; } = Money.Zero();
    public Money Tendered { get; set; } = Money.Zero();
    public Money ChangeGiven { get; set; } = Money.Zero();
}

public class FiscalReceipt
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string TerminalId { get; set; }
    public required string ReceiptNumber { get; set; }
    public Guid OrderId { get; set; }
    public long SequenceNumber { get; set; }
    public Money TotalTtcAmount { get; set; } = Money.Zero();
    public Money TotalHtAmount { get; set; } = Money.Zero();
    public string TaxBreakdownJson { get; set; } = "{}";
    public string PreviousSignatureHash { get; set; } = string.Empty;
    public string SignatureHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    
    // NF525 Void Support
    public bool IsVoid { get; set; }
    public Guid? VoidedReceiptId { get; set; }
    
    public List<PaymentTender> Tenders { get; init; } = [];
}

public class DailyFiscalClosure
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string TerminalId { get; set; }
    public long ClosureSequence { get; set; }
    public DateTimeOffset PeriodStartUtc { get; set; }
    public DateTimeOffset PeriodEndUtc { get; set; }
    public Money TotalSalesTtc { get; set; } = Money.Zero();
    public Money TotalSalesHt { get; set; } = Money.Zero();
    public string TaxesSummaryJson { get; set; } = "{}";
    public string TenderTotalsJson { get; set; } = "{}";
    public long PerpetualGrandTotalCents { get; set; }
    public string PreviousSignatureHash { get; set; } = string.Empty;
    public string SignatureHash { get; set; } = string.Empty;
    public string SealedByUserName { get; set; } = string.Empty;
    public Guid SealedByUserId { get; set; }
}
