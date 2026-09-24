using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ProfileSimulations.Fakes;
using RestaurantPos.Client.Maui.ProfileSimulations.Helpers;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Simulations;

/// <summary>
/// Simulation of the KitchenStaff profile (P2).
/// Exercises: PIN auth → KDS ticket lifecycle (Pending → InPrep → Ready) → recall → empty queue.
/// </summary>
public class KitchenStaffProfileSimulation
{
    private readonly FakePlatformEnvironmentService _env = new();
    private readonly FakeOperatorAuthenticationService _auth = new(SimulatedOperator.KitchenStaff());

    private static KitchenTicketDto BuildTicket(string tableNumber = "T05") =>
        new(
            TicketId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            TableNumber: tableNumber,
            ServerName: "Sophie Durand",
            CoversCount: 2,
            StationId: "STATION-ALL",
            Status: TicketStatus.Pending,
            DispatchedAtUtc: DateTimeOffset.UtcNow,
            Items: []);

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthenticateKitchenStaff_WithValidPin_ShouldSucceed()
    {
        var vm = new PinLockViewModel(_env, _auth);

        foreach (char digit in "3333")
            await vm.AppendDigitAsync(digit.ToString());

        vm.IsAuthenticated.Should().BeTrue();
        vm.CurrentOperatorRole.Should().Be(UserRole.KitchenStaff);
        vm.CurrentOperatorName.Should().Be("Pierre Garnier");
    }

    // ── KDS — View Tickets ────────────────────────────────────────────────────

    [Fact]
    public void KitchenStaff_AddThreeTickets_AllShouldBePending()
    {
        var vm = new KdsViewModel(_env);

        vm.AddIncomingTicket(BuildTicket("T01"));
        vm.AddIncomingTicket(BuildTicket("T02"));
        vm.AddIncomingTicket(BuildTicket("T03"));

        vm.PendingTickets.Should().HaveCount(3);
        vm.InPrepTickets.Should().BeEmpty();
        vm.ReadyTickets.Should().BeEmpty();
    }

    // ── KDS — Bump to InPrep ──────────────────────────────────────────────────

    [Fact]
    public async Task KitchenStaff_BumpPendingTicket_ShouldTransitionToInPreparation()
    {
        var vm = new KdsViewModel(_env);
        vm.AddIncomingTicket(BuildTicket());

        await vm.BumpTicketAsync(vm.PendingTickets.First());

        vm.PendingTickets.Should().BeEmpty();
        vm.InPrepTickets.Should().HaveCount(1);
        vm.InPrepTickets.First().Ticket.Status.Should().Be(TicketStatus.InPreparation);
        _env.HapticCalls.Should().Contain(HapticFeedbackType.LightTap);
    }

    // ── KDS — Complete Ticket ─────────────────────────────────────────────────

    [Fact]
    public async Task KitchenStaff_BumpTwice_ShouldMoveTicketToReady()
    {
        var vm = new KdsViewModel(_env);
        vm.AddIncomingTicket(BuildTicket());
        var ticket = vm.PendingTickets.First();

        await vm.BumpTicketAsync(ticket); // Pending → InPrep
        await vm.BumpTicketAsync(ticket); // InPrep  → Ready

        vm.PendingTickets.Should().BeEmpty();
        vm.InPrepTickets.Should().BeEmpty();
        vm.ReadyTickets.Should().HaveCount(1);
        vm.ReadyTickets.First().Ticket.Status.Should().Be(TicketStatus.Ready);
    }

    // ── KDS — Recall ──────────────────────────────────────────────────────────

    [Fact]
    public async Task KitchenStaff_RecallInPrepTicket_ShouldRestoreToPending()
    {
        var vm = new KdsViewModel(_env);
        vm.AddIncomingTicket(BuildTicket());
        var ticket = vm.PendingTickets.First();

        await vm.BumpTicketAsync(ticket);    // → InPrep
        await vm.RecallTicketAsync(ticket);  // → Pending (recall)

        vm.InPrepTickets.Should().BeEmpty();
        vm.PendingTickets.Should().HaveCount(1);
        ticket.Ticket.Status.Should().Be(TicketStatus.Pending);
    }

    // ── KDS — Empty Queue Edge Case ───────────────────────────────────────────

    [Fact]
    public void KitchenStaff_EmptyQueue_ShouldShowNoTickets()
    {
        var vm = new KdsViewModel(_env);

        vm.PendingTickets.Should().BeEmpty();
        vm.InPrepTickets.Should().BeEmpty();
        vm.ReadyTickets.Should().BeEmpty();
        vm.ServedTickets.Should().BeEmpty();
    }

    // ── Permission Denial ─────────────────────────────────────────────────────

    [Fact]
    public void KitchenStaff_DomainPermissions_ShouldDenyFrontOfHouseOperations()
    {
        var user = new User { Name = "Pierre", Role = UserRole.KitchenStaff, PinHash = "h", PinSalt = "s" };
        user.CanVoidItems().Should().BeFalse();
        user.CanPrintZReports().Should().BeFalse();
        user.CanAccessBackOffice().Should().BeFalse();
    }
}
