using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Helpers;

/// <summary>
/// Represents a fixed test persona with a known identity used to seed
/// <see cref="Fakes.FakeOperatorAuthenticationService"/> for each profile simulation.
/// </summary>
public sealed record SimulatedOperator(
    string Name,
    UserRole Role,
    string KnownPin,
    Guid OperatorId)
{
    /// <summary>Sophie Durand — Waiter profile, PIN "1111".</summary>
    public static SimulatedOperator Waiter() =>
        new("Sophie Durand", UserRole.Waiter, "1111", new Guid("00000000-0000-0000-0000-000000000001"));

    /// <summary>Marc Lefebvre — Cashier profile, PIN "2222".</summary>
    public static SimulatedOperator Cashier() =>
        new("Marc Lefebvre", UserRole.Cashier, "2222", new Guid("00000000-0000-0000-0000-000000000002"));

    /// <summary>Pierre Garnier — KitchenStaff profile, PIN "3333".</summary>
    public static SimulatedOperator KitchenStaff() =>
        new("Pierre Garnier", UserRole.KitchenStaff, "3333", new Guid("00000000-0000-0000-0000-000000000003"));

    /// <summary>Isabelle Martin — FloorManager profile, PIN "4444".</summary>
    public static SimulatedOperator FloorManager() =>
        new("Isabelle Martin", UserRole.FloorManager, "4444", new Guid("00000000-0000-0000-0000-000000000004"));

    /// <summary>Alexandre Dupont — Admin profile, PIN "5555".</summary>
    public static SimulatedOperator Admin() =>
        new("Alexandre Dupont", UserRole.Admin, "5555", new Guid("00000000-0000-0000-0000-000000000005"));
}
