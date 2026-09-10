using System;
using System.Collections.Generic;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

public class PrinterConfiguration
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string Name { get; set; }
    public required string IpAddress { get; set; }
    public int Port { get; set; } = 9100;
    public int PaperWidthMm { get; set; } = 80;
    public bool OpenCashDrawerOnReceipt { get; set; }
    public List<string> AssignedStationIds { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
