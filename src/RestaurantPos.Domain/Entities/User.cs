using System;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

public enum UserRole
{
    Waiter = 0,
    Cashier = 1,
    KitchenStaff = 2,
    FloorManager = 3,
    Admin = 4
}

public class User
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string Name { get; set; }
    public UserRole Role { get; set; } = UserRole.Waiter;
    public required string PinHash { get; set; }
    public required string PinSalt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool CanVoidItems() => Role is UserRole.FloorManager or UserRole.Admin;
    public bool CanPrintZReports() => Role is UserRole.FloorManager or UserRole.Admin;
    public bool CanAccessBackOffice() => Role is UserRole.Admin;
}
