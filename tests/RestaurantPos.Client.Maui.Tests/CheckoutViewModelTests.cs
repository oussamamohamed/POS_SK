using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class CheckoutViewModelTests
{
    private readonly Mock<IPlatformEnvironmentService> _envMock = new();

    [Fact]
    public void AddCashFastBillCalculatesChangeAndReducesRemainingBalance()
    {
        // Arrange
        var vm = new CheckoutViewModel(_envMock.Object);
        vm.Initialize(Guid.NewGuid(), 3500); // 35.00 EUR total

        // Act: Tap fast 50€ bill button
        vm.AddCashFastBill(5000);

        // Assert
        vm.RemainingBalanceCents.Should().Be(0);
        vm.ChangeDueCents.Should().Be(1500); // 50 - 35 = 15.00 EUR
        vm.AppliedTenders.Should().HaveCount(1);
        vm.AppliedTenders.First().Method.Should().Be(PaymentMethod.Cash);
    }

    [Fact]
    public async Task FinalizeCheckoutAsyncWithCardSettlesFullBalance()
    {
        // Arrange
        var vm = new CheckoutViewModel(_envMock.Object);
        vm.Initialize(Guid.NewGuid(), 8000); // 80.00 EUR total
        vm.SelectPaymentMethod(PaymentMethod.CreditCard);

        // Act
        await vm.FinalizeCheckoutAsync();

        // Assert
        vm.IsCompleted.Should().BeTrue();
        vm.RemainingBalanceCents.Should().Be(0);
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.Success), Times.Once);
    }

    [Fact]
    public async Task MultiTender_CashPartialThenCard_ShouldCompleteWithZeroBalance()
    {
        // Arrange: 25.00 EUR total bill
        var vm = new CheckoutViewModel(_envMock.Object);
        vm.Initialize(Guid.NewGuid(), 2500);

        // Act 1: Pay 10.00 EUR cash
        vm.AddCashFastBill(1000);
        vm.RemainingBalanceCents.Should().Be(1500);
        vm.ChangeDueCents.Should().Be(0);

        // Act 2: Pay remainder (15.00 EUR) with Credit Card
        vm.SelectPaymentMethod(PaymentMethod.CreditCard);
        await vm.FinalizeCheckoutAsync();

        // Assert
        vm.IsCompleted.Should().BeTrue();
        vm.RemainingBalanceCents.Should().Be(0);
        vm.AppliedTenders.Should().HaveCount(2);
        vm.AppliedTenders[0].Method.Should().Be(PaymentMethod.Cash);
        vm.AppliedTenders[0].AmountCents.Should().Be(1000);
        vm.AppliedTenders[1].Method.Should().Be(PaymentMethod.CreditCard);
        vm.AppliedTenders[1].AmountCents.Should().Be(1500);
    }

    [Fact]
    public async Task FinalizeCheckout_WithRoomCharge_ShouldRecordRoomAndGuestDetails()
    {
        // Arrange
        var roomBillingMock = new Mock<Application.Common.Interfaces.IRoomBillingService>();
        var vm = new CheckoutViewModel(_envMock.Object, null, roomBillingMock.Object);
        var orderId = Guid.NewGuid();
        vm.Initialize(orderId, 4500);
        vm.SelectPaymentMethod(PaymentMethod.RoomCharge);
        vm.RoomNumber = "204";
        vm.GuestName = "Jean Dupont";

        // Act
        await vm.FinalizeCheckoutAsync();

        // Assert
        vm.IsCompleted.Should().BeTrue();
        roomBillingMock.Verify(r => r.PostRoomChargeAsync(
            orderId,
            "T01",
            "204",
            "Jean Dupont",
            It.IsAny<Domain.ValueObjects.Money>(),
            It.IsAny<Domain.ValueObjects.Money>(),
            null,
            null,
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReceiptNumber_IncrementsMonotonicallyAcrossCheckouts()
    {
        var vm1 = new CheckoutViewModel(_envMock.Object);
        vm1.Initialize(Guid.NewGuid(), 1000, "T01");
        await vm1.FinalizeCheckoutAsync();

        var vm2 = new CheckoutViewModel(_envMock.Object);
        vm2.Initialize(Guid.NewGuid(), 2000, "T02");
        await vm2.FinalizeCheckoutAsync();

        vm1.ReceiptNumber.Should().NotBeNullOrEmpty();
        vm2.ReceiptNumber.Should().NotBeNullOrEmpty();
        vm1.ReceiptNumber.Should().NotBe(vm2.ReceiptNumber);
    }

    [Fact]
    public async Task FinalizeCheckoutAsync_WithScopeFactory_CreatesFiscalReceiptInLocalDb()
    {
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CheckoutFiscalTest_" + Guid.NewGuid().ToString("N"));
        var envMock = new Mock<IPlatformEnvironmentService>();
        envMock.Setup(e => e.GetSecureDatabasePath(It.IsAny<string>()))
               .Returns(System.IO.Path.Combine(tempDir, "test_fiscal.db"));

        if (!System.IO.Directory.Exists(tempDir)) System.IO.Directory.CreateDirectory(tempDir);

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(envMock.Object);
        services.AddDbContext<Persistence.LocalAppDbContext>();
        var serviceProvider = services.BuildServiceProvider();

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Persistence.LocalAppDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        var scopeFactory = serviceProvider.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var vm = new CheckoutViewModel(envMock.Object, scopeFactory: scopeFactory);
        vm.Initialize(Guid.NewGuid(), 2500, "T01"); // 25.00 EUR
        vm.SelectPaymentMethod(PaymentMethod.CreditCard);

        await vm.FinalizeCheckoutAsync();

        vm.IsCompleted.Should().BeTrue();
        vm.ReceiptNumber.Should().StartWith("POS01-");

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Persistence.LocalAppDbContext>();
            var receipts = db.FiscalReceipts.ToList();
            receipts.Should().HaveCount(1);
            receipts[0].TotalTtcAmount.AmountInCents.Should().Be(2500);
            receipts[0].ReceiptNumber.Should().Be(vm.ReceiptNumber);
            receipts[0].SignatureHash.Should().NotBeNullOrEmpty();
        }

        try { System.IO.Directory.Delete(tempDir, true); } catch { }
    }

    [Fact]
    public async Task FinalizeCheckoutAsync_FreesDiningTable_AndClearsActiveOrder()
    {
        // Arrange
        var envMock = new Mock<IPlatformEnvironmentService>();
        var journalMock = new Mock<ILocalJournalService>();
        var tableMock = new Mock<Application.Common.Interfaces.ITableManagementService>();

        var floorVm = new FloorPlanViewModel(envMock.Object, tableMock.Object);
        var posVm = new PosTerminalViewModel(envMock.Object, journalMock.Object, tableService: tableMock.Object);

        await posVm.LoadActiveTableOrderAsync("T01");
        var burger = posVm.AvailableProducts.First();
        await posVm.AddProductAsync(burger);
        posVm.CartItems.Should().HaveCount(1);

        floorVm.SetTableStatus("T01", TableStatus.Occupied);

        var checkoutVm = new CheckoutViewModel(
            envMock.Object,
            floorPlanViewModel: floorVm,
            posTerminalViewModel: posVm);

        checkoutVm.Initialize(Guid.NewGuid(), 1650, "T01");
        checkoutVm.SelectPaymentMethod(PaymentMethod.Cash);
        checkoutVm.AddCashFastBill(2000);

        // Act
        await checkoutVm.FinalizeCheckoutAsync();

        // Assert: Table status is liberated to Free, cart is cleared, totals 0
        checkoutVm.IsCompleted.Should().BeTrue();
        floorVm.GetTableStatus("T01").Should().Be(TableStatus.Free);
        posVm.CartItems.Should().BeEmpty();
        posVm.TotalTtc.AmountInCents.Should().Be(0);
    }
}
