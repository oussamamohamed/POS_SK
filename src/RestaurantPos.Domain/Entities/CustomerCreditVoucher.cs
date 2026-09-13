using System;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Domain.Entities;

public class CustomerCreditVoucher
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string VoucherCode { get; set; }
    public Guid OriginalOrderId { get; set; }
    public required string TerminalId { get; set; }
    public Money Amount { get; set; }
    public DateTimeOffset IssuedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; init; } = DateTimeOffset.UtcNow.AddDays(90);
    public bool IsRedeemed { get; set; }
    public DateTimeOffset? RedeemedAtUtc { get; set; }
    public Guid? RedeemedOrderId { get; set; }
}
