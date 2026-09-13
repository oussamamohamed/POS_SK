using System;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

public enum HappyHourOverrideType
{
    ForceStart = 0,
    Extend = 1,
    ForceStop = 2
}

public class HappyHourOverrideSession
{
    public Guid Id { get; set; } = UuidV7.NewGuid();
    public required string TerminalId { get; set; }
    public Guid OperatorId { get; set; }
    public required string OperatorName { get; set; }

    public HappyHourOverrideType OverrideType { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public required string Reason { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
