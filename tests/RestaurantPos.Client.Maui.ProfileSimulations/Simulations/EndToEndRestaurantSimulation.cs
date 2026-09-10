using System.Linq;
using System.Threading.Tasks;
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
/// End-to-end multi-profile simulation (P1 — User Story 1).
/// Validates that Waiter -> Kitchen Staff -> Floor Manager -> Cashier flows
/// execute seamlessly via the shared backend state without external network or database dependencies.
/// </summary>
public class EndToEndRestaurantSimulation
{
    private readonly FakePlatformEnvironmentService _env = new();
    private readonly FakeLocalJournalService _journal = new();
    private readonly FakeKitchenSignalRClient _signalRClient = new();
    private readonly SharedFakeBackend _sharedBackend;

    public EndToEndRestaurantSimulation()
    {
        _sharedBackend = new SharedFakeBackend(_signalRClient);
    }

    [Fact]
    public async Task E2E_FullRestaurantLifecycle_ShouldSucceed()
    {
        const string tableNumber = "T01";

        // ═════════════════════════════════════════════════════════════════════
        // STAGE 1: Waiter (Order Taking & Kitchen Dispatch)
        // ═════════════════════════════════════════════════════════════════════

        // 1.1 Authenticate Waiter
        var waiterAuth = new FakeOperatorAuthenticationService(SimulatedOperator.Waiter());
        var waiterPinVm = new PinLockViewModel(_env, waiterAuth);
        foreach (char digit in "1111")
            await waiterPinVm.AppendDigitAsync(digit.ToString());

        waiterPinVm.IsAuthenticated.Should().BeTrue();
        waiterPinVm.CurrentOperatorRole.Should().Be(UserRole.Waiter);

        // 1.2 Waiter opens table T01 for 2 covers
        var openedTable = await _sharedBackend.OpenTableAsync(
            tableNumber,
            coversCount: 2,
            operatorId: SimulatedOperator.Waiter().OperatorId,
            operatorName: "Sophie Durand"
        );
        openedTable.Status.Should().Be(TableStatus.Occupied);
        openedTable.CoversCount.Should().Be(2);

        // 1.3 Waiter opens POS terminal and adds products to cart
        var waiterPosVm = new PosTerminalViewModel(_env, _journal);
        waiterPosVm.ActiveTable = tableNumber;

        var espresso = waiterPosVm.AvailableProducts.First(p => p.Name == "Café Espresso");
        var burger = waiterPosVm.AvailableProducts.First(p => p.Name == "Burger Maison & Frites");

        await waiterPosVm.AddProductAsync(espresso);
        await waiterPosVm.AddProductAsync(burger);

        waiterPosVm.CartItems.Should().HaveCount(2);
        waiterPosVm.TotalTtc.AmountInCents.Should().Be(1900); // 2.50 + 16.50 = 19.00 EUR

        // 1.4 Setup KDS ViewModel before dispatch to receive SignalR push event
        var kdsVm = new KdsViewModel(_env, routingService: _sharedBackend, signalRClient: _signalRClient);

        // 1.5 Waiter dispatches order to kitchen
        await waiterPosVm.SendKitchenAndResetAsync(_sharedBackend);

        // Assert Stage 1: Waiter cart is reset, backend order & kitchen queue are populated
        waiterPosVm.CartItems.Should().BeEmpty();
        waiterPosVm.TotalTtc.AmountInCents.Should().Be(0);

        _sharedBackend.ActiveOrders.Should().ContainKey(tableNumber);
        var activeOrder = _sharedBackend.ActiveOrders[tableNumber];
        activeOrder.Lines.Should().HaveCount(2);
        activeOrder.Lines.All(l => l.IsDispatched).Should().BeTrue();
        _sharedBackend.KitchenQueue.Should().HaveCount(1);

        // ═════════════════════════════════════════════════════════════════════
        // STAGE 2: Kitchen Staff (KDS Preparation & Completion)
        // ═════════════════════════════════════════════════════════════════════

        // 2.1 Authenticate Kitchen Staff
        var kitchenAuth = new FakeOperatorAuthenticationService(SimulatedOperator.KitchenStaff());
        var kitchenPinVm = new PinLockViewModel(_env, kitchenAuth);
        foreach (char digit in "3333")
            await kitchenPinVm.AppendDigitAsync(digit.ToString());

        kitchenPinVm.IsAuthenticated.Should().BeTrue();
        kitchenPinVm.CurrentOperatorRole.Should().Be(UserRole.KitchenStaff);

        // 2.2 Verify KDS received the ticket via SignalR client mock
        kdsVm.PendingTickets.Should().HaveCount(1);
        var kdsTicket = kdsVm.PendingTickets.First();
        kdsTicket.Ticket.TableNumber.Should().Be(tableNumber);
        kdsTicket.Ticket.Items.Should().HaveCount(2);
        kdsTicket.Ticket.Status.Should().Be(TicketStatus.Pending);

        // 2.3 Kitchen staff bumps ticket from Pending to InPreparation
        await kdsVm.BumpTicketAsync(kdsTicket);

        kdsVm.PendingTickets.Should().BeEmpty();
        kdsVm.InPrepTickets.Should().HaveCount(1);
        kdsTicket.Ticket.Status.Should().Be(TicketStatus.InPreparation);

        // 2.4 Kitchen staff bumps ticket from InPreparation to Ready
        await kdsVm.BumpTicketAsync(kdsTicket);

        kdsVm.InPrepTickets.Should().BeEmpty();
        kdsVm.ReadyTickets.Should().HaveCount(1);
        kdsTicket.Ticket.Status.Should().Be(TicketStatus.Ready);
        _sharedBackend.KitchenQueue.First().Status.Should().Be(TicketStatus.Ready);

        // ═════════════════════════════════════════════════════════════════════
        // STAGE 3: Floor Manager (Order Verification & Table State)
        // ═════════════════════════════════════════════════════════════════════

        // 3.1 Authenticate Floor Manager
        var managerAuth = new FakeOperatorAuthenticationService(SimulatedOperator.FloorManager());
        var managerPinVm = new PinLockViewModel(_env, managerAuth);
        foreach (char digit in "4444")
            await managerPinVm.AppendDigitAsync(digit.ToString());

        managerPinVm.IsAuthenticated.Should().BeTrue();
        managerPinVm.CurrentOperatorRole.Should().Be(UserRole.FloorManager);

        // 3.2 Floor Manager inspects active table order in POS Terminal
        var managerPosVm = new PosTerminalViewModel(_env, _journal);
        await managerPosVm.LoadActiveTableOrderAsync(tableNumber, _sharedBackend);

        managerPosVm.CartItems.Should().HaveCount(2);
        managerPosVm.CartItems.All(i => i.IsDispatched).Should().BeTrue();
        managerPosVm.TotalTtc.AmountInCents.Should().Be(1900);

        // ═════════════════════════════════════════════════════════════════════
        // STAGE 4: Cashier (Checkout & Settlement)
        // ═════════════════════════════════════════════════════════════════════

        // 4.1 Authenticate Cashier
        var cashierAuth = new FakeOperatorAuthenticationService(SimulatedOperator.Cashier());
        var cashierPinVm = new PinLockViewModel(_env, cashierAuth);
        foreach (char digit in "2222")
            await cashierPinVm.AppendDigitAsync(digit.ToString());

        cashierPinVm.IsAuthenticated.Should().BeTrue();
        cashierPinVm.CurrentOperatorRole.Should().Be(UserRole.Cashier);

        // 4.2 Cashier retrieves active table order from backend
        var finalOrder = await _sharedBackend.GetActiveOrderForTableAsync(tableNumber);
        finalOrder.Should().NotBeNull();
        long totalDueCents = (long)(finalOrder!.TotalTtcAmount * 100);
        totalDueCents.Should().Be(1900);

        // 4.3 Cashier completes payment via CheckoutViewModel
        var checkoutVm = new CheckoutViewModel(_env, _sharedBackend);
        checkoutVm.Initialize(finalOrder.OrderId, totalDueCents);

        checkoutVm.SelectPaymentMethod(PaymentMethod.CreditCard);
        await checkoutVm.FinalizeCheckoutAsync();

        // 4.4 Assert final state: checkout completed, receipt issued, table freed in backend
        checkoutVm.IsCompleted.Should().BeTrue();
        checkoutVm.RemainingBalanceCents.Should().Be(0);
        checkoutVm.ReceiptNumber.Should().NotBeNullOrEmpty();
        checkoutVm.ReceiptNumber.Should().Contain("POS01-SIM-");

        _sharedBackend.ActiveOrders.Should().NotContainKey(tableNumber);
        _sharedBackend.Tables[tableNumber].Status.Should().Be(TableStatus.Free);
        _sharedBackend.ProcessedPayments.Should().HaveCount(1);
        _sharedBackend.ProcessedPayments.First().TotalPaidCents.Should().Be(1900);
    }
}
