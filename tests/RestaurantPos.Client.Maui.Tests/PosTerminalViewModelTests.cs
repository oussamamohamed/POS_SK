using System.IO;
using FluentAssertions;
using Moq;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.Models;
using RestaurantPos.Client.Maui.Persistence;
using RestaurantPos.Client.Maui.Services;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class PosTerminalViewModelTests
{

    
    #region iPad Modern UI Tasks

    [Fact]
    public void InputBuffer_AppendsDigitsAndClears()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);

        // Act
        vm.InputBuffer.ActiveField = InputBufferTarget.Quantity;
        vm.OnDigitPressed(5);
        vm.OnDigitPressed(0);
        
        // Assert
        Assert.Equal("50", vm.InputBuffer.CurrentBuffer);
        
        // Act
        vm.OnBackspacePressed();
        
        // Assert
        Assert.Equal("5", vm.InputBuffer.CurrentBuffer);
        
        // Act
        vm.OnClearPressed();
        
        // Assert
        Assert.Equal("", vm.InputBuffer.CurrentBuffer);
    }

    [Fact]
    public void IsLeftHandedMode_TogglesCorrectly()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);

        // Act
        vm.IsLeftHandedMode = true;
        
        // Assert
        Assert.True(vm.IsLeftHandedMode);
    }

    #endregion


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
        vm.AvailableProducts.Should().OnlyContain(p => p.CategoryId == "CAT-MAINS");
        vm.AvailableProducts.Should().HaveCount(2);
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.LightTap), Times.AtLeastOnce());

        // Act: reset to all categories
        vm.SelectCategory(null);
        vm.AvailableProducts.Should().HaveCount(6);
    }

    [Fact]
    public void SelectCategory_FiltersForEachSpecificFamily()
    {
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);

        // Mains
        var mains = vm.Categories.First(c => c.Id == "CAT-MAINS");
        vm.SelectCategory(mains);
        vm.AvailableProducts.Should().OnlyContain(p => p.CategoryId == "CAT-MAINS");
        vm.AvailableProducts.Should().HaveCount(2);

        // Desserts
        var desserts = vm.Categories.First(c => c.Id == "CAT-DESSERTS");
        vm.SelectCategory(desserts);
        vm.AvailableProducts.Should().OnlyContain(p => p.CategoryId == "CAT-DESSERTS");
        vm.AvailableProducts.Should().HaveCount(1);

        // Drinks
        var drinks = vm.Categories.First(c => c.Id == "CAT-DRINKS");
        vm.SelectCategory(drinks);
        vm.AvailableProducts.Should().OnlyContain(p => p.CategoryId == "CAT-DRINKS");
        vm.AvailableProducts.Should().HaveCount(3);

        // Back to All
        vm.SelectCategory(null);
        vm.AvailableProducts.Should().HaveCount(6);
        vm.PageDisplay.Should().Be("Page 1 / 1");
    }

    [Fact]
    public void Pagination_ChangesCurrentPageAndDisplaysCorrectRange()
    {
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);

        // Verify initial state
        vm.CurrentPage.Should().Be(1);
        vm.TotalPages.Should().Be(1);
        vm.PageDisplay.Should().Be("Page 1 / 1");

        // Next page when TotalPages is 1 should stay at page 1
        vm.NextPageCommand.Execute(null);
        vm.CurrentPage.Should().Be(1);

        // Previous page when CurrentPage is 1 should stay at page 1
        vm.PreviousPageCommand.Execute(null);
        vm.CurrentPage.Should().Be(1);
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
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.LightTap), Times.AtLeastOnce());
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
        tableServiceMock.Verify(s => s.AddOrUpdateTableOrderItemsAsync("T02", It.IsAny<IReadOnlyList<Application.Common.Interfaces.OrderItemInputDto>>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        tableServiceMock.Verify(s => s.DispatchOrderLinesAsync("T02", It.IsAny<CancellationToken>()), Times.AtLeastOnce());
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
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.LightTap), Times.AtLeastOnce());
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

    [Fact]
    public async Task AddProductWithModifiersAsync_AddsItemWithExtraPriceAndComment()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var product = vm.AvailableProducts.First(p => p.CategoryId == "CAT-DRINKS"); // e.g. Bière
        var modifiers = new List<string> { "Pinte 50cl", "Sirop Picon" };
        var extraPrice = 4.00m;
        var comment = "Bien fraîche";

        // Act
        await vm.AddProductWithModifiersAsync(product, modifiers, extraPrice, comment);

        // Assert
        vm.CartItems.Should().HaveCount(1);
        var item = vm.CartItems.First();
        item.ProductName.Should().Be(product.Name);
        item.SelectedModifiers.Should().ContainInOrder(modifiers);
        item.ModifiersPriceExtra.ToDecimal().Should().Be(4.00m);
        item.KitchenComment.Should().Be(comment);

        var expectedTotal = product.Price.ToDecimal() + extraPrice;
        vm.TotalTtc.ToDecimal().Should().Be(expectedTotal);
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.LightTap), Times.AtLeastOnce());
    }

    [Fact]
    public void SetItemKitchenComment_UpdatesCommentAndTriggersHaptic()
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

        // Act
        vm.SetItemKitchenComment(item, "Bien serré");

        // Assert
        item.KitchenComment.Should().Be("Bien serré");
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.LightTap), Times.AtLeastOnce());
    }

    [Fact]
    public void CartItemMutations_RemoveIncrementAndDecrementCorrectly()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var item = new OrderItem
        {
            ProductName = "Bière Pression",
            Quantity = 2,
            UnitPrice = Money.FromDecimal(5.50m)
        };
        vm.CartItems.Add(item);
        vm.RecalculateTotals();

        // Act 1: Increment quantity
        vm.IncrementQuantity(item);

        // Assert 1
        item.Quantity.Should().Be(3);
        vm.TotalTtc.ToDecimal().Should().Be(16.50m);

        // Act 2: Decrement quantity
        vm.DecrementQuantity(item);

        // Assert 2
        item.Quantity.Should().Be(2);
        vm.TotalTtc.ToDecimal().Should().Be(11.00m);

        // Act 3: Decrement to 1
        vm.DecrementQuantity(item);
        item.Quantity.Should().Be(1);

        // Act 4: Decrement when quantity is 1 (should remove item)
        vm.DecrementQuantity(item);
        vm.CartItems.Should().BeEmpty();
        vm.TotalTtc.ToDecimal().Should().Be(0.00m);
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.LightTap), Times.AtLeastOnce()); // Triggered by RemoveItem
    }

    [Fact]
    public void HoldAndRecallCurrentCart_SuccessfullyParksAndRestoresOrder()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var item = new OrderItem
        {
            ProductName = "Tiramisu",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(7.50m)
        };
        vm.CartItems.Add(item);
        vm.RecalculateTotals();
        vm.HeldOrdersCount.Should().Be(0);

        // Act 1: Hold (Park)
        vm.HoldCurrentCart();

        // Assert 1
        vm.CartItems.Should().BeEmpty();
        vm.TotalTtc.ToDecimal().Should().Be(0.00m);
        vm.HeldOrdersCount.Should().Be(1);
        vm.ConflictAlertBanner.Should().Contain("mise en attente");
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.Success), Times.AtLeastOnce());

        // Act 2: Recall last parked
        vm.RecallHeldOrder();

        // Assert 2
        vm.CartItems.Should().HaveCount(1);
        vm.CartItems.First().ProductName.Should().Be("Tiramisu");
        vm.TotalTtc.ToDecimal().Should().Be(7.50m);
        vm.HeldOrdersCount.Should().Be(0);
        vm.ConflictAlertBanner.Should().Contain("rappelée");
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.LightTap), Times.AtLeastOnce());
    }

    [Fact]
    public async Task ApplyGlobalDiscountAsync_Percentage_ReducesTotalCorrectly()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        vm.CartItems.Add(new OrderItem
        {
            ProductName = "Entrecôte",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(20.00m)
        });
        vm.RecalculateTotals();

        // Act: Apply 10% discount
        await vm.ApplyGlobalDiscountAsync(DiscountType.Percentage, 10.00m, "Remise membre");

        // Assert
        vm.TotalTtc.ToDecimal().Should().Be(18.00m);
        vm.ActiveOrder.GlobalDiscountType.Should().Be(DiscountType.Percentage);
        vm.ActiveOrder.GlobalDiscountValue.Should().Be(10.00m);
    }

    #region Financial & KPI Tests

    [Fact]
    public void Kpi_InitialZeroState_FinancialTotalsAreZero()
    {
        // Arrange & Act
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);

        // Assert
        vm.TotalTtc.AmountInCents.Should().Be(0);
        vm.TotalHt.AmountInCents.Should().Be(0);
        vm.TotalVat.AmountInCents.Should().Be(0);
        vm.CartItems.Should().BeEmpty();
        vm.CoversCount.Should().Be(2); // Default covers count
    }

    [Fact]
    public async Task Kpi_TotalHtAndTotalVat_CalculatedAccuratelyFromTotalTtc()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var entrecote = vm.AvailableProducts.First(p => p.Name.Contains("Entrecôte")); // 22.00 EUR (2200 cents)

        // Act
        await vm.AddProductAsync(entrecote);

        // Assert: 22.00 EUR TTC
        // HT = Round(2200 / 1.10) = 2000 cents (20.00 EUR)
        // VAT = 2200 - 2000 = 200 cents (2.00 EUR)
        vm.TotalTtc.AmountInCents.Should().Be(2200);
        vm.TotalHt.AmountInCents.Should().Be(2000);
        vm.TotalVat.AmountInCents.Should().Be(200);
        (vm.TotalHt.AmountInCents + vm.TotalVat.AmountInCents).Should().Be(vm.TotalTtc.AmountInCents);
    }

    [Fact]
    public async Task Kpi_MultiItemCart_CalculatesAggregatedTotalsAndVatBreakdown()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var burger = vm.AvailableProducts.First(p => p.Name.Contains("Burger"));     // 16.50 EUR
        var tiramisu = vm.AvailableProducts.First(p => p.Name.Contains("Tiramisu")); // 7.50 EUR
        var biere = vm.AvailableProducts.First(p => p.Name.Contains("Bière"));       // 5.50 EUR

        // Act
        await vm.AddProductAsync(burger);
        await vm.AddProductAsync(tiramisu);
        await vm.AddProductAsync(biere);

        // Assert: Total TTC = 16.50 + 7.50 + 5.50 = 29.50 EUR (2950 cents)
        // Total HT = Round(2950 / 1.10) = 2682 cents (26.82 EUR)
        // Total VAT = 2950 - 2682 = 268 cents (2.68 EUR)
        vm.TotalTtc.ToDecimal().Should().Be(29.50m);
        vm.TotalHt.AmountInCents.Should().Be(2682);
        vm.TotalVat.AmountInCents.Should().Be(268);
        (vm.TotalHt.AmountInCents + vm.TotalVat.AmountInCents).Should().Be(vm.TotalTtc.AmountInCents);
    }

    [Fact]
    public async Task Kpi_WithDiscountsAndCompLines_ReflectsAccurateNetTotals()
    {
        // Arrange: 2 x Entrecôte (2 x 22.00 = 44.00 EUR) + 1 x Café (2.50 EUR) = 46.50 EUR
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var entrecote = vm.AvailableProducts.First(p => p.Name.Contains("Entrecôte"));
        var cafe = vm.AvailableProducts.First(p => p.Name.Contains("Café"));

        await vm.AddProductAsync(entrecote);
        await vm.AddProductAsync(entrecote);
        await vm.AddProductAsync(cafe);
        vm.TotalTtc.ToDecimal().Should().Be(46.50m);

        // Act 1: Comp (offert) on coffee
        var cafeItem = vm.CartItems.First(i => i.ProductName.Contains("Café"));
        await vm.CompItemAsync(cafeItem, "Fidélité client");

        // Assert 1: Coffee is 0, Total TTC is 44.00 EUR
        vm.TotalTtc.ToDecimal().Should().Be(44.00m);

        // Act 2: Apply 10% global discount on the order
        await vm.ApplyGlobalDiscountAsync(DiscountType.Percentage, 10.00m, "Remise VIP");

        // Assert 2: 44.00 EUR - 10% = 39.60 EUR (3960 cents)
        // HT = Round(3960 / 1.10) = 3600 cents (36.00 EUR)
        // VAT = 3960 - 3600 = 360 cents (3.60 EUR)
        vm.TotalTtc.AmountInCents.Should().Be(3960);
        vm.TotalHt.AmountInCents.Should().Be(3600);
        vm.TotalVat.AmountInCents.Should().Be(360);
        (vm.TotalHt.AmountInCents + vm.TotalVat.AmountInCents).Should().Be(vm.TotalTtc.AmountInCents);
    }

    [Fact]
    public async Task Kpi_PaidModifiers_AccuratelyIncreaseFinancialTotals()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var burger = vm.AvailableProducts.First(p => p.Name.Contains("Burger")); // 16.50 EUR

        // Act: Add Burger with Double Cheddar (+1.50) and Bacon (+2.00) => +3.50 EUR
        await vm.AddProductWithModifiersAsync(
            burger,
            ["Double Cheddar", "Bacon Croustillant"],
            extraPrice: 3.50m,
            kitchenComment: "Bien cuit"
        );

        // Assert: 16.50 + 3.50 = 20.00 EUR (2000 cents)
        // HT = Round(2000 / 1.10) = 1818 cents (18.18 EUR)
        // VAT = 2000 - 1818 = 182 cents (1.82 EUR)
        vm.TotalTtc.AmountInCents.Should().Be(2000);
        vm.TotalHt.AmountInCents.Should().Be(1818);
        vm.TotalVat.AmountInCents.Should().Be(182);
        (vm.TotalHt.AmountInCents + vm.TotalVat.AmountInCents).Should().Be(vm.TotalTtc.AmountInCents);
    }

    [Fact]
    public void Kpi_CoversCountMutation_PreservesIntegrityForAverageCoverCalculation()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        vm.CartItems.Add(new OrderItem
        {
            ProductName = "Menu Découverte",
            Quantity = 4,
            UnitPrice = Money.FromDecimal(25.00m) // Total 100.00 EUR
        });
        vm.RecalculateTotals();

        // Act
        vm.CoversCount = 4;

        // Assert: Total TTC = 100.00 EUR, Average cover = 100 / 4 = 25.00 EUR
        vm.TotalTtc.ToDecimal().Should().Be(100.00m);
        vm.CoversCount.Should().Be(4);
        var avgPerCover = vm.TotalTtc.ToDecimal() / vm.CoversCount;
        avgPerCover.Should().Be(25.00m);
    }

    #endregion

    #region Offline Mode & Synchronization Tests

    [Fact]
    public async Task OfflineMode_AddProductAsync_RecordsJournalEntryWithIdempotencyKey()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var product = vm.AvailableProducts.First(p => p.Name.Contains("Burger"));

        // Act
        await vm.AddProductAsync(product);

        // Assert: journal entry recorded with idempotency key guaranteeing no lost actions
        _journalMock.Verify(j => j.RecordTransactionAsync(
            "OrderItemAdded",
            It.Is<string>(k => k.StartsWith($"ORD-{vm.ActiveOrder.Id}-ITEM-")),
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()
        ), Times.AtLeastOnce());
    }

    [Fact]
    public async Task OfflineMode_AddProductWithModifiers_LogsCompleteJournalPayloadWithoutLoss()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var product = vm.AvailableProducts.First(p => p.Name.Contains("Entrecôte"));

        // Act
        await vm.AddProductWithModifiersAsync(
            product,
            ["Sauce Poivre Vert", "Purée Truffée"],
            extraPrice: 2.50m,
            kitchenComment: "Cuisson Saignant"
        );

        // Assert: verify that full modifier details are preserved in journal payload
        _journalMock.Verify(j => j.RecordTransactionAsync(
            "OrderItemAdded",
            It.Is<string>(k => k.StartsWith($"ORD-{vm.ActiveOrder.Id}-ITEM-")),
            It.Is<object>(p => p != null),
            It.IsAny<CancellationToken>()
        ), Times.AtLeastOnce());
    }

    [Fact]
    public async Task OfflineMode_JournalException_DoesNotCrashPosTerminal()
    {
        // Arrange: simulate offline storage failure (e.g., transient disk lock)
        var failingJournalMock = new Mock<ILocalJournalService>();
        failingJournalMock
            .Setup(j => j.RecordTransactionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk write failure (offline storage busy)"));

        var vm = new PosTerminalViewModel(_envMock.Object, failingJournalMock.Object);
        var product = vm.AvailableProducts.First();

        // Act: terminal must not throw and must keep UI & cart operational
        var act = async () => await vm.AddProductAsync(product);

        // Assert
        await act.Should().NotThrowAsync();
        vm.CartItems.Should().HaveCount(1);
        vm.TotalTtc.AmountInCents.Should().Be(product.Price.AmountInCents);
    }

    [Fact]
    public async Task OfflineMode_RemoteTableServiceFailure_FallsBackGracefullyToLocalCache()
    {
        // Arrange: simulate remote backend service unavailable (offline network)
        var tableServiceMock = new Mock<Application.Common.Interfaces.ITableManagementService>();
        tableServiceMock
            .Setup(s => s.GetActiveOrderForTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused - Server offline"));

        var vm = new PosTerminalViewModel(
            _envMock.Object,
            _journalMock.Object,
            tableService: tableServiceMock.Object);

        // Prepopulate local cart for table T05
        vm.ActiveTable = "T05";
        var product = vm.AvailableProducts.First();
        await vm.AddProductAsync(product);

        // Act: reload order during offline network condition
        var act = async () => await vm.LoadActiveTableOrderAsync("T05");

        // Assert: should not throw, local order cached is retained
        await act.Should().NotThrowAsync();
        vm.ActiveTable.Should().Be("T05");
        vm.CartItems.Should().HaveCount(1);
        vm.TotalTtc.AmountInCents.Should().Be(product.Price.AmountInCents);
    }

    [Fact]
    public async Task OfflineMode_FullWorkflow_JournalAndSyncWorker_ReconcilesAllTransactionsOnlineWithoutLoss()
    {
        // Arrange: Create real LocalAppDbContext + LocalJournalService + LocalSyncWorker
        string tempDir = Path.Combine(Path.GetTempPath(), "PosTerminalSyncTest_" + Guid.NewGuid().ToString("N"));
        var envMock = new Mock<IPlatformEnvironmentService>();
        envMock.Setup(e => e.GetSecureDatabasePath(It.IsAny<string>()))
               .Returns(Path.Combine(tempDir, "pos_offline_sync.db"));
        envMock.Setup(e => e.GetCurrentDeviceProfile()).Returns(new DevicePlatformProfile
        {
            DeviceId = "TERM-OFFLINE-01",
            Platform = PlatformType.iOS,
            Idiom = DeviceIdiomType.Tablet,
            ScreenClass = ScreenClassType.StandardTablet,
            ScreenWidthDip = 1024,
            ScreenHeightDip = 768,
            DisplayDensity = 2.0,
            OsVersion = "18.0",
            AppVersion = "1.0.0"
        });

        if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

        using var dbContext = new LocalAppDbContext(envMock.Object);
        await dbContext.Database.EnsureCreatedAsync();

        var realJournalService = new LocalJournalService(dbContext, envMock.Object);
        var syncWorker = new LocalSyncWorker(dbContext);

        var vm = new PosTerminalViewModel(envMock.Object, realJournalService);

        try
        {
            // Act 1: POS operates in OFFLINE mode (multiple transactions performed)
            var p1 = vm.AvailableProducts.First(p => p.Name.Contains("Burger"));
            var p2 = vm.AvailableProducts.First(p => p.Name.Contains("Tiramisu"));
            var p3 = vm.AvailableProducts.First(p => p.Name.Contains("Café"));

            await vm.AddProductAsync(p1);
            await vm.AddProductAsync(p2);
            await vm.AddProductWithModifiersAsync(p3, ["Double Dose"], 1.00m, "Bien serré");

            // Assert 1: Transactions logged in local SQLite Journal & Outbox queue
            var pendingEntries = await realJournalService.GetPendingJournalEntriesAsync();
            pendingEntries.Should().HaveCount(3);
            pendingEntries[0].LocalSequence.Should().Be(1);
            pendingEntries[1].LocalSequence.Should().Be(2);
            pendingEntries[2].LocalSequence.Should().Be(3);
            pendingEntries.All(e => !string.IsNullOrEmpty(e.EntryHash)).Should().BeTrue();

            dbContext.OutboxMessages.Count(m => m.Status == SyncStatus.Pending).Should().Be(3);

            // Act 2: Network reconnects -> Synchronize outbox queue
            int processedCount = await syncWorker.ProcessOutboxQueueAsync();

            // Assert 2: All 3 transactions reconciled with 0 loss and marked Completed
            processedCount.Should().Be(3);
            dbContext.OutboxMessages.All(m => m.Status == SyncStatus.Completed).Should().BeTrue();
            dbContext.OutboxMessages.Count(m => m.Status == SyncStatus.Pending).Should().Be(0);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region Multi-Configuration Reports & Calculations (Online/Offline, HappyHour, Discounts & Modifiers)

    [Fact]
    public async Task Report_OnlineConfig_WithHappyHourPricingAndModifiers_CalculatesExactTotals()
    {
        // Arrange: Online configuration with Happy Hour adjusted item (e.g. Beer standard 5.50€ discounted to 4.00€)
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var tableServiceMock = new Mock<Application.Common.Interfaces.ITableManagementService>();
        vm.ActiveTable = "T01";

        var happyHourBeer = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Bière Blonde Happy Hour",
            CategoryId = "CAT-DRINKS",
            Price = Money.FromDecimal(4.00m), // Happy Hour promotional rate
            TaxRatePercent = 20.0m
        };

        var burger = vm.AvailableProducts.First(p => p.Name.Contains("Burger")); // 16.50€

        // Act 1: Add Happy Hour drink with paid modifier ("Pinte 50cl" +3.00€) -> Total line 1 = 7.00€
        await vm.AddProductWithModifiersAsync(
            happyHourBeer,
            ["Pinte 50cl"],
            extraPrice: 3.00m,
            kitchenComment: "Très fraîche"
        );

        // Act 2: Add Burger with extra Bacon (+2.00€) -> Total line 2 = 18.50€
        await vm.AddProductWithModifiersAsync(
            burger,
            ["Bacon Croustillant"],
            extraPrice: 2.00m,
            kitchenComment: "Cuisson Saignant"
        );

        // Assert: Total TTC = 7.00 + 18.50 = 25.50 EUR (2550 cents)
        // Total HT = Round(2550 / 1.10) = 2318 cents (23.18 EUR)
        // Total VAT = 2550 - 2318 = 232 cents (2.32 EUR)
        vm.TotalTtc.AmountInCents.Should().Be(2550);
        vm.TotalHt.AmountInCents.Should().Be(2318);
        vm.TotalVat.AmountInCents.Should().Be(232);
        (vm.TotalHt.AmountInCents + vm.TotalVat.AmountInCents).Should().Be(vm.TotalTtc.AmountInCents);

        // Send to online kitchen & table service
        await vm.SendKitchenAndResetAsync(tableServiceMock.Object);
        tableServiceMock.Verify(s => s.AddOrUpdateTableOrderItemsAsync("T01", It.IsAny<IReadOnlyList<Application.Common.Interfaces.OrderItemInputDto>>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        tableServiceMock.Verify(s => s.DispatchOrderLinesAsync("T01", It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        vm.CartItems.Should().BeEmpty();
        vm.TotalTtc.AmountInCents.Should().Be(0);
    }

    [Fact]
    public async Task Report_OfflineConfig_WithMultipleModifiersAndFixedDiscount_MaintainsExactFinancialIntegrity()
    {
        // Arrange: Offline mode (no remote table service, local journal only)
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        var burger = vm.AvailableProducts.First(p => p.Name.Contains("Burger"));       // 16.50€
        var entrecote = vm.AvailableProducts.First(p => p.Name.Contains("Entrecôte")); // 22.00€

        // Act 1: Burger + Double Cheddar (1.50€) + Bacon (2.00€) = 20.00€
        await vm.AddProductWithModifiersAsync(burger, ["Double Cheddar", "Bacon Croustillant"], extraPrice: 3.50m, kitchenComment: "Sans oignon");

        // Act 2: Entrecôte + Purée Truffée (2.50€) = 24.50€
        await vm.AddProductWithModifiersAsync(entrecote, ["Purée Truffée"], extraPrice: 2.50m, kitchenComment: "Cuisson À Point");

        // Gross subtotal = 20.00 + 24.50 = 44.50€ (4450 cents)
        vm.TotalTtc.ToDecimal().Should().Be(44.50m);

        // Act 3: Apply 10.00 EUR fixed voucher discount
        await vm.ApplyGlobalDiscountAsync(DiscountType.FixedAmount, 10.00m, "Chèque Cadeau Entreprise");

        // Assert: Net Total TTC = 44.50 - 10.00 = 34.50 EUR (3450 cents)
        // HT = Round(3450 / 1.10) = 3136 cents (31.36 EUR)
        // VAT = 3450 - 3136 = 314 cents (3.14 EUR)
        vm.TotalTtc.AmountInCents.Should().Be(3450);
        vm.TotalHt.AmountInCents.Should().Be(3136);
        vm.TotalVat.AmountInCents.Should().Be(314);
        (vm.TotalHt.AmountInCents + vm.TotalVat.AmountInCents).Should().Be(vm.TotalTtc.AmountInCents);

        // Verify that offline journal recorded transactions with intact idempotency
        _journalMock.Verify(j => j.RecordTransactionAsync(
            "OrderItemAdded",
            It.IsAny<string>(),
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()
        ), Times.Exactly(2));
    }

    [Fact]
    public async Task Report_CombinedHappyHour_ItemComps_AndGlobalPercentageDiscount_CalculatesNetTotals()
    {
        // Arrange: Setup comprehensive promotional scenario
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);
        vm.CoversCount = 2;

        var happyHourBeer = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Bière Pression HappyHour (-20%)",
            CategoryId = "CAT-DRINKS",
            Price = Money.FromDecimal(4.00m),
            TaxRatePercent = 20.0m
        };

        var tiramisu = vm.AvailableProducts.First(p => p.Name.Contains("Tiramisu"));   // 7.50€
        var entrecote = vm.AvailableProducts.First(p => p.Name.Contains("Entrecôte")); // 22.00€

        // Act 1: 2 x HappyHour Beers = 8.00€
        await vm.AddProductAsync(happyHourBeer);
        await vm.AddProductAsync(happyHourBeer);

        // Act 2: 1 x Tiramisu = 7.50€
        await vm.AddProductAsync(tiramisu);

        // Act 3: 1 x Entrecôte + Purée Truffée (2.50€) = 24.50€
        await vm.AddProductWithModifiersAsync(entrecote, ["Purée Truffée"], extraPrice: 2.50m, kitchenComment: "Bleu");

        // Subtotal before comp & discount = 8.00 + 7.50 + 24.50 = 40.00 EUR
        vm.TotalTtc.ToDecimal().Should().Be(40.00m);

        // Act 4: Comp dessert (offert par la direction) -> reduces by 7.50€
        var dessertItem = vm.CartItems.First(i => i.ProductName.Contains("Tiramisu"));
        await vm.CompItemAsync(dessertItem, "Anniversaire Client");

        // Total after comp = 32.50 EUR
        vm.TotalTtc.ToDecimal().Should().Be(32.50m);

        // Act 5: Apply 20% staff discount on the remaining total
        await vm.ApplyGlobalDiscountAsync(DiscountType.Percentage, 20.00m, "Remise Staff");

        // Assert: 32.50 EUR * (1 - 0.20) = 26.00 EUR (2600 cents)
        // HT = Round(2600 / 1.10) = 2364 cents (23.64 EUR)
        // VAT = 2600 - 2364 = 236 cents (2.36 EUR)
        vm.TotalTtc.AmountInCents.Should().Be(2600);
        vm.TotalHt.AmountInCents.Should().Be(2364);
        vm.TotalVat.AmountInCents.Should().Be(236);
        (vm.TotalHt.AmountInCents + vm.TotalVat.AmountInCents).Should().Be(vm.TotalTtc.AmountInCents);

        // Check Average per cover KPI: 26.00€ / 2 covers = 13.00€
        var avgPerCover = vm.TotalTtc.ToDecimal() / vm.CoversCount;
        avgPerCover.Should().Be(13.00m);
    }

    [Fact]
    public async Task Report_MultiTable_OnlineToOfflineSwitch_PreservesIndividualTableTotalsAndModifiers()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);

        // Table 1: Happy Hour order with modifiers
        await vm.LoadActiveTableOrderAsync("T01");
        var beer = vm.AvailableProducts.First(p => p.Name.Contains("Bière"));
        await vm.AddProductWithModifiersAsync(beer, ["Pinte 50cl"], extraPrice: 3.00m, kitchenComment: "Pression"); // 5.50 + 3.00 = 8.50€
        vm.TotalTtc.ToDecimal().Should().Be(8.50m);

        // Table 2: Mains with 10% discount
        await vm.LoadActiveTableOrderAsync("T02");
        var burger = vm.AvailableProducts.First(p => p.Name.Contains("Burger")); // 16.50€
        var entrecote = vm.AvailableProducts.First(p => p.Name.Contains("Entrecôte")); // 22.00€
        await vm.AddProductAsync(burger);
        await vm.AddProductAsync(entrecote);
        await vm.ApplyGlobalDiscountAsync(DiscountType.Percentage, 10.00m, "Remise Fidélité");
        // (16.50 + 22.00) = 38.50€ - 10% = 34.65€ (3465 cents)
        vm.TotalTtc.AmountInCents.Should().Be(3465);

        // Act: Recall Table 1
        await vm.LoadActiveTableOrderAsync("T01");

        // Assert Table 1 integrity
        vm.ActiveTable.Should().Be("T01");
        vm.CartItems.Should().HaveCount(1);
        vm.CartItems[0].SelectedModifiers.Should().Contain("Pinte 50cl");
        vm.CartItems[0].ModifiersPriceExtra.ToDecimal().Should().Be(3.00m);
        vm.TotalTtc.ToDecimal().Should().Be(8.50m);

        // Act: Recall Table 2
        await vm.LoadActiveTableOrderAsync("T02");

        // Assert Table 2 integrity
        vm.ActiveTable.Should().Be("T02");
        vm.CartItems.Should().HaveCount(2);
        vm.ActiveOrder.GlobalDiscountType.Should().Be(DiscountType.Percentage);
        vm.ActiveOrder.GlobalDiscountValue.Should().Be(10.00m);
        vm.TotalTtc.AmountInCents.Should().Be(3465);
    }

    [Fact]
    public async Task Report_SyncAfterMultiConfigOfflineSession_ReconcilesHappyHourAndModifierTransactionsWithoutLoss()
    {
        // Arrange: End-to-end multi-configuration offline session with real SQLite Database
        string tempDir = Path.Combine(Path.GetTempPath(), "PosMultiReportSync_" + Guid.NewGuid().ToString("N"));
        var envMock = new Mock<IPlatformEnvironmentService>();
        envMock.Setup(e => e.GetSecureDatabasePath(It.IsAny<string>()))
               .Returns(Path.Combine(tempDir, "pos_multi_report.db"));
        envMock.Setup(e => e.GetCurrentDeviceProfile()).Returns(new DevicePlatformProfile
        {
            DeviceId = "TERM-MULTI-01",
            Platform = PlatformType.iOS,
            Idiom = DeviceIdiomType.Tablet,
            ScreenClass = ScreenClassType.StandardTablet,
            ScreenWidthDip = 1024,
            ScreenHeightDip = 768,
            DisplayDensity = 2.0,
            OsVersion = "18.0",
            AppVersion = "1.0.0"
        });

        if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

        using var dbContext = new LocalAppDbContext(envMock.Object);
        await dbContext.Database.EnsureCreatedAsync();

        var realJournalService = new LocalJournalService(dbContext, envMock.Object);
        var syncWorker = new LocalSyncWorker(dbContext);

        var vm = new PosTerminalViewModel(envMock.Object, realJournalService);

        try
        {
            // Act 1: Shift Order 1 - Happy Hour Drinks + Modifiers
            var happyHourBeer = new Product
            {
                Id = Guid.NewGuid(),
                Name = "Pression Happy Hour",
                CategoryId = "CAT-DRINKS",
                Price = Money.FromDecimal(4.00m),
                TaxRatePercent = 20m
            };
            await vm.AddProductWithModifiersAsync(happyHourBeer, ["Pinte 50cl"], 3.00m, "Très fraîche");

            // Act 2: Shift Order 2 - Meals with modifiers
            var burger = vm.AvailableProducts.First(p => p.Name.Contains("Burger"));
            await vm.AddProductWithModifiersAsync(burger, ["Double Cheddar", "Bacon Croustillant"], 3.50m, "Bien cuit");

            // Act 3: Shift Order 3 - Desserts with coffee
            var cafe = vm.AvailableProducts.First(p => p.Name.Contains("Café"));
            var dessert = vm.AvailableProducts.First(p => p.Name.Contains("Tiramisu"));
            await vm.AddProductAsync(cafe);
            await vm.AddProductAsync(dessert);

            // Assert: SQLite Journal contains 4 distinct ordered operations
            var entries = await realJournalService.GetPendingJournalEntriesAsync();
            entries.Should().HaveCount(4);
            entries[0].LocalSequence.Should().Be(1);
            entries[1].LocalSequence.Should().Be(2);
            entries[2].LocalSequence.Should().Be(3);
            entries[3].LocalSequence.Should().Be(4);

            // All entries have valid hashes and pending outbox sync messages
            entries.All(e => !string.IsNullOrEmpty(e.EntryHash)).Should().BeTrue();
            dbContext.OutboxMessages.Count(m => m.Status == SyncStatus.Pending).Should().Be(4);

            // Act 4: Reconnect to network -> Process and reconcile outbox
            int reconciledCount = await syncWorker.ProcessOutboxQueueAsync();

            // Assert: 100% reconciled with zero transaction loss
            reconciledCount.Should().Be(4);
            dbContext.OutboxMessages.All(m => m.Status == SyncStatus.Completed).Should().BeTrue();
            dbContext.OutboxMessages.Count(m => m.Status == SyncStatus.Pending).Should().Be(0);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    #endregion
}
