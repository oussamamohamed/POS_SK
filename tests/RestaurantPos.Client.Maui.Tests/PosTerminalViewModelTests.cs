using FluentAssertions;
using Moq;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class PosTerminalViewModelTests
{
    private readonly Mock<IPlatformEnvironmentService> _envMock = new();
    private readonly Mock<ILocalJournalService> _journalMock = new();

    [Fact]
    public async Task AddProductAsyncIncrementsQuantityIfProductAlreadyInCart()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var product = vm.AvailableProducts.First();

        // Act - Tap 1: Add product
        await vm.AddProductAsync(product);
        // Act - Tap 2: Tap product again
        await vm.AddProductAsync(product);

        // Assert
        vm.CartItems.Should().HaveCount(1);
        vm.CartItems.First().Quantity.Should().Be(2);
        vm.TotalTtc.AmountInCents.Should().Be(product.Price.AmountInCents * 2);
    }

    [Fact]
    public void SelectCategorySwitchesActiveCategoryFilter()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var mainsCat = vm.Categories.First(c => c.Id == "CAT-MAINS");

        // Act
        vm.SelectCategory(mainsCat);

        // Assert
        vm.SelectedCategory.Should().Be(mainsCat);
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.LightTap), Times.Once);
    }

    [Fact]
    public async Task LoadActiveTableOrderAsync_WithActiveOrder_ShouldHydrateCartItemsAndTotals()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var tableServiceMock = new Mock<Application.Common.Interfaces.ITableManagementService>();

        var sampleOrder = new Application.Common.Interfaces.ActiveTableOrderDto(
            Guid.NewGuid(),
            "T02",
            "Sophie",
            3,
            DateTimeOffset.UtcNow,
            [
                new Application.Common.Interfaces.ActiveOrderLineDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Salade César",
                    2,
                    9.50m,
                    19.00m,
                    10.0m,
                    "COLD",
                    true,
                    []
                ),
                new Application.Common.Interfaces.ActiveOrderLineDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Burger Rossini",
                    1,
                    19.50m,
                    19.50m,
                    10.0m,
                    "HOT_KITCHEN",
                    true,
                    ["Cuisson : À Point"]
                )
            ],
            35.00m,
            3.50m,
            38.50m
        );

        tableServiceMock
            .Setup(s => s.GetActiveOrderForTableAsync("T02", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleOrder);

        // Act
        await vm.LoadActiveTableOrderAsync("T02", tableServiceMock.Object);

        // Assert
        vm.ActiveTable.Should().Be("T02");
        vm.CartItems.Should().HaveCount(2);
        vm.CartItems[0].ProductName.Should().Be("Salade César");
        vm.CartItems[0].Quantity.Should().Be(2);
        vm.CartItems[1].SelectedModifiers.Should().Contain("Cuisson : À Point");
        vm.TotalTtc.ToDecimal().Should().Be(38.50m);
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.LightTap), Times.Once);
    }

    [Fact]
    public void ClearCart_WithMixedDispatchedAndPendingItems_ShouldOnlyRemovePendingItems()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        vm.CartItems.Add(new OrderItem
        {
            ProductName = "Salade César",
            Quantity = 2,
            UnitPrice = Money.FromDecimal(9.50m),
            IsDispatched = true
        });
        vm.CartItems.Add(new OrderItem
        {
            ProductName = "Tiramisu Maison",
            Quantity = 2,
            UnitPrice = Money.FromDecimal(7.50m),
            IsDispatched = false
        });
        vm.RecalculateTotals();
        vm.TotalTtc.ToDecimal().Should().Be(34.00m);

        // Act
        vm.ClearCart();

        // Assert
        vm.CartItems.Should().HaveCount(1);
        vm.CartItems[0].ProductName.Should().Be("Salade César");
        vm.CartItems[0].IsDispatched.Should().BeTrue();
        vm.TotalTtc.ToDecimal().Should().Be(19.00m);
        vm.ConflictAlertBanner.Should().Contain("conservés");
    }

    [Fact]
    public void ClearCart_WithOnlyDispatchedItems_ShouldRetainAllItemsAndShowWarning()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        vm.CartItems.Add(new OrderItem
        {
            ProductName = "Burger Rossini",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(19.50m),
            IsDispatched = true
        });
        vm.RecalculateTotals();

        // Act
        vm.ClearCart();

        // Assert
        vm.CartItems.Should().HaveCount(1);
        vm.TotalTtc.ToDecimal().Should().Be(19.50m);
        vm.ConflictAlertBanner.Should().Contain("déjà transmis en cuisine");
    }

    [Fact]
    public void ClearCart_WithOnlyPendingItems_ShouldClearEntireCart()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        vm.CartItems.Add(new OrderItem
        {
            ProductName = "Bière Pression",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(5.50m),
            IsDispatched = false
        });
        vm.RecalculateTotals();

        // Act
        vm.ClearCart();

        // Assert
        vm.CartItems.Should().BeEmpty();
        vm.TotalTtc.ToDecimal().Should().Be(0.00m);
    }

    [Fact]
    public async Task SendKitchenAndResetAsync_ShouldDispatchAndClearCart()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var tableServiceMock = new Mock<Application.Common.Interfaces.ITableManagementService>();
        vm.ActiveTable = "T02";
        vm.CartItems.Add(new OrderItem
        {
            ProductId = Guid.NewGuid(),
            ProductName = "Tiramisu Maison",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(7.50m),
            IsDispatched = false
        });
        vm.RecalculateTotals();

        // Act
        await vm.SendKitchenAndResetAsync(tableServiceMock.Object);

        // Assert
        tableServiceMock.Verify(s => s.AddOrUpdateTableOrderItemsAsync("T02", It.IsAny<IReadOnlyList<Application.Common.Interfaces.OrderItemInputDto>>(), It.IsAny<CancellationToken>()), Times.Once);
        tableServiceMock.Verify(s => s.DispatchOrderLinesAsync("T02", It.IsAny<CancellationToken>()), Times.Once);
        vm.CartItems.Should().BeEmpty();
        vm.TotalTtc.ToDecimal().Should().Be(0.00m);
    }

    [Fact]
    public async Task CompItemAsync_MarksItemAsCompAndRecalculatesTotals()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var item = new OrderItem
        {
            ProductName = "Café Espresso",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(2.50m)
        };
        vm.CartItems.Add(item);
        vm.RecalculateTotals();
        vm.TotalTtc.ToDecimal().Should().Be(2.50m);

        // Act
        await vm.CompItemAsync(item, "Geste commercial fidélité");

        // Assert
        item.IsComp.Should().BeTrue();
        item.CompReason.Should().Be("Geste commercial fidélité");
        vm.TotalTtc.ToDecimal().Should().Be(0.00m);
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.LightTap), Times.Once);
    }

    [Fact]
    public async Task ApplyGlobalDiscountAsync_FixedAmount_ReducesTotalCorrectly()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        vm.CartItems.Add(new OrderItem
        {
            ProductName = "Entrecôte Grillée",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(22.00m)
        });
        vm.RecalculateTotals();

        // Act: Apply 5.00 EUR fixed discount
        await vm.ApplyGlobalDiscountAsync(DiscountType.FixedAmount, 5.00m, "Bon d'achat");

        // Assert
        vm.TotalTtc.ToDecimal().Should().Be(17.00m);
        vm.ActiveOrder.GlobalDiscountType.Should().Be(DiscountType.FixedAmount);
        vm.ActiveOrder.GlobalDiscountValue.Should().Be(5.00m);
    }

    [Fact]
    public async Task LoadActiveTableOrderAsync_WithInjectedServiceAndSingleArg_ShouldHydrateCart()
    {
        // Arrange: Injected via constructor, called with single arg (as in PosTerminalPage)
        var tableServiceMock = new Mock<Application.Common.Interfaces.ITableManagementService>();
        var sampleOrder = new Application.Common.Interfaces.ActiveTableOrderDto(
            Guid.NewGuid(),
            "T02",
            "Sophie",
            3,
            DateTimeOffset.UtcNow,
            [
                new Application.Common.Interfaces.ActiveOrderLineDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Salade César",
                    2,
                    9.50m,
                    19.00m,
                    10.0m,
                    "COLD",
                    true,
                    []
                )
            ],
            17.27m,
            1.73m,
            19.00m
        );

        tableServiceMock
            .Setup(s => s.GetActiveOrderForTableAsync("T02", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleOrder);

        var vm = new PosTerminalViewModel(
            _envMock.Object,
            _journalMock.Object,
            tableService: tableServiceMock.Object);

        // Act: called with single argument tableNumber
        await vm.LoadActiveTableOrderAsync("T02");

        // Assert
        vm.ActiveTable.Should().Be("T02");
        vm.CartItems.Should().HaveCount(1);
        vm.CartItems[0].ProductName.Should().Be("Salade César");
        vm.TotalTtc.ToDecimal().Should().Be(19.00m);
    }

    [Fact]
    public async Task RecallTable_AfterKitchenDispatch_ShouldRetainDispatchedItems()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        await vm.LoadActiveTableOrderAsync("T01");

        var burger = vm.AvailableProducts.First(p => p.Name.Contains("Burger"));
        await vm.AddProductAsync(burger);
        vm.CartItems.Should().HaveCount(1);

        // Act 1: Send to kitchen
        await vm.SendKitchenAndResetAsync();
        vm.CartItems.Should().BeEmpty(); // Screen resets for next operation

        // Act 2: Switch to table T02
        await vm.LoadActiveTableOrderAsync("T02");
        vm.ActiveTable.Should().Be("T02");
        vm.CartItems.Should().BeEmpty();

        // Act 3: Recall table T01
        await vm.LoadActiveTableOrderAsync("T01");

        // Assert: T01 cart is restored with dispatched items!
        vm.ActiveTable.Should().Be("T01");
        vm.CartItems.Should().HaveCount(1);
        vm.CartItems[0].ProductName.Should().Be(burger.Name);
        vm.CartItems[0].IsDispatched.Should().BeTrue();
        vm.TotalTtc.ToDecimal().Should().Be(burger.Price.ToDecimal());
    }

    [Fact]
    public async Task SwitchingTables_WithPendingItems_ShouldPreserveBothTables()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);

        // Table 1
        await vm.LoadActiveTableOrderAsync("T01");
        var burger = vm.AvailableProducts.First(p => p.Name.Contains("Burger"));
        await vm.AddProductAsync(burger);

        // Table 2
        await vm.LoadActiveTableOrderAsync("T02");
        var coffee = vm.AvailableProducts.First(p => p.Name.Contains("Café"));
        await vm.AddProductAsync(coffee);

        // Act: Recall Table 1
        await vm.LoadActiveTableOrderAsync("T01");
        vm.CartItems.Should().HaveCount(1);
        vm.CartItems[0].ProductName.Should().Be(burger.Name);

        // Act: Recall Table 2
        await vm.LoadActiveTableOrderAsync("T02");
        vm.CartItems.Should().HaveCount(1);
        vm.CartItems[0].ProductName.Should().Be(coffee.Name);
    }
}
